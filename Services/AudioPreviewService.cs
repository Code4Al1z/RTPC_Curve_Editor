using NAudio.Wave;

namespace RTPCCurveEditor.Services;

/// <summary>Loads an audio file and plays it with live-adjustable volume, filter cutoff, and pitch/rate, driven by the curve.</summary>
public sealed class AudioPreviewService : IDisposable
{
    private readonly AudioFileReader _reader;
    private readonly VariSpeedSampleProvider _variSpeed;
    private readonly FilterSampleProvider _filter;
    private readonly WaveOutEvent _output;
    private bool _manualStop;

    // Position/Duration come from _variSpeed, not _reader — _reader gets fully
    // drained upfront by VariSpeedSampleProvider's constructor (see there), so
    // its own CurrentTime/TotalTime would just be stuck at end-of-file afterward.
    public TimeSpan Duration => _variSpeed.Duration;
    public TimeSpan Position => _variSpeed.Position;

    /// <summary>
    /// Fires when playback stops, for any reason. The bool argument is true if
    /// Stop() was called, false if it just reached the end of the file — fires
    /// on NAudio's playback thread, not the UI thread, so subscribers must
    /// marshal back to the dispatcher before touching bound properties.
    /// </summary>
    public event Action<bool>? PlaybackEnded;

    public AudioPreviewService(string filePath)
    {
        _reader = new AudioFileReader(filePath);
        _variSpeed = new VariSpeedSampleProvider(_reader);
        _filter = new FilterSampleProvider(_variSpeed);
        _output = new WaveOutEvent();
        _output.Init(_filter.ToWaveProvider());
        _output.PlaybackStopped += (_, _) =>
        {
            bool wasManual = _manualStop;
            _manualStop = false;
            PlaybackEnded?.Invoke(wasManual);
        };
    }

    public void Play()
    {
        _variSpeed.Reset();
        _manualStop = false;
        _output.Play();
    }

    public void Stop()
    {
        _manualStop = true;
        _output.Stop();
    }

    /// <summary>Linear gain (1.0 = unity) — safe to call while playing. Applied in FilterSampleProvider, not AudioFileReader.Volume (see there for why).</summary>
    public void SetVolume(float gain) => _filter.Gain = gain;

    public void SetFilterEnabled(bool enabled) => _filter.Enabled = enabled;

    /// <summary>Low-pass cutoff frequency in Hz — safe to call while playing.</summary>
    public void SetFilterCutoffHz(float hz) => _filter.SetCutoffHz(hz);

    /// <summary>Playback rate multiplier (pitch moves with it) — safe to call while playing.</summary>
    public void SetPlaybackRate(float rate) => _variSpeed.SetRate(rate);

    public void Dispose()
    {
        _output.Dispose();
        _reader.Dispose();
    }
}
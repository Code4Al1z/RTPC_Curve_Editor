using NAudio.Wave;

namespace RTPCCurveEditor.Services;

/// <summary>Loads an audio file and plays it with a live-adjustable volume, driven by the curve.</summary>
public sealed class AudioPreviewService : IDisposable
{
    private readonly AudioFileReader _reader;
    private readonly WaveOutEvent _output;
    private bool _manualStop;

    public TimeSpan Duration => _reader.TotalTime;
    public TimeSpan Position => _reader.CurrentTime;

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
        _output = new WaveOutEvent();
        _output.Init(_reader);
        _output.PlaybackStopped += (_, _) =>
        {
            bool wasManual = _manualStop;
            _manualStop = false;
            PlaybackEnded?.Invoke(wasManual);
        };
    }

    public void Play()
    {
        _reader.Position = 0;
        _manualStop = false;
        _output.Play();
    }

    public void Stop()
    {
        _manualStop = true;
        _output.Stop();
    }

    /// <summary>Linear gain (1.0 = unity) — safe to call while playing.</summary>
    public void SetVolume(float gain) => _reader.Volume = gain;

    public void Dispose()
    {
        _output.Dispose();
        _reader.Dispose();
    }
}
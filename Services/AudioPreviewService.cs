using NAudio.Wave;

namespace RTPCCurveEditor.Services;

/// <summary>Loads an audio file and plays it with a live-adjustable volume, driven by the curve.</summary>
public sealed class AudioPreviewService : IDisposable
{
    private readonly AudioFileReader _reader;
    private readonly WaveOutEvent _output;

    public AudioPreviewService(string filePath)
    {
        _reader = new AudioFileReader(filePath);
        _output = new WaveOutEvent();
        _output.Init(_reader);
    }

    public void Play()
    {
        _reader.Position = 0;
        _output.Play();
    }

    public void Stop() => _output.Stop();

    /// <summary>Linear gain (1.0 = unity) — safe to call while playing.</summary>
    public void SetVolume(float gain) => _reader.Volume = gain;

    public void Dispose()
    {
        _output.Dispose();
        _reader.Dispose();
    }
}

using NAudio.Wave;

namespace RTPCCurveEditor.Services;

/// <summary>
/// Changes playback rate (and therefore pitch — they move together, like a
/// sped-up record, not true independent pitch-shifting) by resampling a
/// fully-preloaded copy of the source at a fractional read position.
/// Loads the whole file into memory up front — reasonable for short preview
/// clips, and avoids the bug surface of a streaming variable-rate resampler.
/// </summary>
public sealed class VariSpeedSampleProvider : ISampleProvider
{
    private readonly float[] _data;
    private readonly int _channels;
    private readonly int _totalFrames;
    private double _readFramePos;
    private float _rate = 1.0f;

    public WaveFormat WaveFormat { get; }

    public TimeSpan Duration => TimeSpan.FromSeconds(WaveFormat.SampleRate > 0 ? (double)_totalFrames / WaveFormat.SampleRate : 0);
    public TimeSpan Position => TimeSpan.FromSeconds(WaveFormat.SampleRate > 0 ? _readFramePos / WaveFormat.SampleRate : 0);

    public VariSpeedSampleProvider(ISampleProvider source)
    {
        WaveFormat = source.WaveFormat;
        _channels = Math.Max(1, source.WaveFormat.Channels);

        var chunks = new List<float[]>();
        var readBuffer = new float[_channels * 4096];
        int total = 0;
        int read;
        while ((read = source.Read(readBuffer, 0, readBuffer.Length)) > 0)
        {
            var chunk = new float[read];
            Array.Copy(readBuffer, chunk, read);
            chunks.Add(chunk);
            total += read;
        }

        _data = new float[total];
        int offset = 0;
        foreach (var chunk in chunks)
        {
            Array.Copy(chunk, 0, _data, offset, chunk.Length);
            offset += chunk.Length;
        }

        _totalFrames = _channels > 0 ? _data.Length / _channels : 0;
    }

    /// <summary>Playback rate multiplier — 1.0 = original, 2.0 = an octave up and twice as fast, 0.5 = an octave down and half speed.</summary>
    public void SetRate(float rate) => _rate = Math.Clamp(rate, 0.25f, 4.0f);

    public void Reset() => _readFramePos = 0;

    public int Read(float[] buffer, int offset, int count)
    {
        int framesRequested = count / _channels;
        int framesWritten = 0;

        while (framesWritten < framesRequested)
        {
            int frame = (int)_readFramePos;
            if (frame >= _totalFrames) break;

            for (int ch = 0; ch < _channels; ch++)
                buffer[offset + framesWritten * _channels + ch] = _data[frame * _channels + ch];

            framesWritten++;
            _readFramePos += _rate;
        }

        return framesWritten * _channels;
    }
}

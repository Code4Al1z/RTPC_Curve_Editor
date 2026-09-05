using NAudio.Dsp;
using NAudio.Wave;

namespace RTPCCurveEditor.Services;

/// <summary>
/// Applies a live-adjustable gain and low-pass filter to an audio stream.
/// Filtering is a no-op when disabled; gain always applies (this is now the
/// actual volume control — see AudioPreviewService for why AudioFileReader.Volume
/// isn't used for that anymore). Uses two cascaded BiQuadFilter stages per
/// channel (24dB/octave) rather than one (12dB/octave) — a single stage's
/// effect can be too subtle to clearly hear against broadband material.
/// </summary>
public sealed class FilterSampleProvider : ISampleProvider
{
    private const int StagesPerChannel = 2;

    private readonly ISampleProvider _source;
    private readonly BiQuadFilter[,] _filters; // [channel, stage]
    private readonly int _channels;
    private float _cutoffHz = 20000f;

    public WaveFormat WaveFormat => _source.WaveFormat;
    public bool Enabled { get; set; }
    public float Gain { get; set; } = 1.0f;

    public FilterSampleProvider(ISampleProvider source)
    {
        _source = source;
        _channels = Math.Max(1, source.WaveFormat.Channels);
        _filters = new BiQuadFilter[_channels, StagesPerChannel];
        for (int ch = 0; ch < _channels; ch++)
            for (int stage = 0; stage < StagesPerChannel; stage++)
                _filters[ch, stage] = BiQuadFilter.LowPassFilter(source.WaveFormat.SampleRate, _cutoffHz, 0.707f);
    }

    public void SetCutoffHz(float hz)
    {
        _cutoffHz = Math.Clamp(hz, 20f, 20000f);
        for (int ch = 0; ch < _channels; ch++)
            for (int stage = 0; stage < StagesPerChannel; stage++)
                _filters[ch, stage].SetLowPassFilter(_source.WaveFormat.SampleRate, _cutoffHz, 0.707f);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int samplesRead = _source.Read(buffer, offset, count);

        for (int i = 0; i < samplesRead; i++)
        {
            float sample = buffer[offset + i];
            if (Enabled)
            {
                int ch = i % _channels;
                for (int stage = 0; stage < StagesPerChannel; stage++)
                    sample = _filters[ch, stage].Transform(sample);
            }
            buffer[offset + i] = sample * Gain;
        }

        return samplesRead;
    }
}
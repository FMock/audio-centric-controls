namespace AudioCentric.Controls.Plotting;

/// <summary>
/// Read-only access to recorded audio for a waveform display. The host owns decoding and storage. The display asks
/// only for per-column min/max summaries or, when zoomed in far enough, the individual samples.
/// Implementations must be safe to call from the UI thread and should answer quickly; build summaries in the background.
/// </summary>
public interface IWaveformSource
{
    int SampleRate { get; }
    int ChannelCount { get; }
    long Length { get; }

    /// <summary>Raised (on any thread) when samples or summaries change, e.g. after an edit or while recording.</summary>
    event EventHandler? Changed;

    /// <summary>
    /// Peak-preserving reduction of [start, end) into lo.Length columns using the doc's bucket formula.
    /// Empty buckets are reported as NaN.
    /// </summary>
    void GetMinMax(int channel, long start, long end, Span<float> lo, Span<float> hi);

    /// <summary>Copies samples [start, start + destination.Length) of a channel; samples out of range are 0.</summary>
    void GetSamples(int channel, long start, Span<float> destination);
}

/// <summary>An in-memory recording with a <see cref="MinMaxPyramid"/> per channel.</summary>
public sealed class InMemoryWaveformSource : IWaveformSource
{
    private readonly float[][] _channels;
    private readonly MinMaxPyramid[] _pyramids;

    public InMemoryWaveformSource(int sampleRate, params float[][] channels)
    {
        if (channels.Length == 0) throw new ArgumentException("At least one channel is required.");
        long len = channels[0].Length;
        if (channels.Any(c => c.Length != len)) throw new ArgumentException("All channels must have the same length.");
        SampleRate = sampleRate;
        _channels = channels;
        _pyramids = channels.Select(c => new MinMaxPyramid(c)).ToArray();
    }

    public int SampleRate { get; }
    public int ChannelCount => _channels.Length;
    public long Length => _channels[0].Length;

    public event EventHandler? Changed { add { } remove { } }

    public void GetMinMax(int channel, long start, long end, Span<float> lo, Span<float> hi)
    {
        int columns = lo.Length;
        long count = end - start;
        var pyramid = _pyramids[channel];
        for (int i = 0; i < columns; i++)
        {
            long b0 = start + (long)Math.Floor((double)i * count / columns);
            long b1 = start + (long)Math.Floor((double)(i + 1) * count / columns);
            (lo[i], hi[i]) = b1 > b0 ? pyramid.Range(b0, b1) : (float.NaN, float.NaN);
        }
    }

    public void GetSamples(int channel, long start, Span<float> destination)
    {
        var src = _channels[channel];
        for (int i = 0; i < destination.Length; i++)
        {
            long n = start + i;
            destination[i] = n >= 0 && n < src.Length ? src[n] : 0f;
        }
    }
}

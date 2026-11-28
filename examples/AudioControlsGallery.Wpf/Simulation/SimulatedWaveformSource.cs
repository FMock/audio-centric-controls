using AudioCentric.Controls.Plotting;

namespace AudioControlsGallery.Simulation;

/// <summary>
/// Host-side sample history (about 21 s of stereo at 48 kHz). The engine thread appends each block, and the UI copies
/// recent history for the oscilloscope or captures a recording for the waveform display.
/// Single writer and lock-free: the write position is published with a volatile write after the samples are
/// stored. A reader can only see torn data if it falls a whole ring behind the writer, which cannot happen at
/// these sizes.
/// </summary>
public sealed class SimulatedWaveformSource
{
    private const int Size = 1 << 20;
    private const int Mask = Size - 1;
    private readonly float[] _left = new float[Size];
    private readonly float[] _right = new float[Size];
    private long _written;

    public SimulatedWaveformSource(int sampleRate) => SampleRate = sampleRate;

    public int SampleRate { get; }
    public int Capacity => Size;

    /// <summary>Total samples written so far (absolute index of the next sample).</summary>
    public long Written => Volatile.Read(ref _written);

    /// <summary>Engine thread: append one stereo block.</summary>
    public void Append(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        long w = _written;
        for (int i = 0; i < left.Length; i++)
        {
            int k = (int)((w + i) & Mask);
            _left[k] = left[i];
            _right[k] = right[i];
        }
        Volatile.Write(ref _written, w + left.Length);
    }

    /// <summary>
    /// UI thread: copies the newest <c>left.Length</c> samples (newest last) into the given arrays and returns the
    /// absolute index of the first copied sample.
    /// </summary>
    public long CopyLatest(float[] left, float[]? right)
    {
        int n = Math.Min(left.Length, Size);
        long end = Written;
        long start = end - n;
        for (int i = 0; i < n; i++)
        {
            long abs = start + i;
            if (abs < 0) { left[i] = 0; if (right != null) right[i] = 0; continue; }
            int k = (int)(abs & Mask);
            left[i] = _left[k];
            if (right != null) right[i] = _right[k];
        }
        return start;
    }

    /// <summary>Captures the last <paramref name="seconds"/> as an in-memory recording with min/max pyramids.</summary>
    public IWaveformSource Capture(double seconds)
    {
        int n = (int)Math.Min(Size, Math.Min(Written, (long)(seconds * SampleRate)));
        n = Math.Max(n, 1);
        var l = new float[n];
        var r = new float[n];
        CopyLatest(l, r);
        return new InMemoryWaveformSource(SampleRate, l, r);
    }
}

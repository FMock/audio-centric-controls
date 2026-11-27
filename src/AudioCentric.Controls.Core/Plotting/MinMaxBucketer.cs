namespace AudioCentric.Controls.Plotting;

/// <summary>
/// Peak-preserving waveform overview. For visible samples [s0, s1) and W columns:
/// start(i) = s0 + floor(i (s1 - s0) / W), end(i) = s0 + floor((i + 1)(s1 - s0) / W),
/// lo[i] = min x[n], hi[i] = max x[n] over start(i) &lt;= n &lt; end(i).
/// Empty buckets are never reduced. They are reported as NaN so the renderer can skip or bridge them.
/// </summary>
public static class MinMaxBucketer
{
    /// <summary>
    /// Fills <paramref name="lo"/> and <paramref name="hi"/> (length = column count) from <paramref name="samples"/>.
    /// <paramref name="start"/> and <paramref name="end"/> index into <paramref name="samples"/>.
    /// </summary>
    public static void Reduce(ReadOnlySpan<float> samples, long start, long end, Span<float> lo, Span<float> hi)
    {
        int columns = lo.Length;
        if (hi.Length != columns) throw new ArgumentException("lo and hi must have the same length.");
        if (columns == 0) return;
        start = Math.Clamp(start, 0, samples.Length);
        end = Math.Clamp(end, start, samples.Length);
        long count = end - start;

        for (int i = 0; i < columns; i++)
        {
            long b0 = start + (long)Math.Floor((double)i * count / columns);
            long b1 = start + (long)Math.Floor((double)(i + 1) * count / columns);
            if (b1 <= b0)
            {
                lo[i] = float.NaN;
                hi[i] = float.NaN;
                continue;
            }

            float mn = float.PositiveInfinity, mx = float.NegativeInfinity;
            for (long n = b0; n < b1; n++)
            {
                float x = samples[(int)n];
                if (x < mn) mn = x;
                if (x > mx) mx = x;
            }
            lo[i] = mn;
            hi[i] = mx;
        }
    }

    /// <summary>True when fewer samples than columns are visible, so the renderer should draw individual samples.</summary>
    public static bool ShouldDrawSamples(long visibleSamples, int columns) => visibleSamples <= columns;
}

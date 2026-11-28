namespace AudioCentric.Controls.Plotting;

/// <summary>How several FFT bins combine into one displayed band.</summary>
public enum BandAggregation
{
    /// <summary>Largest bin level in the band. Keeps tone levels (peak calibration) and is the usual analyzer choice.</summary>
    Maximum,
    /// <summary>
    /// Summed bin power divided by the window's noise bandwidth: the band's total level. Broadband noise reads higher in
    /// wider bands, while a single tone reads its own level in any band.
    /// </summary>
    PowerSum,
}

/// <summary>A frequency band with its centre and edges in Hz.</summary>
public readonly record struct FrequencyBand(double Center, double Low, double High);

/// <summary>Base-2 fractional-octave bands referenced to 1 kHz (IEC 61260 style), e.g. the 31 third-octave bands from 20 Hz to 20 kHz.</summary>
public static class FractionalOctaveBands
{
    /// <summary>
    /// Bands of 1/<paramref name="bandsPerOctave"/> octave whose centres lie within a quarter band of
    /// [<paramref name="min"/>, <paramref name="max"/>], so 20 Hz…20 kHz gives the 31 nominal third-octave bands
    /// (exact centres 19.7 Hz … 20.2 kHz).
    /// </summary>
    public static FrequencyBand[] Create(int bandsPerOctave, double min = 20, double max = 20000)
    {
        if (bandsPerOctave < 1) throw new ArgumentOutOfRangeException(nameof(bandsPerOctave));
        if (!(min > 0) || !(max > min)) throw new ArgumentException("Need 0 < min < max.");
        double b = bandsPerOctave;
        int k0 = (int)Math.Ceiling(b * Math.Log2(min / 1000) - 0.25 + 1e-6);
        int k1 = (int)Math.Floor(b * Math.Log2(max / 1000) + 0.25 - 1e-6);
        var bands = new FrequencyBand[Math.Max(0, k1 - k0 + 1)];
        double half = Math.Pow(2, 1 / (2 * b));
        for (int k = k0; k <= k1; k++)
        {
            double c = 1000 * Math.Pow(2, k / b);
            bands[k - k0] = new FrequencyBand(c, c / half, c * half);
        }
        return bands;
    }
}

/// <summary>
/// Reduces a spectrum (ascending bin frequencies and dB levels) to what a display can draw: one value per pixel column,
/// or one value per band. Where a column or band holds several bins the reduction keeps the peaks. Where it holds none
/// (low frequencies on a log axis are wider than a bin), it interpolates between the neighbouring bins on the axis,
/// so the curve stays smooth instead of stair-stepping. Results are NaN outside the data. Allocation-free.
/// </summary>
public static class SpectrumReducer
{
    private const double Floor = -400; // stands in for −∞ while interpolating

    /// <summary>
    /// Fills <paramref name="output"/> with one level per equal-width column spanning the axis. Columns containing bins
    /// take their maximum; empty columns interpolate at the column centre.
    /// </summary>
    public static void ToColumns(ReadOnlySpan<double> frequencies, ReadOnlySpan<double> levelsDb, FrequencyAxis axis, Span<double> output)
    {
        int n = output.Length;
        int count = Math.Min(frequencies.Length, levelsDb.Length);
        int j = 0;
        while (j < count && axis.ToNormalized(frequencies[j]) == double.NegativeInfinity) j++; // DC has no place on a log axis
        int firstUsable = j;

        for (int i = 0; i < n; i++)
        {
            double lo = axis.FromNormalized((double)i / n), hi = axis.FromNormalized((double)(i + 1) / n);
            while (j < count && frequencies[j] < lo) j++;
            double max = double.NaN;
            int k = j;
            for (; k < count && frequencies[k] < hi; k++)
                if (double.IsNaN(max) || levelsDb[k] > max) max = levelsDb[k];
            if (k > j) { output[i] = max; continue; }
            output[i] = Interpolate(frequencies, levelsDb, axis, axis.FromNormalized((i + 0.5) / n), j, firstUsable, count);
        }
    }

    /// <summary>
    /// Fills <paramref name="output"/> with one level per band. Bands without a bin interpolate at their centre.
    /// <paramref name="noiseBandwidthBins"/> is the window's equivalent noise bandwidth (see <see cref="Measurements.SpectrumSnapshot.NoiseBandwidthBins"/>),
    /// used by <see cref="BandAggregation.PowerSum"/>.
    /// </summary>
    public static void ToBands(ReadOnlySpan<double> frequencies, ReadOnlySpan<double> levelsDb, ReadOnlySpan<FrequencyBand> bands,
        BandAggregation aggregation, double noiseBandwidthBins, FrequencyAxis axis, Span<double> output)
    {
        int count = Math.Min(frequencies.Length, levelsDb.Length);
        int firstUsable = 0;
        while (firstUsable < count && !(frequencies[firstUsable] > 0)) firstUsable++;
        double enbw = noiseBandwidthBins > 0 ? noiseBandwidthBins : 1;
        int j = firstUsable;

        for (int b = 0; b < bands.Length && b < output.Length; b++)
        {
            var band = bands[b];
            while (j < count && frequencies[j] < band.Low) j++;
            int k = j;
            double max = double.NegativeInfinity, power = 0;
            for (; k < count && frequencies[k] < band.High; k++)
            {
                double l = levelsDb[k];
                if (double.IsNaN(l)) continue;
                if (l > max) max = l;
                if (l > double.NegativeInfinity) power += Math.Pow(10, l / 10);
            }
            if (k > j)
                output[b] = aggregation == BandAggregation.Maximum ? max : power > 0 ? 10 * Math.Log10(power / enbw) : double.NegativeInfinity;
            else
                output[b] = Interpolate(frequencies, levelsDb, axis, band.Center, j, firstUsable, count);
        }
    }

    /// <summary>Level at <paramref name="hz"/>, interpolated on the axis between bins <paramref name="next"/>−1 and <paramref name="next"/>.</summary>
    private static double Interpolate(ReadOnlySpan<double> f, ReadOnlySpan<double> l, FrequencyAxis axis, double hz, int next, int firstUsable, int count)
    {
        if (next <= firstUsable || next >= count) return double.NaN;
        double u0 = axis.ToNormalized(f[next - 1]), u1 = axis.ToNormalized(f[next]), u = axis.ToNormalized(hz);
        double a = Math.Max(Floor, l[next - 1]), b = Math.Max(Floor, l[next]);
        if (double.IsNaN(a) || double.IsNaN(b)) return double.NaN;
        double t = u1 > u0 ? Math.Clamp((u - u0) / (u1 - u0), 0, 1) : 0;
        double v = a + t * (b - a);
        return v <= Floor + 1e-9 ? double.NegativeInfinity : v;
    }
}

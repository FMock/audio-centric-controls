namespace AudioCentric.Controls.Plotting;

/// <summary>Helpers for frequency-response data: log-spaced grids, phase wrapping and unwrapping, interpolation and crossings.</summary>
public static class ResponseMath
{
    /// <summary><paramref name="count"/> log-spaced frequencies from <paramref name="min"/> to <paramref name="max"/> inclusive.</summary>
    public static double[] LogSpacedFrequencies(double min, double max, int count)
    {
        if (!(min > 0) || !(max > min) || count < 2) throw new ArgumentException("Need 0 < min < max and count ≥ 2.");
        var f = new double[count];
        double r = Math.Log(max / min);
        for (int i = 0; i < count; i++) f[i] = min * Math.Exp(r * i / (count - 1));
        f[^1] = max;
        return f;
    }

    /// <summary>Wraps degrees into (−180, 180].</summary>
    public static double WrapDegrees(double degrees)
    {
        if (double.IsNaN(degrees) || double.IsInfinity(degrees)) return degrees;
        double w = degrees % 360;
        if (w <= -180) w += 360;
        else if (w > 180) w -= 360;
        return w;
    }

    /// <summary>Removes 360° jumps so consecutive points never differ by more than 180°. NaN points are kept and skipped.</summary>
    public static double[] UnwrapDegrees(ReadOnlySpan<double> phase)
    {
        var o = new double[phase.Length];
        double offset = 0, prev = double.NaN;
        for (int i = 0; i < phase.Length; i++)
        {
            double p = phase[i];
            if (double.IsNaN(p)) { o[i] = double.NaN; continue; }
            if (!double.IsNaN(prev))
            {
                double d = p + offset - prev;
                offset -= 360 * Math.Round(d / 360);
            }
            o[i] = p + offset;
            prev = o[i];
        }
        return o;
    }

    /// <summary>Value at <paramref name="hz"/>, interpolated linearly in log frequency. NaN outside the data.</summary>
    public static double Interpolate(ReadOnlySpan<double> frequencies, ReadOnlySpan<double> values, double hz)
    {
        int n = Math.Min(frequencies.Length, values.Length);
        if (n == 0 || !(hz > 0) || hz < frequencies[0] || hz > frequencies[n - 1]) return double.NaN;
        int lo = 0, hi = n - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (frequencies[mid] <= hz) lo = mid; else hi = mid;
        }
        if (hi == lo || frequencies[hi] <= frequencies[lo]) return values[lo];
        double t = Math.Log(hz / frequencies[lo]) / Math.Log(frequencies[hi] / frequencies[lo]);
        return values[lo] + Math.Clamp(t, 0, 1) * (values[hi] - values[lo]);
    }

    /// <summary>
    /// Frequencies where the curve crosses <paramref name="levelDb"/> (e.g. −3 dB cutoff points), interpolated in log
    /// frequency. Segments touching a NaN point are skipped.
    /// </summary>
    public static IReadOnlyList<double> FindCrossings(ReadOnlySpan<double> frequencies, ReadOnlySpan<double> gainDb, double levelDb)
    {
        var result = new List<double>();
        int n = Math.Min(frequencies.Length, gainDb.Length);
        for (int i = 1; i < n; i++)
        {
            double a = gainDb[i - 1] - levelDb, b = gainDb[i] - levelDb;
            if (double.IsNaN(a) || double.IsNaN(b) || a == 0 && i > 1) continue;
            if (a == 0) { result.Add(frequencies[i - 1]); continue; }
            if (Math.Sign(a) == Math.Sign(b) && b != 0) continue;
            if (b == 0) { result.Add(frequencies[i]); continue; }
            double t = a / (a - b);
            if (double.IsInfinity(a) || double.IsInfinity(b)) t = double.IsInfinity(a) ? 1 : 0;
            result.Add(frequencies[i - 1] * Math.Pow(frequencies[i] / frequencies[i - 1], t));
        }
        return result;
    }
}

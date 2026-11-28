namespace AudioCentric.Controls.Plotting;

/// <summary>The 1-2-5 sequence used by oscilloscope time/div and amplitude/div selectors.</summary>
public static class Steps125
{
    /// <summary>All 1-2-5 values from <paramref name="min"/> to <paramref name="max"/> inclusive.</summary>
    public static double[] Range(double min, double max)
    {
        var list = new List<double>();
        for (int exp = (int)Math.Floor(Math.Log10(min)) - 1; exp <= (int)Math.Ceiling(Math.Log10(max)) + 1; exp++)
        {
            foreach (var m in new[] { 1.0, 2.0, 5.0 })
            {
                double v = Math.Round(m * Math.Pow(10, exp), 12);
                if (v >= min * (1 - 1e-9) && v <= max * (1 + 1e-9)) list.Add(v);
            }
        }
        return list.ToArray();
    }

    /// <summary>Engineering text for seconds per division, e.g. "500 µs", "2 ms", "1 s".</summary>
    public static string FormatSeconds(double seconds)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        if (seconds >= 1) return seconds.ToString("0.###", ci) + " s";
        if (seconds >= 1e-3) return (seconds * 1e3).ToString("0.###", ci) + " ms";
        return (seconds * 1e6).ToString("0.###", ci) + " µs";
    }
}

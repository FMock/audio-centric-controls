namespace AudioControlsGallery.Simulation;

/// <summary>
/// Host-side spectral analysis: Hann window, radix-2 FFT and the one-sided amplitude spectrum with peak-tone
/// calibration, A[k] = 2|X[k]|/W for interior bins and |X[k]|/W at DC and Nyquist, where W = Σw[n].
/// A full-scale bin-centred sine reads 0 dBFS. This math belongs to the analysis provider, not to the controls.
/// </summary>
public static class SpectrumAnalysis
{
    /// <summary>Periodic (DFT-even) Hann window, w[n] = 0.5 − 0.5 cos(2πn/N). Its noise bandwidth is exactly 1.5 bins.</summary>
    public static double[] Hann(int n)
    {
        var w = new double[n];
        for (int i = 0; i < n; i++) w[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / n);
        return w;
    }

    /// <summary>Equivalent noise bandwidth in bins: N Σw² / (Σw)².</summary>
    public static double NoiseBandwidthBins(ReadOnlySpan<double> w)
    {
        double s = 0, s2 = 0;
        foreach (var v in w) { s += v; s2 += v * v; }
        return w.Length * s2 / (s * s);
    }

    /// <summary>Bin centre frequencies k·fs/N for k = 0…N/2.</summary>
    public static double[] BinFrequencies(int n, int sampleRate)
    {
        var f = new double[n / 2 + 1];
        for (int k = 0; k < f.Length; k++) f[k] = (double)k * sampleRate / n;
        return f;
    }

    /// <summary>In-place iterative radix-2 complex FFT (unnormalized forward transform, X[k] = Σ x[n] e^(−j2πkn/N)).</summary>
    public static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        if (n != im.Length || n == 0 || (n & (n - 1)) != 0) throw new ArgumentException("Length must be a power of two.");
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len;
            double wr = Math.Cos(ang), wi = Math.Sin(ang);
            int half = len >> 1;
            for (int i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (int k = 0; k < half; k++)
                {
                    int a = i + k, b = a + half;
                    double tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - tr; im[b] = im[a] - ti;
                    re[a] += tr; im[a] += ti;
                    double nr = cr * wr - ci * wi;
                    ci = cr * wi + ci * wr;
                    cr = nr;
                }
            }
        }
    }

    /// <summary>Reference O(N²) DFT for verifying the FFT.</summary>
    public static void Dft(ReadOnlySpan<double> x, double[] re, double[] im)
    {
        int n = x.Length;
        for (int k = 0; k < n; k++)
        {
            double sr = 0, si = 0;
            for (int i = 0; i < n; i++)
            {
                double a = -2 * Math.PI * ((long)k * i % n) / n;
                sr += x[i] * Math.Cos(a);
                si += x[i] * Math.Sin(a);
            }
            re[k] = sr;
            im[k] = si;
        }
    }

    /// <summary>
    /// Windows <paramref name="x"/> (length N), transforms it and writes N/2+1 peak-calibrated levels in dBFS
    /// (−∞ for exact zero) to <paramref name="levelsDb"/>. <paramref name="re"/>/<paramref name="im"/> are N-length scratch buffers.
    /// </summary>
    public static void AmplitudeSpectrumDb(ReadOnlySpan<double> x, double[] window, double[] re, double[] im, double[] levelsDb)
    {
        int n = window.Length;
        double sum = 0;
        for (int i = 0; i < n; i++)
        {
            re[i] = x[i] * window[i];
            im[i] = 0;
            sum += window[i];
        }
        Fft(re, im);
        for (int k = 0; k <= n / 2; k++)
        {
            double mag = Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
            double a = (k == 0 || k == n / 2 ? 1 : 2) * mag / sum;
            levelsDb[k] = a > 0 ? 20 * Math.Log10(a) : double.NegativeInfinity;
        }
    }
}

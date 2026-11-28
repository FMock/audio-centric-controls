namespace AudioCentric.Controls.Plotting;

/// <summary>
/// Multi-resolution min/max summary of one channel for fast overview drawing of long recordings.
/// Level 0 aggregates <see cref="BaseBlock"/> samples per block, and each further level aggregates
/// <see cref="Fanout"/> blocks of the level below. Queries use the coarsest level that fits inside a column and
/// refine partial blocks at the column edges from the source samples, so results match a full scan exactly.
/// </summary>
public sealed class MinMaxPyramid
{
    public const int BaseBlock = 64;
    public const int Fanout = 4;

    private readonly float[] _samples;
    private readonly List<(int BlockSize, float[] Lo, float[] Hi)> _levels = new();

    public MinMaxPyramid(float[] samples)
    {
        _samples = samples;
        int block = BaseBlock;
        float[]? prevLo = null, prevHi = null;
        while (true)
        {
            int count = (samples.Length + block - 1) / block;
            if (count < 1) break;
            var lo = new float[count];
            var hi = new float[count];
            for (int b = 0; b < count; b++)
            {
                float mn = float.PositiveInfinity, mx = float.NegativeInfinity;
                if (prevLo == null)
                {
                    int s0 = b * block, s1 = Math.Min(samples.Length, s0 + block);
                    for (int n = s0; n < s1; n++)
                    {
                        float x = samples[n];
                        if (x < mn) mn = x;
                        if (x > mx) mx = x;
                    }
                }
                else
                {
                    int c0 = b * Fanout, c1 = Math.Min(prevLo.Length, c0 + Fanout);
                    for (int c = c0; c < c1; c++)
                    {
                        if (prevLo[c] < mn) mn = prevLo[c];
                        if (prevHi![c] > mx) mx = prevHi[c];
                    }
                }
                lo[b] = mn;
                hi[b] = mx;
            }
            _levels.Add((block, lo, hi));
            if (count == 1) break;
            prevLo = lo;
            prevHi = hi;
            block *= Fanout;
        }
    }

    public int Length => _samples.Length;
    public int LevelCount => _levels.Count;

    /// <summary>Min and max of samples [start, end). Returns (NaN, NaN) for an empty range.</summary>
    public (float Lo, float Hi) Range(long start, long end)
    {
        long s = Math.Clamp(start, 0, _samples.Length), e = Math.Clamp(end, s, _samples.Length);
        if (e <= s) return (float.NaN, float.NaN);
        float mn = float.PositiveInfinity, mx = float.NegativeInfinity;

        // Coarsest level whose blocks fit at least twice in the range; smaller ranges scan raw samples.
        int level = -1;
        for (int i = _levels.Count - 1; i >= 0; i--)
            if (_levels[i].BlockSize * 2 <= e - s) { level = i; break; }

        if (level < 0)
        {
            Scan(s, e, ref mn, ref mx);
            return (mn, mx);
        }

        var (bs, lo, hi) = _levels[level];
        long firstBlock = (s + bs - 1) / bs, lastBlock = e / bs; // full blocks [firstBlock, lastBlock)
        Scan(s, Math.Min(e, firstBlock * bs), ref mn, ref mx);
        for (long b = firstBlock; b < lastBlock; b++)
        {
            if (lo[b] < mn) mn = lo[b];
            if (hi[b] > mx) mx = hi[b];
        }
        Scan(Math.Max(s, lastBlock * bs), e, ref mn, ref mx);
        return (mn, mx);
    }

    private void Scan(long s, long e, ref float mn, ref float mx)
    {
        for (long n = s; n < e; n++)
        {
            float x = _samples[n];
            if (x < mn) mn = x;
            if (x > mx) mx = x;
        }
    }
}

using AudioCentric.Controls.Measurements;

namespace AudioCentric.Controls.Scales;

/// <summary>
/// Console fader gain law. Interpolates in dB between finite anchors and ramps linearly in amplitude
/// from the lowest finite anchor down to mute at position 0. Proposed default (position, dB):
/// (0, mute), (0.05, -60), (0.25, -30), (0.50, -12), (0.75, 0), (1, +10). Not a console standard.
/// </summary>
public sealed class FaderTaper
{
    private readonly double[] _positions;
    private readonly double[] _db;

    public static FaderTaper Default { get; } = new(new[]
    {
        (0.05, -60.0), (0.25, -30.0), (0.50, -12.0), (0.75, 0.0), (1.0, 10.0),
    });

    /// <param name="finiteAnchors">(position, dB) pairs above the mute ramp. The first position must be greater than 0.</param>
    public FaderTaper(IEnumerable<(double Position, double Db)> finiteAnchors)
    {
        var list = finiteAnchors.OrderBy(a => a.Position).ToArray();
        if (list.Length < 2) throw new ArgumentException("At least two anchors are required.");
        if (list[0].Position <= 0 || list[^1].Position > 1) throw new ArgumentException("Anchor positions must lie in (0, 1].");
        for (int i = 1; i < list.Length; i++)
            if (!(list[i].Position > list[i - 1].Position) || !(list[i].Db > list[i - 1].Db))
                throw new ArgumentException("Anchors must be strictly increasing.");
        _positions = list.Select(a => a.Position).ToArray();
        _db = list.Select(a => a.Db).ToArray();
    }

    public double MinimumFiniteDb => _db[0];
    public double MaximumDb => _db[^1];
    public double RampTopPosition => _positions[0];
    public IReadOnlyList<double> AnchorPositions => _positions;
    public IReadOnlyList<double> AnchorDb => _db;

    /// <summary>Position (0..1) to gain in dB. Position 0 returns -infinity (mute).</summary>
    public double ToDb(double position)
    {
        double p = double.IsNaN(position) ? 0 : Math.Clamp(position, 0, 1);
        if (p <= 0) return double.NegativeInfinity;
        if (p < _positions[0])
            return Decibels.FromAmplitude(Decibels.ToAmplitude(_db[0]) * (p / _positions[0]));
        if (p >= _positions[^1]) return _db[^1];
        int i = Array.BinarySearch(_positions, p);
        if (i >= 0) return _db[i];
        i = ~i;
        double t = (p - _positions[i - 1]) / (_positions[i] - _positions[i - 1]);
        return _db[i - 1] + t * (_db[i] - _db[i - 1]);
    }

    /// <summary>Inverse mapping: gain in dB (or -infinity for mute) to position 0..1.</summary>
    public double ToPosition(double db)
    {
        if (double.IsNaN(db) || double.IsNegativeInfinity(db)) return 0;
        if (db >= _db[^1]) return _positions[^1];
        if (db < _db[0])
            return Math.Clamp(Decibels.ToAmplitude(db) / Decibels.ToAmplitude(_db[0]), 0, 1) * _positions[0];
        int i = Array.BinarySearch(_db, db);
        if (i >= 0) return _positions[i];
        i = ~i;
        double t = (db - _db[i - 1]) / (_db[i] - _db[i - 1]);
        return _positions[i - 1] + t * (_positions[i] - _positions[i - 1]);
    }

    /// <summary>Linear gain factor g = 10^(dB/20) for a position; 0 at mute.</summary>
    public double ToGain(double position) => Decibels.ToAmplitude(ToDb(position));
}

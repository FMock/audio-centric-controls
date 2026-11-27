namespace AudioCentric.Controls.Scales;

/// <summary>
/// Monotonic piecewise-linear mapping through (value, position) anchors. Used for meter scales that give
/// more room near full scale (e.g. -10..0 dBFS) than a uniform dB layout would.
/// </summary>
public sealed class PiecewiseLinearMapping : IValueMapping
{
    private readonly double[] _values;
    private readonly double[] _positions;

    public PiecewiseLinearMapping(IEnumerable<(double Value, double Position)> anchors)
    {
        var list = anchors.OrderBy(a => a.Value).ToArray();
        if (list.Length < 2) throw new ArgumentException("At least two anchors are required.", nameof(anchors));
        for (int i = 1; i < list.Length; i++)
        {
            if (!(list[i].Value > list[i - 1].Value) || !(list[i].Position > list[i - 1].Position))
                throw new ArgumentException("Anchors must be strictly increasing in value and position.", nameof(anchors));
        }
        _values = list.Select(a => a.Value).ToArray();
        _positions = list.Select(a => a.Position).ToArray();
    }

    public double Minimum => _values[0];
    public double Maximum => _values[^1];

    public double ToNormalized(double value)
    {
        if (double.IsNaN(value) || value <= _values[0]) return MappingMath.Clamp01(_positions[0]);
        if (value >= _values[^1]) return MappingMath.Clamp01(_positions[^1]);
        int i = Array.BinarySearch(_values, value);
        if (i >= 0) return _positions[i];
        i = ~i; // first index with a greater value
        double t = (value - _values[i - 1]) / (_values[i] - _values[i - 1]);
        return _positions[i - 1] + t * (_positions[i] - _positions[i - 1]);
    }

    public double FromNormalized(double normalized)
    {
        double u = MappingMath.Clamp01(normalized);
        if (u <= _positions[0]) return _values[0];
        if (u >= _positions[^1]) return _values[^1];
        int i = Array.BinarySearch(_positions, u);
        if (i >= 0) return _values[i];
        i = ~i;
        double t = (u - _positions[i - 1]) / (_positions[i] - _positions[i - 1]);
        return _values[i - 1] + t * (_values[i] - _values[i - 1]);
    }
}

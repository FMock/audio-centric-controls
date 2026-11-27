namespace AudioCentric.Controls.Dynamics;

/// <summary>How the displayed needle follows its target.</summary>
public enum NeedleDynamicsMode
{
    /// <summary>The needle tracks the value exactly. Use for calibrated measurements (no double smoothing).</summary>
    None,
    /// <summary>A damped second-order spring that gives the needle visual mass and slight overshoot.</summary>
    Mechanical,
}

/// <summary>
/// Display-only needle motion in normalized deflection space (0..1). This is not a calibrated VU detector:
/// calibrated dynamics belong to the measurement provider. It only adds the visual inertia of a physical
/// movement. Driven by elapsed time; stable for any frame interval.
/// </summary>
public sealed class NeedleBallistics
{
    private const double MaxStep = 1.0 / 480.0;

    public NeedleDynamicsMode Mode { get; set; } = NeedleDynamicsMode.Mechanical;

    /// <summary>Natural frequency in Hz. Default 7 Hz gives a lively but visibly mechanical movement.</summary>
    public double NaturalFrequencyHz { get; set; } = 7.0;

    /// <summary>Damping ratio; below 1 overshoots slightly. Default 0.75.</summary>
    public double DampingRatio { get; set; } = 0.75;

    public double Position { get; private set; }
    public double Velocity { get; private set; }
    public double Target { get; set; }

    /// <summary>True while the needle is still moving noticeably.</summary>
    public bool IsSettling => Math.Abs(Target - Position) > 1e-4 || Math.Abs(Velocity) > 1e-3;

    public void Reset(double position)
    {
        Position = Target = position;
        Velocity = 0;
    }

    /// <summary>Advance by <paramref name="elapsedSeconds"/> and return the new position.</summary>
    public double Update(double elapsedSeconds)
    {
        if (Mode == NeedleDynamicsMode.None || double.IsNaN(Position))
        {
            Position = Target;
            Velocity = 0;
            return Position;
        }

        double remaining = Math.Clamp(elapsedSeconds, 0, 0.25);
        double w = 2 * Math.PI * NaturalFrequencyHz;
        double z = DampingRatio;
        while (remaining > 0)
        {
            double dt = Math.Min(MaxStep, remaining);
            remaining -= dt;
            // Semi-implicit Euler for x'' = w^2 (target - x) - 2 z w x'
            double accel = w * w * (Target - Position) - 2 * z * w * Velocity;
            Velocity += accel * dt;
            Position += Velocity * dt;
        }

        // A real needle stops against its end pins.
        const double pinLow = -0.02, pinHigh = 1.03;
        if (Position < pinLow) { Position = pinLow; Velocity = Math.Max(0, Velocity) * -0.2; }
        if (Position > pinHigh) { Position = pinHigh; Velocity = Math.Min(0, Velocity) * -0.2; }
        return Position;
    }
}

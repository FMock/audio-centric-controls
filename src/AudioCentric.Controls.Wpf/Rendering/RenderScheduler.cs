using System.Windows.Media;

namespace AudioCentric.Controls.Wpf.Rendering;

/// <summary>Something that advances display animation by elapsed time on the UI thread.</summary>
public interface IRenderTickable
{
    /// <summary>Advance animation state by <paramref name="elapsedSeconds"/>.</summary>
    void Tick(double elapsedSeconds);
}

/// <summary>
/// One shared <see cref="CompositionTarget.Rendering"/> subscription for every animated control.
/// Controls register on Loaded and unregister on Unloaded. The event is detached when nothing is registered,
/// so an idle application does not keep rendering frames.
/// </summary>
public static class RenderScheduler
{
    private static readonly List<IRenderTickable> Items = new();
    private static readonly List<IRenderTickable> Snapshot = new();
    private static TimeSpan _lastRenderTime = TimeSpan.MinValue;
    private static bool _attached;

    public static int Count => Items.Count;

    public static void Register(IRenderTickable item)
    {
        if (Items.Contains(item)) return;
        Items.Add(item);
        if (!_attached)
        {
            CompositionTarget.Rendering += OnRendering;
            _attached = true;
            _lastRenderTime = TimeSpan.MinValue;
        }
    }

    public static void Unregister(IRenderTickable item)
    {
        Items.Remove(item);
        if (Items.Count == 0 && _attached)
        {
            CompositionTarget.Rendering -= OnRendering;
            _attached = false;
        }
    }

    private static void OnRendering(object? sender, EventArgs e)
    {
        var now = e is RenderingEventArgs r ? r.RenderingTime : TimeSpan.FromTicks(Environment.TickCount64 * TimeSpan.TicksPerMillisecond);
        if (now == _lastRenderTime) return; // Rendering can fire more than once per frame.
        double dt = _lastRenderTime == TimeSpan.MinValue ? 1.0 / 60.0 : (now - _lastRenderTime).TotalSeconds;
        _lastRenderTime = now;
        dt = Math.Clamp(dt, 0, 0.25);

        Snapshot.Clear();
        Snapshot.AddRange(Items);
        foreach (var item in Snapshot) item.Tick(dt);
    }
}

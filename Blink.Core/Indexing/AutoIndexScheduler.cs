namespace Blink.Core.Indexing;

/// <summary>
/// Fires a callback on a fixed cadence — the auto-index period selected in Settings. The "수동"
/// (manual) option leaves the scheduler disabled. The callback runs on a <see cref="System.Threading.Timer"/>
/// thread-pool thread; callers marshal to their UI thread as needed (e.g. WPF Dispatcher).
///
/// Reconfiguring (via <see cref="Configure"/>/<see cref="SetPeriod"/>) restarts the timer with the new
/// period, so changing the setting takes effect immediately without an app restart.
/// </summary>
public sealed class AutoIndexScheduler : IDisposable
{
    private readonly Action _onTick;
    private readonly object _gate = new();
    private System.Threading.Timer? _timer;
    private bool _disposed;

    /// <summary>The active polling period, or <c>null</c> when disabled (manual).</summary>
    public TimeSpan? Period { get; private set; }

    /// <summary>True while a cadence is active (i.e. not manual and not disposed).</summary>
    public bool IsEnabled => Period is not null;

    public AutoIndexScheduler(Action onTick) =>
        _onTick = onTick ?? throw new ArgumentNullException(nameof(onTick));

    /// <summary>Apply a cadence key ("15m"/"1h"/"6h"/"off"); starts, restarts, or stops the timer.</summary>
    public void Configure(string? intervalKey) => SetPeriod(AutoIndexInterval.ToPeriod(intervalKey));

    /// <summary>Set the polling period directly; <c>null</c> disables the timer. The first tick is one period out.</summary>
    public void SetPeriod(TimeSpan? period)
    {
        lock (_gate)
        {
            if (_disposed) return;
            Period = period;
            _timer?.Dispose();
            _timer = null;
            if (period is { } p)
                _timer = new System.Threading.Timer(_ => _onTick(), null, p, p);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            Period = null;
            _timer?.Dispose();
            _timer = null;
        }
    }
}

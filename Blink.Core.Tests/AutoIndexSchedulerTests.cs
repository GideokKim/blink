using Blink.Core.Indexing;

namespace Blink.Core.Tests;

public sealed class AutoIndexIntervalTests
{
    [Fact]
    public void Options_AreTheFourHandoffChoices_InOrder()
    {
        Assert.Equal(new[] { "15m", "1h", "6h", "off" }, AutoIndexInterval.Options.Select(o => o.Key));
    }

    [Theory]
    [InlineData("15m", 15)]
    [InlineData("1h", 60)]
    [InlineData("6h", 360)]
    public void ToPeriod_MapsKnownKeys(string key, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), AutoIndexInterval.ToPeriod(key));
    }

    [Fact]
    public void ToPeriod_Manual_IsNull()
    {
        Assert.Null(AutoIndexInterval.ToPeriod("off"));
        Assert.True(AutoIndexInterval.IsManual("off"));
    }

    [Theory]
    [InlineData("1H")]
    [InlineData("15M")]
    public void Resolve_IsCaseInsensitive(string key)
    {
        Assert.Equal(key.ToLowerInvariant(), AutoIndexInterval.Resolve(key).Key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bogus")]
    public void Resolve_UnknownOrBlank_FallsBackToDefaultHourly(string? key)
    {
        Assert.Equal(AutoIndexInterval.Default, AutoIndexInterval.Resolve(key).Key);
        Assert.Equal(TimeSpan.FromHours(1), AutoIndexInterval.ToPeriod(key));
        Assert.False(AutoIndexInterval.IsManual(key));
    }
}

public sealed class AutoIndexSchedulerTests
{
    [Fact]
    public void Configure_Manual_LeavesSchedulerDisabled()
    {
        using var sched = new AutoIndexScheduler(() => { });
        sched.Configure("off");
        Assert.False(sched.IsEnabled);
        Assert.Null(sched.Period);
    }

    [Fact]
    public void Configure_Interval_EnablesWithThatPeriod()
    {
        using var sched = new AutoIndexScheduler(() => { });
        sched.Configure("6h");
        Assert.True(sched.IsEnabled);
        Assert.Equal(TimeSpan.FromHours(6), sched.Period);
    }

    [Fact]
    public void SetPeriod_FiresCallbackOnCadence()
    {
        using var fired = new ManualResetEventSlim(false);
        using var sched = new AutoIndexScheduler(() => fired.Set());
        sched.SetPeriod(TimeSpan.FromMilliseconds(40));
        Assert.True(fired.Wait(TimeSpan.FromSeconds(5)), "callback should fire within the cadence");
    }

    [Fact]
    public void SetPeriod_Null_StopsFiring()
    {
        int count = 0;
        using var sched = new AutoIndexScheduler(() => Interlocked.Increment(ref count));
        sched.SetPeriod(TimeSpan.FromMilliseconds(30));
        Thread.Sleep(150);
        sched.SetPeriod(null);
        int afterStop = Volatile.Read(ref count);
        Thread.Sleep(150);
        Assert.Equal(afterStop, Volatile.Read(ref count)); // no further ticks after disabling
        Assert.False(sched.IsEnabled);
    }

    [Fact]
    public void Dispose_StopsFiringAndDisables()
    {
        int count = 0;
        var sched = new AutoIndexScheduler(() => Interlocked.Increment(ref count));
        sched.SetPeriod(TimeSpan.FromMilliseconds(30));
        Thread.Sleep(120);
        sched.Dispose();
        int afterDispose = Volatile.Read(ref count);
        Thread.Sleep(120);
        Assert.Equal(afterDispose, Volatile.Read(ref count));
        Assert.False(sched.IsEnabled);
    }
}

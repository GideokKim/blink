using Blink.Core.Launch;

namespace Blink.Core.Tests;

/// <summary>
/// Unit tests for <see cref="HotkeyInterceptor"/> — the pure decision state machine for the
/// left-Alt+Space global hotkey. Verifies that Alt+Space fires once and is swallowed (down and up),
/// auto-repeat is swallowed without refiring, marker-tagged injected events are forwarded without
/// polluting state, right Alt is not tracked, and state resets cleanly between cycles.
/// </summary>
public sealed class HotkeyInterceptorTests
{
    private const uint LAlt = 0xA4;
    private const uint RAlt = 0xA5;
    private const uint Space = 0x20;

    private readonly HotkeyInterceptor _sut = new();

    private HotkeyAction Down(uint vk) => _sut.Process(vk, isDown: true, isUp: false, extraInfo: 0);
    private HotkeyAction Up(uint vk) => _sut.Process(vk, isDown: false, isUp: true, extraInfo: 0);
    private HotkeyAction InjectedDown(uint vk)
        => _sut.Process(vk, isDown: true, isUp: false, extraInfo: HotkeyInterceptor.InjectionMarker);

    [Fact]
    public void Space_alone_forwards()
    {
        Assert.Equal(HotkeyAction.Forward, Down(Space));
        Assert.Equal(HotkeyAction.Forward, Up(Space));
    }

    [Fact]
    public void AltSpace_fires_and_swallows()
    {
        Assert.Equal(HotkeyAction.Forward, Down(LAlt));
        Assert.Equal(HotkeyAction.SwallowAndFire, Down(Space));
        Assert.Equal(HotkeyAction.Swallow, Up(Space));
        Assert.Equal(HotkeyAction.Forward, Up(LAlt));
    }

    [Fact]
    public void AutoRepeat_swallows_without_refire()
    {
        Assert.Equal(HotkeyAction.Forward, Down(LAlt));
        Assert.Equal(HotkeyAction.SwallowAndFire, Down(Space));
        Assert.Equal(HotkeyAction.Swallow, Down(Space));
        Assert.Equal(HotkeyAction.Swallow, Down(Space));
    }

    [Fact]
    public void AltUp_before_SpaceUp_still_swallows_up()
    {
        Assert.Equal(HotkeyAction.Forward, Down(LAlt));
        Assert.Equal(HotkeyAction.SwallowAndFire, Down(Space));
        Assert.Equal(HotkeyAction.Forward, Up(LAlt));
        Assert.Equal(HotkeyAction.Swallow, Up(Space));
    }

    [Fact]
    public void RightAlt_not_tracked()
    {
        Assert.Equal(HotkeyAction.Forward, Down(RAlt));
        Assert.Equal(HotkeyAction.Forward, Down(Space));
    }

    [Fact]
    public void Marker_events_ignored_for_state()
    {
        Assert.Equal(HotkeyAction.Forward, Down(LAlt));
        Assert.Equal(HotkeyAction.Forward, InjectedDown(Space));
        // No state pollution: a subsequent real Space down still fires.
        Assert.Equal(HotkeyAction.SwallowAndFire, Down(Space));
    }

    [Fact]
    public void SpaceUp_without_swallowed_down_forwards()
    {
        Assert.Equal(HotkeyAction.Forward, Up(Space));
    }

    [Fact]
    public void Alt_tapped_then_space_forwards()
    {
        Assert.Equal(HotkeyAction.Forward, Down(LAlt));
        Assert.Equal(HotkeyAction.Forward, Up(LAlt));
        Assert.Equal(HotkeyAction.Forward, Down(Space));
    }

    [Fact]
    public void Two_cycles_work()
    {
        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(HotkeyAction.Forward, Down(LAlt));
            Assert.Equal(HotkeyAction.SwallowAndFire, Down(Space));
            Assert.Equal(HotkeyAction.Swallow, Up(Space));
            Assert.Equal(HotkeyAction.Forward, Up(LAlt));
        }
    }

    [Fact]
    public void Refires_after_full_release()
    {
        // Complete one full cycle.
        Down(LAlt);
        Down(Space);
        Up(Space);
        Up(LAlt);

        // A fresh press fires again.
        Assert.Equal(HotkeyAction.Forward, Down(LAlt));
        Assert.Equal(HotkeyAction.SwallowAndFire, Down(Space));
    }
}

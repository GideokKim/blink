using Blink.Core.Update;

namespace Blink.Core.Tests;

public sealed class UpdatePolicyTests
{
    private static ReleaseInfo Release(string tag, bool withInstaller = true)
    {
        Assert.True(SemVer.TryParse(tag, out var v));
        return new ReleaseInfo
        {
            Version = v!,
            TagName = tag,
            InstallerUrl = withInstaller ? $"https://example.com/Blink-Setup-{v}.exe" : null,
            InstallerName = withInstaller ? $"Blink-Setup-{v}.exe" : null,
        };
    }

    // ── IsNewer (수동 "지금 확인": skip_version 무시) ──────────────────────────
    [Fact]
    public void IsNewer_NewerWithInstaller_True() =>
        Assert.True(UpdatePolicy.IsNewer(Release("v1.1.0"), "1.0.0"));

    [Theory]
    [InlineData("1.1.0")] // 같음
    [InlineData("1.2.0")] // 현재가 더 높음
    public void IsNewer_SameOrOlder_False(string current) =>
        Assert.False(UpdatePolicy.IsNewer(Release("v1.1.0"), current));

    [Fact]
    public void IsNewer_PreReleaseUser_SeesStableUpgrade() =>
        // 스펙: 0.1.0-rc1 설치자는 stable 0.1.0 알림을 받아야 한다.
        Assert.True(UpdatePolicy.IsNewer(Release("v0.1.0"), "0.1.0-rc1"));

    [Fact]
    public void IsNewer_NullRelease_False() =>
        Assert.False(UpdatePolicy.IsNewer(null, "1.0.0"));

    [Fact]
    public void IsNewer_NoInstallerAsset_False() =>
        // 인스톨러 자산이 없으면 체크 실패로 간주(침묵).
        Assert.False(UpdatePolicy.IsNewer(Release("v9.9.9", withInstaller: false), "1.0.0"));

    [Fact]
    public void IsNewer_UnparsableCurrentVersion_False() =>
        Assert.False(UpdatePolicy.IsNewer(Release("v1.1.0"), "garbage"));

    // ── ShouldOffer (자동 체크: skip_version 존중) ─────────────────────────────
    [Fact]
    public void ShouldOffer_NewVersionNotSkipped_True() =>
        Assert.True(UpdatePolicy.ShouldOffer(Release("v1.1.0"), "1.0.0", skipVersion: ""));

    [Fact]
    public void ShouldOffer_SkippedVersion_False() =>
        Assert.False(UpdatePolicy.ShouldOffer(Release("v1.1.0"), "1.0.0", skipVersion: "1.1.0"));

    [Fact]
    public void ShouldOffer_NewerThanSkipped_True() =>
        // 건너뛴 버전보다 더 새로운 버전이 나오면 다시 알린다.
        Assert.True(UpdatePolicy.ShouldOffer(Release("v1.2.0"), "1.0.0", skipVersion: "1.1.0"));

    [Fact]
    public void ShouldOffer_NotNewer_False() =>
        Assert.False(UpdatePolicy.ShouldOffer(Release("v1.0.0"), "1.0.0", skipVersion: ""));

    // ── WhatsNewGate ──────────────────────────────────────────────────────────
    [Fact]
    public void WhatsNew_NoRecord_InitializeOnly() =>
        // 신규 설치/기능 도입 직후: 표시하지 않고 현재 버전으로 초기화만.
        Assert.Equal(WhatsNewAction.InitializeOnly, WhatsNewGate.Decide("", "1.0.0"));

    [Fact]
    public void WhatsNew_GarbageRecord_InitializeOnly() =>
        Assert.Equal(WhatsNewAction.InitializeOnly, WhatsNewGate.Decide("garbage", "1.0.0"));

    [Fact]
    public void WhatsNew_CurrentAboveLastSeen_Show() =>
        Assert.Equal(WhatsNewAction.Show, WhatsNewGate.Decide("1.0.0", "1.1.0"));

    [Fact]
    public void WhatsNew_SameVersion_None() =>
        Assert.Equal(WhatsNewAction.None, WhatsNewGate.Decide("1.1.0", "1.1.0"));

    [Fact]
    public void WhatsNew_LastSeenHigher_None() =>
        // 다운그레이드/롤백: 창을 띄우지 않는다.
        Assert.Equal(WhatsNewAction.None, WhatsNewGate.Decide("1.2.0", "1.1.0"));

    [Fact]
    public void WhatsNew_UnparsableCurrent_None() =>
        Assert.Equal(WhatsNewAction.None, WhatsNewGate.Decide("1.0.0", "garbage"));
}

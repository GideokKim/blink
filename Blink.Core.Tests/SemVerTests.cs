using Blink.Core.Update;

namespace Blink.Core.Tests;

public sealed class SemVerTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("V0.1.0", 0, 1, 0)]
    [InlineData("1.2.3+abc123", 1, 2, 3)]          // 빌드 메타데이터는 무시하고 파싱
    [InlineData(" v1.2.3 ", 1, 2, 3)]              // 앞뒤 공백 허용
    public void TryParse_Stable(string input, int maj, int min, int pat)
    {
        Assert.True(SemVer.TryParse(input, out var v));
        Assert.Equal(maj, v!.Major);
        Assert.Equal(min, v.Minor);
        Assert.Equal(pat, v.Patch);
        Assert.Empty(v.PreRelease);
    }

    [Fact]
    public void TryParse_PreRelease_KeepsIdentifiers()
    {
        Assert.True(SemVer.TryParse("1.2.3-rc.1", out var v));
        Assert.Equal(new[] { "rc", "1" }, v!.PreRelease);
    }

    [Fact]
    public void TryParse_PreReleaseWithBuildMetadata()
    {
        Assert.True(SemVer.TryParse("0.1.0-rc1+deadbeef", out var v));
        Assert.Equal(new[] { "rc1" }, v!.PreRelease);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("abc")]
    [InlineData("1.2.x")]
    [InlineData("1.2.3-")]      // 빈 pre-release
    [InlineData("1.2.3-rc..1")] // 빈 식별자
    [InlineData("1.-2.3")]
    public void TryParse_Invalid_ReturnsFalse(string? input)
    {
        Assert.False(SemVer.TryParse(input, out var v));
        Assert.Null(v);
    }

    [Theory]
    // 기본 우열
    [InlineData("0.1.0", "0.1.1", -1)]
    [InlineData("1.9.9", "2.0.0", -1)]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.0.0", "1.1.0", -1)]
    // 빌드 메타데이터는 비교에서 무시
    [InlineData("1.2.3+aaa", "1.2.3+bbb", 0)]
    // pre-release < stable (스펙 §10: 0.1.0-rc1 < 0.1.0)
    [InlineData("0.1.0-rc1", "0.1.0", -1)]
    // pre-release끼리: 문자 ordinal
    [InlineData("1.0.0-alpha", "1.0.0-beta", -1)]
    // 숫자 식별자는 수치 비교 (rc.2 < rc.10)
    [InlineData("1.0.0-rc.2", "1.0.0-rc.10", -1)]
    // 숫자 식별자 < 문자 식별자
    [InlineData("1.0.0-1", "1.0.0-alpha", -1)]
    // 식별자 수가 적은 쪽이 낮음 (rc < rc.1)
    [InlineData("1.0.0-rc", "1.0.0-rc.1", -1)]
    public void CompareTo_FollowsSemVerPrecedence(string a, string b, int expectedSign)
    {
        SemVer.TryParse(a, out var va);
        SemVer.TryParse(b, out var vb);
        Assert.Equal(expectedSign, Math.Sign(va!.CompareTo(vb)));
        Assert.Equal(-expectedSign, Math.Sign(vb!.CompareTo(va)));
    }

    [Fact]
    public void CompareTo_Null_IsGreater()
    {
        SemVer.TryParse("1.0.0", out var v);
        Assert.Equal(1, Math.Sign(v!.CompareTo(null)));
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("v1.2.3-rc.1+meta", "1.2.3-rc.1")] // ToString은 메타데이터 제외
    public void ToString_RoundTrips(string input, string expected)
    {
        SemVer.TryParse(input, out var v);
        Assert.Equal(expected, v!.ToString());
    }
}

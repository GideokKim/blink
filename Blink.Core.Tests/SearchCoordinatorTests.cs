using Blink.Core.Search;

namespace Blink.Core.Tests;

/// <summary>
/// <see cref="SearchCoordinator"/> 계약 검증: Begin이 이전 토큰을 취소하고 이전 세대를
/// 무효화하는지, CancelPending이 새 검색 없이 전부 무효화하는지, 세대가 단조 증가하는지,
/// 그리고 늦게 끝난 작업의 결과가 세대 검사로 버려지는지(stale-discard) 확인한다.
/// </summary>
public sealed class SearchCoordinatorTests : IDisposable
{
    private readonly SearchCoordinator _coordinator = new();

    public void Dispose() => _coordinator.Dispose();

    [Fact]
    public void Begin_Twice_FirstTokenCanceled_FirstGenerationStale()
    {
        var (gen1, token1) = _coordinator.Begin();

        _coordinator.Begin();

        Assert.True(token1.IsCancellationRequested);
        Assert.False(_coordinator.IsCurrent(gen1));
    }

    [Fact]
    public void Begin_NewTokenNotCanceled()
    {
        _coordinator.Begin();

        var (gen2, token2) = _coordinator.Begin();

        Assert.False(token2.IsCancellationRequested);
        Assert.True(_coordinator.IsCurrent(gen2));
    }

    [Fact]
    public void CancelPending_CancelsToken_AndInvalidatesGeneration()
    {
        var (gen, token) = _coordinator.Begin();

        _coordinator.CancelPending();

        Assert.True(token.IsCancellationRequested);
        Assert.False(_coordinator.IsCurrent(gen));
    }

    [Fact]
    public void Generation_Monotonic()
    {
        var previous = _coordinator.Begin().Generation;
        for (int i = 0; i < 5; i++)
        {
            var current = _coordinator.Begin().Generation;
            Assert.True(current > previous);
            previous = current;
        }
    }

    [Fact]
    public async Task StaleWork_EitherCanceled_OrDiscardedByGenerationCheck()
    {
        var (gen1, token1) = _coordinator.Begin();
        using var gate = new ManualResetEventSlim(false);

        // 게이트에 막혀 있는 가짜 검색 작업 — 풀려난 뒤 토큰을 확인하고 결과를 낸다.
        var work = Task.Run(() =>
        {
            gate.Wait(TimeSpan.FromSeconds(10));
            token1.ThrowIfCancellationRequested();
            return 42;
        }, token1);

        _coordinator.Begin(); // 새 검색이 끼어든다.
        gate.Set();

        try
        {
            await work;
            // 작업이 살아서 완료됐다면, 세대 검사가 stale 결과 적용을 막아야 한다.
            Assert.False(_coordinator.IsCurrent(gen1));
        }
        catch (OperationCanceledException)
        {
            // 토큰 취소로 빠르게 중단된 경우 — 역시 올바른 결말.
        }
    }
}

namespace Blink.Core.Search;

/// <summary>
/// 검색의 세대(generation)와 취소 토큰 수명을 관리한다. 토큰은 비싼 작업(쿼리 실행 등)을
/// 빠르게 중단시키는 수단이고, 세대는 stale 결과 적용을 막는 최종 방어선이다.
/// 백그라운드에서 "내가 최신이다"를 확인한 뒤에도 새 <see cref="Begin"/>이 끼어들 수 있으므로,
/// <see cref="IsCurrent"/> 검사는 결과를 적용하기 직전 UI 스레드에서 — 사이에 await 없이 —
/// 수행해야 한다. 내부에 Task.Run/Dispatcher가 없어 단위 테스트가 쉽다.
/// </summary>
public sealed class SearchCoordinator : IDisposable
{
    private long _generation;
    private CancellationTokenSource _cts = new();

    /// <summary>진행 중인 검색을 대체: 이전 토큰 취소 후 새 (세대, 토큰) 발급.
    /// Begin/CancelPending은 단일 스레드(UI)에서만 호출하는 계약.</summary>
    public (long Generation, CancellationToken Token) Begin()
    {
        _cts.Cancel();
        _cts.Dispose();
        _cts = new CancellationTokenSource();
        return (Interlocked.Increment(ref _generation), _cts.Token);
    }

    /// <summary>해당 세대가 아직 최신인가 — 결과 적용 직전에 호출.</summary>
    public bool IsCurrent(long generation) => Interlocked.Read(ref _generation) == generation;

    /// <summary>새 검색 없이 전부 무효화 (창 숨김 등).</summary>
    public void CancelPending() { _cts.Cancel(); Interlocked.Increment(ref _generation); }

    public void Dispose() => _cts.Dispose();
}

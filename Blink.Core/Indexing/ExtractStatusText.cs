using System.Text;
using Blink.Core.Model;

namespace Blink.Core.Indexing;

/// <summary>User-facing (Korean) wording for <see cref="ExtractStatus"/>, kept in Core so it is testable.</summary>
public static class ExtractStatusText
{
    public static string Label(ExtractStatus s) => s switch
    {
        ExtractStatus.ReadDenied => "읽기 권한 없음",
        ExtractStatus.CloudUnavailable => "클라우드 파일 미수신",
        ExtractStatus.Encrypted => "암호화된 것으로 보임",
        ExtractStatus.ParseError => "파싱 오류",
        ExtractStatus.TooLarge => "크기 상한 초과",
        ExtractStatus.EmptyText => "텍스트 없음",
        ExtractStatus.Locked => "다른 프로그램이 사용 중",
        ExtractStatus.NotApplicable => "파일명만 색인",
        _ => "정상",
    };

    public static string Hint(ExtractStatus s) => s switch
    {
        ExtractStatus.ReadDenied => "파일 권한 또는 보안 솔루션이 Blink의 읽기를 막고 있습니다.",
        ExtractStatus.CloudUnavailable => "가상 드라이브에서 파일 내용을 받지 못했습니다. 오프라인 사용 설정 또는 연결 상태를 확인하세요.",
        ExtractStatus.Encrypted => "문서중앙화/DRM으로 암호화된 것으로 보입니다. 솔루션 관리자에게 Blink.exe 허용을 요청하세요.",
        ExtractStatus.ParseError => "파일이 손상되었거나 지원하지 않는 형식 변형입니다.",
        ExtractStatus.TooLarge => "본문 추출 상한을 넘어 파일명만 색인했습니다.",
        ExtractStatus.EmptyText => "스캔본 등 추출 가능한 텍스트가 없습니다.",
        ExtractStatus.Locked => "다른 프로그램이 파일을 열고 있어 읽지 못했습니다. 다음 인덱싱 때 다시 읽습니다.",
        _ => string.Empty,
    };

    /// <summary>Sum of counts for blocking failures only (what the badge shows).</summary>
    public static long FailureCount(IReadOnlyDictionary<ExtractStatus, long> counts)
        => counts.Where(kv => kv.Key.IsFailure()).Sum(kv => kv.Value);

    /// <summary>Tab-separated copy of a failure list (header + one row each) for pasting into an issue.</summary>
    public static string ToTsv(IEnumerable<ExtractFailure> failures)
    {
        var sb = new StringBuilder("상태\t경로\t상세");
        foreach (var f in failures)
            sb.Append('\n').Append(Clean(Label(f.Status))).Append('\t')
              .Append(Clean(f.DocId)).Append('\t').Append(Clean(f.Detail ?? string.Empty));
        return sb.ToString();
    }

    private static string Clean(string s) => s.Replace('\t', ' ').ReplaceLineEndings(" ");
}

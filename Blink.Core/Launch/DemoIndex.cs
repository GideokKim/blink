namespace Blink.Core.Launch;

/// <summary>
/// The prototype's mock index (<c>data.js</c> → <c>INDEX</c>), ported verbatim. Used for
/// design/preview parity so the launcher UI reproduces the handoff screenshots for the
/// query "blink". In production this is replaced by real apps / file-system / content-index
/// results fed into <see cref="LaunchSearch.Search"/>.
/// </summary>
public static class DemoIndex
{
    public static IReadOnlyList<LaunchItem> Items { get; } = new[]
    {
        // ---- Apps ----
        new LaunchItem("app-vscode", LaunchItemKind.App, "Visual Studio Code", "응용 프로그램", "VS", 250, Keywords: "code editor dev ide vscode 코드 에디터"),
        new LaunchItem("app-figma", LaunchItemKind.App, "Figma", "응용 프로그램", "Fi", 320, Keywords: "design ui figma 디자인"),
        new LaunchItem("app-terminal", LaunchItemKind.App, "Terminal", "응용 프로그램", "›_", 200, Keywords: "terminal shell console 터미널 명령"),
        new LaunchItem("app-slack", LaunchItemKind.App, "Slack", "응용 프로그램", "Sl", 290, Keywords: "chat slack message 메신저"),
        new LaunchItem("app-notion", LaunchItemKind.App, "Notion", "응용 프로그램", "No", 30, Keywords: "notes notion docs 노션 노트"),
        new LaunchItem("app-spotify", LaunchItemKind.App, "Spotify", "응용 프로그램", "Sp", 150, Keywords: "music spotify 음악"),
        new LaunchItem("app-chrome", LaunchItemKind.App, "Chrome", "응용 프로그램", "Ch", 220, Keywords: "browser web chrome 브라우저"),

        // ---- Folders ----
        new LaunchItem("fold-blink", LaunchItemKind.Folder, "blink", "~/dev/projects/blink", "▣", 250, Keywords: "project source 프로젝트 폴더 dev"),
        new LaunchItem("fold-design", LaunchItemKind.Folder, "Design Assets", "~/work/design-assets", "▣", 250, Keywords: "design assets 폴더 디자인"),

        // ---- Files ----
        new LaunchItem("file-readme", LaunchItemKind.File, "README.md", "~/dev/projects/blink", "md", 250,
            Ext: "md", Size: "4.2 KB", Mod: "2분 전",
            Keywords: "readme markdown blink documentation 문서",
            Content: "Blink is a blazing-fast launcher that indexes your files and searches inside document contents in real time."),
        new LaunchItem("file-arch", LaunchItemKind.File, "blink_architecture.md", "~/dev/projects/blink/docs", "md", 250,
            Ext: "md", Size: "11.8 KB", Mod: "어제",
            Keywords: "architecture design system core engine 아키텍처 설계 blink",
            Content: "The Blink.Core engine builds an inverted index over file contents. Tokenized full-text search returns ranked snippets with sub-millisecond latency."),
        new LaunchItem("file-roadmap", LaunchItemKind.File, "Roadmap 2026.pdf", "~/Documents/Planning", "pdf", 12,
            Ext: "pdf", Size: "2.1 MB", Mod: "3일 전",
            Keywords: "roadmap planning 2026 로드맵 기획 blink",
            Content: "Q3 milestone: ship Blink content indexing and the new translucent launcher UI across Windows and macOS."),
        new LaunchItem("file-finance", LaunchItemKind.File, "Q3_재무보고서.xlsx", "~/Documents/Finance", "xls", 150,
            Ext: "xlsx", Size: "842 KB", Mod: "1주 전",
            Keywords: "finance report 재무 보고서 q3 budget 예산",
            Content: "총 매출 23.8억, 전분기 대비 +14%. 마케팅 예산 항목 재배분 검토 필요."),
        new LaunchItem("file-notes", LaunchItemKind.File, "회의록_2026-06-04.md", "~/Notes/meetings", "md", 250,
            Ext: "md", Size: "3.6 KB", Mod: "4일 전",
            Keywords: "meeting notes 회의록 blink launcher",
            Content: "Blink 런처 디자인 리뷰: 반투명 글래스 + 키보드 우선 탐색으로 방향 확정. 다음 스프린트에 프로토타입."),
        new LaunchItem("file-spec", LaunchItemKind.File, "search-spec.pdf", "~/dev/projects/blink/docs", "pdf", 12,
            Ext: "pdf", Size: "980 KB", Mod: "2일 전",
            Keywords: "search specification fuzzy ranking 검색 명세 blink",
            Content: "Fuzzy matching ranks results by token proximity, recency and frequency. Content matches surface inline snippets."),
        new LaunchItem("file-budget", LaunchItemKind.File, "marketing_budget.xlsx", "~/Documents/Finance", "xls", 150,
            Ext: "xlsx", Size: "455 KB", Mod: "2주 전",
            Keywords: "marketing budget 마케팅 예산 spend",
            Content: "채널별 지출: 검색광고 38%, 콘텐츠 27%, 이벤트 18%, 기타 17%."),
        new LaunchItem("file-logo", LaunchItemKind.File, "blink_logo_final.svg", "~/work/design-assets/brand", "svg", 320,
            Ext: "svg", Size: "62 KB", Mod: "5일 전",
            Keywords: "logo brand vector svg 로고 blink 브랜드"),
        new LaunchItem("file-screenshot", LaunchItemKind.File, "launcher_mock_v3.png", "~/work/design-assets/blink", "png", 320,
            Ext: "png", Size: "1.4 MB", Mod: "오늘",
            Keywords: "mockup screenshot launcher 목업 스크린샷 blink"),

        // ---- Commands ----
        new LaunchItem("cmd-sleep", LaunchItemKind.Command, "절전 모드", "시스템 명령", "◐", 250, Keywords: "sleep 절전 system 잠자기"),
        new LaunchItem("cmd-empty-trash", LaunchItemKind.Command, "휴지통 비우기", "시스템 명령", "⌫", 12, Keywords: "trash empty 휴지통 비우기 삭제"),
        new LaunchItem("cmd-screenshot", LaunchItemKind.Command, "화면 캡처", "시스템 명령", "⤓", 250, Keywords: "screenshot capture 캡처 화면"),
    };
}

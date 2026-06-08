namespace Blink.Core.Launch;

/// <summary>
/// The kind of a launcher result. Mirrors the prototype's <c>type</c> union
/// (<c>app | file | folder | content | command | calc | web | setting</c>) and drives the
/// Korean type-tag shown in the UI (see <see cref="LaunchTypes.Label"/>).
/// </summary>
public enum LaunchItemKind
{
    App,
    File,
    Folder,
    Content,
    Command,
    Calc,
    Web,
    Setting,
}

/// <summary>Korean labels for each <see cref="LaunchItemKind"/> (the prototype's <c>TYPE_LABEL</c>).</summary>
public static class LaunchTypes
{
    public static string Label(LaunchItemKind kind) => kind switch
    {
        LaunchItemKind.App => "앱",
        LaunchItemKind.File => "파일",
        LaunchItemKind.Folder => "폴더",
        LaunchItemKind.Content => "문서 내용",
        LaunchItemKind.Command => "명령",
        LaunchItemKind.Calc => "계산",
        LaunchItemKind.Web => "웹 검색",
        LaunchItemKind.Setting => "설정",
        _ => "",
    };
}

/// <summary>
/// One searchable launcher entry. Matches the prototype item model in <c>data.js</c>:
/// <c>{ id, type, title, sub (path), glyph, hue, ext?, size?, mod?, content?, kw? }</c>.
///
/// <para><see cref="Content"/> is the indexed document body used for full-text matching and
/// snippet/preview generation. <see cref="Keywords"/> (<c>kw</c>) and <see cref="Sub"/> (the
/// path) contribute to the scorable haystack. <see cref="Hue"/> is the category hue (220–280
/// cool range) used to tint the icon tile.</para>
/// </summary>
public sealed record LaunchItem(
    string Id,
    LaunchItemKind Type,
    string Title,
    string Sub,
    string Glyph,
    int Hue,
    string? Ext = null,
    string? Size = null,
    string? Mod = null,
    string? Content = null,
    string? Keywords = null);

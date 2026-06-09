// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
using System.Text.RegularExpressions;
using System.Windows.Media;
using Blink.App.Mvvm;
using Blink.App.Theming;
using Blink.Core.Launch;

namespace Blink.App.ViewModels;

/// <summary>
/// One result row, shared by Direction A and B. Wraps a <see cref="LaunchItem"/> (or a
/// calculator result) and exposes everything the row/preview templates bind to: title,
/// path/snippet, type tag, tile glyph + tint, content-match flag, and (for B) the preview
/// metadata and highlighted content lines.
/// </summary>
public sealed class RowViewModel : ObservableObject
{
    private static readonly Regex SentenceSplit = new(@"(?<=[.!?。…])\s+", RegexOptions.Compiled);

    public LaunchItem Item { get; }
    public string Query { get; }

    public RowViewModel(LaunchResult result, string query)
    {
        Item = result.Item;
        Query = query;
        ContentHit = result.ContentHit;
        var snip = LaunchSearch.Snippet(Item, query);
        SnippetPre = snip?.Pre ?? "";
        SnippetHit = snip?.Hit ?? "";
        SnippetPost = snip?.Post ?? "";
    }

    /// <summary>Build a synthetic row for the inline calculator result (pinned at the top).</summary>
    public static RowViewModel ForCalc(CalcResult calc)
    {
        var item = new LaunchItem("calc", LaunchItemKind.Calc, calc.Value, $"{calc.Expr} =", "=", 250);
        return new RowViewModel(new LaunchResult(item, int.MaxValue, false), "");
    }

    public string Title => Item.Title;
    public string Sub => Item.Sub;
    public string Glyph => Item.Glyph;
    public LaunchItemKind Kind => Item.Type;
    public string TypeLabel => LaunchTypes.Label(Item.Type);
    public string Size => Item.Size ?? "";
    public bool HasSize => !string.IsNullOrEmpty(Item.Size);

    public bool ContentHit { get; }
    public bool ShowSnippet => ContentHit && SnippetHit.Length > 0;
    public bool ShowSubtitle => !ShowSnippet;

    /// <summary>Direction B left-row subtitle: the literal "문서 내용 일치" for content matches, else the path.</summary>
    public string BSubtitle => ContentHit ? "문서 내용 일치" : Sub;

    public string SnippetPre { get; }
    public string SnippetHit { get; }
    public string SnippetPost { get; }

    /// <summary>Per-category tile tint: oklch(0.8 0.09 hue). Mono glyphs (len &gt; 2) use the mono font.</summary>
    public Brush GlyphBrush => ThemeManager.TileGlyph(Item.Hue);
    public bool GlyphIsMono => Item.Glyph.Length > 2;

    // ── Direction B preview ────────────────────────────────────────────────
    /// <summary>Display location (the containing folder), shown as the row/preview path.</summary>
    public string Path => Item.Sub;

    /// <summary>
    /// The action target — the actual file/folder path to open or reveal. This is the
    /// document key (<see cref="LaunchItem.Id"/> = absolute path), NOT <see cref="Path"/>
    /// (which is only the containing folder for display).
    /// </summary>
    public string TargetPath => Item.Id;
    public string KindLabel => LaunchTypes.Label(Item.Type);
    public string SizeOrDash => string.IsNullOrEmpty(Item.Size) ? "—" : Item.Size!;
    public string ModOrDash => string.IsNullOrEmpty(Item.Mod) ? "—" : Item.Mod!;
    public string ExtOrDash => string.IsNullOrEmpty(Item.Ext) ? "—" : "." + Item.Ext;
    public bool HasContent => !string.IsNullOrEmpty(Item.Content);

    /// <summary>Document content split into sentence lines (each highlighted in the preview).</summary>
    public IReadOnlyList<string> ContentLines =>
        string.IsNullOrEmpty(Item.Content)
            ? Array.Empty<string>()
            : SentenceSplit.Split(Item.Content!).Where(s => s.Trim().Length > 0).ToArray();

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Blink.App.Theming;

namespace Blink.App.Controls;

/// <summary>
/// A <see cref="TextBlock"/> that highlights query tokens inside its text, mirroring the
/// prototype's <c>&lt;Highlight&gt;</c>. Matched tokens get the <c>Mark</c> background (accent
/// tint). Set <see cref="SourceText"/> and <see cref="Query"/>; tokens are whitespace-split,
/// case-insensitive. The mark brush is bound via resource reference so it follows theme changes.
/// </summary>
public sealed class HighlightTextBlock : TextBlock
{
    public static readonly DependencyProperty SourceTextProperty = DependencyProperty.Register(
        nameof(SourceText), typeof(string), typeof(HighlightTextBlock),
        new PropertyMetadata("", OnChanged));

    public static readonly DependencyProperty QueryProperty = DependencyProperty.Register(
        nameof(Query), typeof(string), typeof(HighlightTextBlock),
        new PropertyMetadata("", OnChanged));

    public string SourceText
    {
        get => (string)GetValue(SourceTextProperty);
        set => SetValue(SourceTextProperty, value);
    }

    public string Query
    {
        get => (string)GetValue(QueryProperty);
        set => SetValue(QueryProperty, value);
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((HighlightTextBlock)d).Rebuild();

    private void Rebuild()
    {
        Inlines.Clear();
        var text = SourceText ?? "";
        var q = (Query ?? "").Trim();

        if (text.Length == 0) return;
        if (q.Length == 0) { Inlines.Add(new Run(text)); return; }

        var toks = q.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(Regex.Escape)
            .Where(t => t.Length > 0)
            .ToArray();
        if (toks.Length == 0) { Inlines.Add(new Run(text)); return; }

        var re = new Regex("(" + string.Join("|", toks) + ")", RegexOptions.IgnoreCase);
        foreach (var part in re.Split(text))
        {
            if (part.Length == 0) continue;
            var run = new Run(part);
            if (re.IsMatch(part))
                run.SetResourceReference(TextElement.BackgroundProperty, ThemeManager.Mark);
            Inlines.Add(run);
        }
    }
}

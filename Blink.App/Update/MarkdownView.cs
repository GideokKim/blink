// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Blink.Core.Update;

namespace Blink.App.Update;

/// <summary>
/// Renders MarkdownLite blocks into a StackPanel of TextBlocks using Blink theme tokens
/// (DynamicResource via SetResourceReference so theme switches keep working).
/// </summary>
public static class MarkdownView
{
    /// <summary>Marks the start of the web-only donation footer appended by the release pipeline.</summary>
    private const string FooterSentinel = "<!--BLINK_NOTES_END-->";

    public static void Render(StackPanel target, string markdown)
    {
        target.Children.Clear();

        // Drop the web-only donation footer (QR table / HTML) — MarkdownLite would render
        // its table/img markup as raw text. In-app donation is shown via DonatePanel instead.
        int cut = markdown?.IndexOf(FooterSentinel, StringComparison.Ordinal) ?? -1;
        if (cut >= 0) markdown = markdown![..cut];

        var blocks = MarkdownLite.Parse(markdown);
        if (blocks.Count == 0)
        {
            var empty = new TextBlock { Text = "릴리스 노트가 없습니다.", FontSize = 12.5 };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "Blink.TxtFaint");
            target.Children.Add(empty);
            return;
        }

        foreach (var block in blocks)
        {
            var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5 };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "Blink.Txt");

            switch (block.Kind)
            {
                case MdBlockKind.Heading:
                    tb.FontSize = block.Level switch { 1 => 16, 2 => 14.5, _ => 13 };
                    tb.FontWeight = FontWeights.SemiBold;
                    tb.Margin = new Thickness(0, 12, 0, 5);
                    break;
                case MdBlockKind.Bullet:
                    tb.Margin = new Thickness(12, 2, 0, 2);
                    var dot = new Run("•  ");
                    dot.SetResourceReference(TextElement.ForegroundProperty, "Blink.Accent");
                    tb.Inlines.Add(dot);
                    break;
                default:
                    tb.Margin = new Thickness(0, 4, 0, 4);
                    break;
            }

            foreach (var inline in block.Inlines)
                tb.Inlines.Add(ToInline(inline));
            target.Children.Add(tb);
        }
    }

    private static Inline ToInline(MdInline run)
    {
        switch (run.Kind)
        {
            case MdInlineKind.Code:
                var code = new Run(run.Text);
                code.SetResourceReference(TextElement.FontFamilyProperty, "Blink.Mono");
                code.SetResourceReference(TextElement.ForegroundProperty, "Blink.Accent");
                return code;

            case MdInlineKind.Bold:
                return new Run(run.Text) { FontWeight = FontWeights.SemiBold };

            case MdInlineKind.Link:
                var link = new Hyperlink(new Run(run.Text));
                link.SetResourceReference(TextElement.ForegroundProperty, "Blink.Accent");
                if (Uri.TryCreate(run.Url, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
                {
                    link.NavigateUri = uri;
                    link.RequestNavigate += (_, e) =>
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
                            {
                                UseShellExecute = true,
                            });
                        }
                        catch { /* no browser — non-fatal */ }
                    };
                }
                return link;

            default:
                return new Run(run.Text);
        }
    }
}

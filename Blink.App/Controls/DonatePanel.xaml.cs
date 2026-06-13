// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
using System.Diagnostics;
using System.Windows.Controls;

namespace Blink.App.Controls;

/// <summary>후원 안내 패널: 감사 문구 + 카카오페이/토스 QR. 설정 창과 DonateWindow에서 공유.</summary>
public partial class DonatePanel : UserControl
{
    /// <summary>카카오페이 송금 링크 (QR과 동일). README·FUNDING과 동일하게 유지한다.</summary>
    public const string KakaoPayUrl = "https://qr.kakaopay.com/281006011000003981911022";

    public DonatePanel() => InitializeComponent();

    private void KakaoLink_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        // Matches the app's link-open convention (ShellExecute) used elsewhere.
        try { Process.Start(new ProcessStartInfo(KakaoPayUrl) { UseShellExecute = true }); }
        catch (Exception ex) { Trace.WriteLine($"[Blink] open kakaopay failed: {ex}"); }
    }
}

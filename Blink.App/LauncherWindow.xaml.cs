// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Blink.App.Interop;
using Blink.App.Theming;
using Blink.App.ViewModels;
using Blink.App.Views;
using Blink.Core.Launch;

namespace Blink.App;

public enum LauncherDirection { Classic, Split }

public partial class LauncherWindow : Window
{
    private readonly LauncherViewModel _vm;
    private readonly IndexingStatusViewModel _status;
    private readonly ClassicView _classic;
    private readonly SplitView _split;
    private readonly DispatcherTimer _toastTimer;
    private bool _suppressHide;
    private bool _dotPulsing;
    private LauncherDirection _direction = LauncherDirection.Classic;

    private const double WidthClassic = 640;
    private const double WidthSplit = 880;

    internal LauncherWindow(IReadOnlyList<LaunchItem> index, IndexingStatusViewModel status)
    {
        InitializeComponent();

        _vm = new LauncherViewModel(index);
        _status = status;
        DataContext = _vm;
        _vm.PropertyChanged += OnVmChanged;
        _status.PropertyChanged += (_, _) => UpdateChrome(); // indexing status → footer

        _classic = new ClassicView { DataContext = _vm };
        _split = new SplitView { DataContext = _vm };
        _classic.ActionRequested += HandleAction;
        _split.ActionRequested += HandleAction;

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); HideLauncher(); };

        Deactivated += (_, _) => { if (!_suppressHide) HideLauncher(); };

        SetDirection(_direction);
        UpdateChrome();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        // Acrylic blur-behind + rounded corners (best effort; the translucent glass panel is the fallback).
        AcrylicHelper.TryApply(hwnd, (int)ActualWidth, (int)ActualHeight, 20);
    }

    // ── Direction & theme (settings) ──────────────────────────────────────────
    public void SetDirection(LauncherDirection direction)
    {
        _direction = direction;
        Body.Content = direction == LauncherDirection.Classic ? _classic : (UIElement)_split;
        Width = direction == LauncherDirection.Classic ? WidthClassic : WidthSplit;
        UpdateChrome();
    }

    public void ToggleTheme() => ThemeManager.Toggle();

    // ── Summon / hide ───────────────────────────────────────────────────────────
    public void Summon()
    {
        SearchBox.Text = "";
        UpdateChrome();
        PositionTopThird();
        Show();
        Activate();
        var hwnd = new WindowInteropHelper(this).Handle;
        ForegroundHelper.BringToForeground(hwnd);
        SearchBox.Focus();
        PlayEntrance();
    }

    private void PositionTopThird()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + 120; // Spotlight-style upper anchor (prototype top: 120px)
    }

    public void HideLauncher()
    {
        _toastTimer.Stop();
        Hide();
    }

    private void PlayEntrance()
    {
        // translateY(7) scale(0.992) → none over 0.34s, cubic-bezier(0.16,1,0.3,1)
        var ease = new CubicBezierEase(0.16, 1, 0.3, 1);
        var dur = TimeSpan.FromSeconds(0.34);
        PopTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(7, 0, dur) { EasingFunction = ease });
        var scale = new DoubleAnimation(0.992, 1, dur) { EasingFunction = ease };
        PopScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scale);
        PopScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scale);
    }

    // ── Search input ──────────────────────────────────────────────────────────
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _vm.Query = SearchBox.Text;
        UpdateChrome();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LauncherViewModel.ResultCount) or nameof(LauncherViewModel.SelectedIndex))
            UpdateChrome();
    }

    /// <summary>Placeholder, esc-chip/count slot, and footer caption per direction + query.</summary>
    private void UpdateChrome()
    {
        bool hasQuery = SearchBox.Text.Length > 0;
        Placeholder.Visibility = hasQuery ? Visibility.Collapsed : Visibility.Visible;

        bool classic = _direction == LauncherDirection.Classic;
        EscChip.Visibility = classic && hasQuery ? Visibility.Visible : Visibility.Collapsed;
        CountLabel.Visibility = !classic && _vm.HasResults ? Visibility.Visible : Visibility.Collapsed;
        CountLabel.Text = _vm.ResultCountText;

        // Footer: live indexing status when running, else the indexed-count caption.
        if (_status.IsIndexing)
        {
            FooterCaption.Text = $"인덱싱 중… {_status.Percent}%";
            SetDotPulse(true);
        }
        else
        {
            var count = (_status.HasCount ? _status.DocumentCount : _vm.IndexCount).ToString("N0");
            FooterCaption.Text = classic ? $"{count} 항목 인덱싱됨" : $"{count} 항목 · 전문 인덱스";
            SetDotPulse(false);
        }
    }

    /// <summary>Pulse the footer status dot while indexing (opacity 1 ↔ 0.45, looping).</summary>
    private void SetDotPulse(bool on)
    {
        if (on == _dotPulsing) return;
        _dotPulsing = on;
        if (on)
        {
            var pulse = new DoubleAnimation(1.0, 0.45, TimeSpan.FromSeconds(0.7))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
            };
            StatusDot.BeginAnimation(OpacityProperty, pulse);
        }
        else
        {
            StatusDot.BeginAnimation(OpacityProperty, null); // clear animation
            StatusDot.Opacity = 1.0;
        }
    }

    // ── Keyboard (↑/↓ wrap, Enter, Esc 1-clear/2-hide) ──────────────────────────
    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down: _vm.MoveSelection(+1); e.Handled = true; break;
            case Key.Up: _vm.MoveSelection(-1); e.Handled = true; break;
            case Key.Enter:
                bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
                HandleAction(shift ? "parent" : "open");
                e.Handled = true;
                break;
            case Key.Escape:
                if (SearchBox.Text.Length > 0) SearchBox.Text = ""; // 1st press clears
                else HideLauncher();                                 // 2nd press hides
                e.Handled = true;
                break;
        }
    }

    // ── Activation (Enter / row click / preview buttons) ────────────────────────
    private void HandleAction(string action)
    {
        var sel = _vm.Selected;
        if (sel is null) return;

        // Calculator: copy the value, then dismiss.
        if (sel.Kind == LaunchItemKind.Calc)
        {
            TrySetClipboard(sel.Title);
            HideLauncher();
            return;
        }

        var path = sel.Path;
        switch (action)
        {
            case "copy":
                TrySetClipboard(path);
                HideLauncher();
                break;
            case "reveal":
            case "parent":
                Actions.OpenParentFolder(path);
                HideLauncher();
                break;
            default: // "open"
                var r = Actions.OpenDefault(path);
                if (r.Ok) HideLauncher();
                else { Actions.OpenParentFolder(path); HideLauncher(); }
                break;
        }
    }

    private static void TrySetClipboard(string text)
    {
        try { Clipboard.SetText(text); } catch { /* clipboard busy */ }
    }
}

/// <summary>
/// A CSS-style <c>cubic-bezier(x1,y1,x2,y2)</c> easing function (control points P1/P2 with
/// P0=(0,0), P3=(1,1)). Solves x(t)=time by bisection, then returns y. Used for the
/// launcher's entrance curve <c>cubic-bezier(0.16, 1, 0.3, 1)</c>.
/// </summary>
internal sealed class CubicBezierEase : EasingFunctionBase
{
    private readonly double _x1, _y1, _x2, _y2;
    public CubicBezierEase(double x1, double y1, double x2, double y2)
    {
        _x1 = x1; _y1 = y1; _x2 = x2; _y2 = y2;
        // The bezier already encodes the full curve; use EaseInCore output directly
        // (EasingFunctionBase defaults to EaseOut, which would mirror it).
        EasingMode = EasingMode.EaseIn;
    }

    protected override double EaseInCore(double t)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;

        // Find parameter u where SampleX(u) ≈ t, then return SampleY(u).
        double lo = 0, hi = 1, u = t;
        for (int i = 0; i < 24; i++)
        {
            double x = Sample(u, _x1, _x2);
            if (Math.Abs(x - t) < 1e-5) break;
            if (x < t) lo = u; else hi = u;
            u = (lo + hi) / 2;
        }
        return Sample(u, _y1, _y2);
    }

    // Cubic bezier component with P0=0, P3=1: 3(1-u)^2 u p1 + 3(1-u) u^2 p2 + u^3
    private static double Sample(double u, double p1, double p2)
    {
        double mt = 1 - u;
        return 3 * mt * mt * u * p1 + 3 * mt * u * u * p2 + u * u * u;
    }

    protected override Freezable CreateInstanceCore() => new CubicBezierEase(_x1, _y1, _x2, _y2);
}

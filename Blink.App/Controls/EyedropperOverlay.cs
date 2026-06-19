// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
//
// Fullscreen screen color picker ("스포이드"). On open it snapshots the whole virtual screen once
// (GDI CopyFromScreen) and reads colors from that snapshot — so the near-transparent overlay never
// contaminates the sampled pixel. A follower shows the live color + hex; left-click commits,
// Esc / right-click cancels. Result is exposed via PickedHex ("#RRGGBB" or null).
//
// DPI note: pixel coordinates are physical (GetCursorPos + SM_*VIRTUALSCREEN); the follower is
// positioned in WPF device-independent units. On mixed-DPI multi-monitor setups the snapshot may be
// scaled — verify on Windows; the common single-DPI case is exact.
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Blink.App.Controls;

public sealed class EyedropperOverlay : Window
{
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT p);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private readonly System.Drawing.Bitmap _snapshot;
    private readonly int _vx;
    private readonly int _vy;
    private readonly Canvas _canvas = new();
    private readonly Border _swatch;
    private readonly TextBlock _hexText;
    private readonly StackPanel _follower;
    private string? _current;

    /// <summary>The picked color as "#RRGGBB", or null if cancelled.</summary>
    public string? PickedHex { get; private set; }

    public EyedropperOverlay()
    {
        // Snapshot the virtual screen (physical pixels) before the overlay is shown.
        _vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
        _vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        _snapshot = new System.Drawing.Bitmap(vw, vh, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(_snapshot))
            g.CopyFromScreen(_vx, _vy, 0, 0, new System.Drawing.Size(vw, vh));

        // Window: borderless, near-transparent (hit-testable), topmost, over the whole virtual screen.
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)); // ~invisible but receives mouse
        Topmost = true;
        ShowInTaskbar = false;
        Cursor = Cursors.Cross;
        Title = "Blink 색 추출";
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        // Follower: a color swatch + hex readout that trails the cursor.
        _swatch = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(6),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(2),
        };
        _hexText = new TextBlock
        {
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Consolas, Menlo, monospace"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 10, 0),
        };
        var hexChrome = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 12, 13, 18)),
            CornerRadius = new CornerRadius(6),
            Child = _hexText,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _follower = new StackPanel { Orientation = Orientation.Horizontal };
        _follower.Children.Add(_swatch);
        _follower.Children.Add(hexChrome);
        _canvas.Children.Add(_follower);
        Content = _canvas;

        MouseMove += OnMouseMove;
        MouseLeftButtonDown += OnLeftDown;
        MouseRightButtonDown += (_, _) => Cancel();
        Loaded += (_, _) => { Focus(); UpdateAtCursor(); };
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        UpdateAtCursor();
        var pos = e.GetPosition(_canvas);
        Canvas.SetLeft(_follower, pos.X + 18);
        Canvas.SetTop(_follower, pos.Y + 18);
    }

    private void UpdateAtCursor()
    {
        if (!GetCursorPos(out var p)) return;
        int sx = p.X - _vx;
        int sy = p.Y - _vy;
        if (sx < 0 || sy < 0 || sx >= _snapshot.Width || sy >= _snapshot.Height) return;

        var c = _snapshot.GetPixel(sx, sy);
        _current = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        _swatch.Background = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
        _hexText.Text = _current;
    }

    private void OnLeftDown(object sender, MouseButtonEventArgs e)
    {
        if (_current is null) return; // nothing sampled yet
        PickedHex = _current;
        DialogResult = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) Cancel();
    }

    private void Cancel()
    {
        PickedHex = null;
        DialogResult = false;
    }

    protected override void OnClosed(EventArgs e)
    {
        _snapshot.Dispose();
        base.OnClosed(e);
    }
}

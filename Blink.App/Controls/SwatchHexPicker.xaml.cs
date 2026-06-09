// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Blink.App.Mvvm;
using Blink.Core.Theming;

namespace Blink.App.Controls;

/// <summary>
/// Value-based color picker: optional 시스템 chip + preset swatch dots + a "#" hex field +
/// a live preview box. <see cref="SelectedHex"/> is the authoritative value; <see cref="IsSystem"/>
/// is set only via the chip. <see cref="ColorChanged"/> fires after any valid change.
/// </summary>
public partial class SwatchHexPicker : UserControl
{
    private static readonly Brush InvalidBrush = Frozen("#E0524A");
    private static readonly Brush TransparentRing = Brushes.Transparent;

    private readonly ObservableCollection<SwatchVm> _swatches = new();
    private string _selectedHex = "#0B0D12";
    private bool _isSystem;
    private bool _suppressEvents;

    public SwatchHexPicker()
    {
        InitializeComponent();
        SwatchList.ItemsSource = _swatches;
    }

    /// <summary>Raised after any valid change (a swatch, a valid hex edit, or 시스템 selection).</summary>
    public event Action? ColorChanged;

    /// <summary>Show the leading 시스템 chip (theme picker only). Set in XAML.</summary>
    public bool ShowSystemOption
    {
        get => SystemChip.Visibility == Visibility.Visible;
        set => SystemChip.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The current color as "#RRGGBB". Setting it updates the field, preview and swatch ring.</summary>
    public string SelectedHex
    {
        get => _selectedHex;
        set
        {
            if (!ColorMath.TryParseHex(value, out var rgb)) return;
            _selectedHex = ColorMath.ToHex(rgb.r, rgb.g, rgb.b);
            _isSystem = false;
            _suppressEvents = true;
            HexInput.Text = $"{rgb.r:X2}{rgb.g:X2}{rgb.b:X2}";
            _suppressEvents = false;
            HexInput.Foreground = (Brush)FindResource(Theming.ThemeManager.Txt);
            UpdatePreview(rgb);
            UpdateSwatchRings();
            UpdateSystemChipVisual();
        }
    }

    /// <summary>True when the user selected the 시스템 chip (theme follows the OS).</summary>
    public bool IsSystem => _isSystem;

    /// <summary>Enter system-following mode programmatically (host init), without firing ColorChanged.</summary>
    public void SetSystem()
    {
        if (SystemChip.Visibility != Visibility.Visible) return;
        _isSystem = true;
        HexBox.IsEnabled = false;
        HexBox.Opacity = 0.5;
        // Keep swatches hit-testable (only greyed) so a click can exit system mode.
        SwatchList.Opacity = 0.5;
        UpdateSwatchRings();
        UpdateSystemChipVisual();
    }

    /// <summary>Assign the preset swatches (called from the host window after construction).</summary>
    public IList<string> Presets
    {
        set
        {
            _swatches.Clear();
            foreach (var hex in value)
            {
                if (ColorMath.TryParseHex(hex, out var rgb))
                    _swatches.Add(new SwatchVm(ColorMath.ToHex(rgb.r, rgb.g, rgb.b),
                        Frozen(Color.FromRgb(rgb.r, rgb.g, rgb.b))));
            }
            UpdateSwatchRings();
        }
    }

    private void Swatch_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string hex }) return;
        SelectedHex = hex; // sets _isSystem=false, updates visuals
        ColorChanged?.Invoke();
    }

    private void HexInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents) return;
        var text = HexInput.Text;
        if (ColorMath.TryParseHex(text, out var rgb))
        {
            _selectedHex = ColorMath.ToHex(rgb.r, rgb.g, rgb.b);
            _isSystem = false;
            HexInput.Foreground = (Brush)FindResource(Theming.ThemeManager.Txt);
            UpdatePreview(rgb);
            UpdateSwatchRings();
            UpdateSystemChipVisual();
            ColorChanged?.Invoke();
        }
        else
        {
            // Invalid — redden the field, keep the last valid SelectedHex.
            HexInput.Foreground = InvalidBrush;
        }
    }

    private void SystemChip_Click(object sender, MouseButtonEventArgs e)
    {
        if (SystemChip.Visibility != Visibility.Visible) return;
        _isSystem = true;
        // Grey + disable the hex box and swatches while system-following is active.
        HexBox.IsEnabled = false;
        HexBox.Opacity = 0.5;
        // Keep swatches hit-testable (only greyed) so a click can exit system mode.
        SwatchList.Opacity = 0.5;
        UpdateSwatchRings();
        UpdateSystemChipVisual();
        ColorChanged?.Invoke();
    }

    private void UpdateSystemChipVisual()
    {
        if (SystemChip.Visibility != Visibility.Visible) return;
        if (_isSystem)
        {
            SystemChip.Background = (Brush)FindResource(Theming.ThemeManager.AccentSoft);
            SystemChip.BorderBrush = (Brush)FindResource(Theming.ThemeManager.AccentLine);
            SystemChipText.Foreground = (Brush)FindResource(Theming.ThemeManager.Txt);
        }
        else
        {
            SystemChip.Background = (Brush)FindResource(Theming.ThemeManager.Inset);
            SystemChip.BorderBrush = (Brush)FindResource(Theming.ThemeManager.Hairline);
            SystemChipText.Foreground = (Brush)FindResource(Theming.ThemeManager.TxtDim);
            // Re-enable the hex box/swatches when leaving system mode.
            HexBox.IsEnabled = true;
            HexBox.Opacity = 1.0;
            SwatchList.Opacity = 1.0;
        }
    }

    private void UpdatePreview((byte r, byte g, byte b) rgb)
    {
        Preview.Background = Frozen(Color.FromRgb(rgb.r, rgb.g, rgb.b));
    }

    private void UpdateSwatchRings()
    {
        var accent = (Brush)FindResource(Theming.ThemeManager.Accent);
        foreach (var s in _swatches)
            s.Ring = (!_isSystem && string.Equals(s.Hex, _selectedHex, StringComparison.OrdinalIgnoreCase))
                ? accent : TransparentRing;
    }

    private static SolidColorBrush Frozen(string hex)
    {
        ColorMath.TryParseHex(hex, out var rgb);
        return Frozen(Color.FromRgb(rgb.r, rgb.g, rgb.b));
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>One preset swatch: its hex, its fill brush, and a live selection ring brush.</summary>
    private sealed class SwatchVm : ObservableObject
    {
        private Brush _ring = Brushes.Transparent;

        public SwatchVm(string hex, Brush fill)
        {
            Hex = hex;
            Fill = fill;
        }

        public string Hex { get; }
        public Brush Fill { get; }
        public Brush Ring { get => _ring; set => Set(ref _ring, value); }
    }
}

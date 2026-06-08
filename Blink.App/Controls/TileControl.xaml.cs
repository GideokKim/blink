// Blink.App (WPF, Windows-only). NOT built on macOS — verify on Windows.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Blink.App.Controls;

public partial class TileControl : UserControl
{
    public TileControl() => InitializeComponent();

    public static readonly DependencyProperty TileSizeProperty = DependencyProperty.Register(
        nameof(TileSize), typeof(double), typeof(TileControl),
        new PropertyMetadata(40.0, OnTileSizeChanged));

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(TileControl), new PropertyMetadata("•"));

    public static readonly DependencyProperty GlyphBrushProperty = DependencyProperty.Register(
        nameof(GlyphBrush), typeof(Brush), typeof(TileControl), new PropertyMetadata(Brushes.Gray));

    public static readonly DependencyProperty IsMonoProperty = DependencyProperty.Register(
        nameof(IsMono), typeof(bool), typeof(TileControl), new PropertyMetadata(false, OnIsMonoChanged));

    public double TileSize { get => (double)GetValue(TileSizeProperty); set => SetValue(TileSizeProperty, value); }
    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public Brush GlyphBrush { get => (Brush)GetValue(GlyphBrushProperty); set => SetValue(GlyphBrushProperty, value); }
    public bool IsMono { get => (bool)GetValue(IsMonoProperty); set => SetValue(IsMonoProperty, value); }

    private static void OnTileSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var t = (TileControl)d;
        t.GlyphText.FontSize = (double)e.NewValue * 0.36; // matches the prototype's size*0.36
    }

    private static void OnIsMonoChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var t = (TileControl)d;
        var key = (bool)e.NewValue ? "Blink.Mono" : "Blink.Sora";
        t.GlyphText.SetResourceReference(TextBlock.FontFamilyProperty, key);
    }
}

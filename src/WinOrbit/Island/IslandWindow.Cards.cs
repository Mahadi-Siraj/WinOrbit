using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WinOrbit.Core;
using WinOrbit.Design;
using WinOrbit.Interop;
using WinOrbit.Services;
using WinOrbit.WindowsIntegration;
using Windows.Devices.Radios;

namespace WinOrbit.Island;

/// <summary>
/// Card pop-ups: the capsule swells into a taller card with an icon tile (or artwork), a title, a subtitle, an optional switch, and — for cards about a file — click, drag-out and button actions.
/// </summary>
public partial class IslandWindow
{
    // ------------------------------------------------------------------ card pop-ups
    private const double CardHeight = 64;
    private static readonly Color SwitchOff = Color.FromArgb(0x60, 0x78, 0x78, 0x80);
    private bool _card;
    private DispatcherTimer? _cardTimer, _switchDelay;

    /// <summary>
    /// A larger pop-up for connections, radios and lock keys: the capsule swells into a taller card with an icon
    /// tile, a title and a subtitle. Pieces arrive on their own springs (tile first with a bounce, then the text),
    /// and on/off states get a switch that visibly flips. A new card while one is showing updates it in place.
    /// </summary>
    /// <param name="cap">Show this letter in a key cap instead of an icon (lock keys).</param>
    /// <param name="toggle">Show an on/off switch that flips to this state.</param>
    /// <param name="switchColor">The switch's "on" colour when it differs from the tile (e.g. a grey "off" tile).</param>
    /// <param name="art">Fill the tile with this image (album artwork) instead of an icon.</param>
    /// <param name="onClick">Clicking the card does this (and closes it) instead of opening the island.</param>
    /// <param name="file">Dragging the card drags this file into another app.</param>
    /// <param name="actions">Small icon buttons on the right of the card.</param>
    public void ShowCard(string glyph, string title, string subtitle, Color color, double seconds = 3, bool? toggle = null, string? cap = null, Color? switchColor = null, ImageSource? art = null,
        Action? onClick = null, string? file = null, IReadOnlyList<(string Glyph, string Name, Action Do)>? actions = null)
    {
        if (_expanded || !IsVisible) return;
        var fresh = !_card;
        _card = true;
        _cardIsMedia = false;
        SetCardActions(onClick, file, actions);
        _hudTimer?.Stop(); _peekTimer.Stop();
        CHud.Visibility = Visibility.Collapsed; CPeek.Visibility = Visibility.Collapsed; CNormal.Visibility = Visibility.Visible;

        var ink = new SolidColorBrush(color); ink.Freeze();
        CardGlyph.Visibility = cap is null ? Visibility.Visible : Visibility.Collapsed;
        CardCap.Visibility = cap is null ? Visibility.Collapsed : Visibility.Visible;
        CardGlyph.Text = glyph; CardCap.Text = cap ?? "";
        CardGlyph.Foreground = ink; CardCap.Foreground = ink;
        var fill = new LinearGradientBrush(Color.FromArgb(0x50, color.R, color.G, color.B), Color.FromArgb(0x24, color.R, color.G, color.B), 90);
        fill.Freeze();
        CardTileFill.Background = fill;
        // Concentric with the card whatever the island's shape: each tile corner is the card's corner minus the margin.
        var outer = ShapeCorners(S.Shape, CardHeight / 2, Edge);
        static double Inset(double v) => Math.Max(5, v - 10);
        var tile = new CornerRadius(Inset(outer.TopLeft), Inset(outer.TopRight), Inset(outer.BottomRight), Inset(outer.BottomLeft));
        CardTileFill.CornerRadius = tile; CardTileEdge.CornerRadius = tile;
        if (art is not null) SetCardArt(art);
        CardTitle.Text = title;
        CardSub.Text = subtitle;
        CardSub.Visibility = string.IsNullOrWhiteSpace(subtitle) ? Visibility.Collapsed : Visibility.Visible;
        CardSwitch.Visibility = toggle is null ? Visibility.Collapsed : Visibility.Visible;

        Card.Visibility = Visibility.Visible;
        Card.Width = double.NaN;
        Card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = EvenWidth(Card.DesiredSize.Width + 20 + 10, 240, 460);
        // Fixed at its final width and centred: the growing glass reveals it from the middle outwards, and the
        // text never reflows (or truncates) mid-motion.
        Card.Width = width - 20;

        SetAutoHidden(false);
        Motion.To(Compact, OpacityProperty, 0, 90);
        // The capsule grows into the card on one spring, with a soft overshoot, like the island making room.
        Springs.To(Pill, WidthProperty, width, 0.46, 0.68);
        Springs.To(Pill, HeightProperty, CardHeight, 0.46, 0.68);
        Springs.To(this, RadiusProperty, CardHeight / 2, 0.46, 0.68);

        if (Motion.Intensity > 0.01)
        {
            if (fresh) { Card.Opacity = 0; Motion.To(Card, OpacityProperty, 1, 160); }
            // The tile lands first with a bounce; the title and subtitle glide in a beat behind it.
            CardTileScale.ScaleX = CardTileScale.ScaleY = fresh ? 0.35 : 0.8;
            Springs.To(CardTileScale, ScaleTransform.ScaleXProperty, 1, 0.42, 0.52);
            Springs.To(CardTileScale, ScaleTransform.ScaleYProperty, 1, 0.42, 0.52);
            CardTitleShift.X = 12; CardSubShift.X = 20;
            Springs.To(CardTitleShift, TranslateTransform.XProperty, 0, 0.42, 0.85);
            Springs.To(CardSubShift, TranslateTransform.XProperty, 0, 0.52, 0.85);
        }
        else Card.Opacity = 1;

        if (toggle is { } on) FlipSwitch(on, switchColor ?? color);

        _cardTimer ??= new DispatcherTimer();
        _cardTimer.Stop();
        _cardTimer.Interval = TimeSpan.FromSeconds(seconds);
        _cardTimer.Tick -= OnCardTimer;
        _cardTimer.Tick += OnCardTimer;
        _cardTimer.Start();
    }

    private void OnCardTimer(object? sender, EventArgs e)
    {
        // A card you can act on waits while the pointer rests on it.
        if (CardInteractive && Pill.IsMouseOver) return;
        EndCard();
    }

    private Action? _cardClick;
    private string? _cardFile;
    private bool _dragOut;
    private bool CardInteractive => _card && (_cardClick is not null || _cardFile is not null || CardActions.Children.Count > 0);

    private void SetCardActions(Action? onClick, string? file, IReadOnlyList<(string Glyph, string Name, Action Do)>? actions)
    {
        _cardClick = onClick;
        _cardFile = file;
        CardActions.Children.Clear();
        foreach (var (glyph, name, act) in actions ?? Array.Empty<(string, string, Action)>())
        {
            var b = new Button { Content = glyph, FontSize = 14, Width = 32, Height = 32, Margin = new Thickness(2, 0, 0, 0), ToolTip = name };
            b.SetResourceReference(StyleProperty, "Button.Icon");
            System.Windows.Automation.AutomationProperties.SetName(b, name);
            b.Click += (_, _) => { EndCard(); act(); };
            CardActions.Children.Add(b);
        }
        CardActions.Visibility = CardActions.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        // Plain cards let every click through to the capsule; cards with buttons take them.
        Card.IsHitTestVisible = CardActions.Children.Count > 0;
    }

    /// <summary>Drag the card's file out into another app (a chat, an email, a folder).</summary>
    private void DragCardFile(string file)
    {
        _cardTimer?.Stop();
        _dragOut = true;
        try { DragDrop.DoDragDrop(Pill, new DataObject(DataFormats.FileDrop, new[] { file }), DragDropEffects.Copy | DragDropEffects.Link); }
        catch (Exception ex) { Log.Warn("Card drag: " + ex.Message); }
        finally { _dragOut = false; EndCard(); }
    }

    /// <summary>Artwork fills the tile edge to edge; the icon and key cap step aside.</summary>
    private void SetCardArt(ImageSource art)
    {
        var brush = new ImageBrush(art) { Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
        brush.Freeze();
        CardTileFill.Background = brush;
        // Covers read as covers when square-ish: cap the corners at 12 so a capsule's circle tile becomes a rounded square.
        var c = CardTileFill.CornerRadius;
        var square = new CornerRadius(Math.Min(c.TopLeft, 12), Math.Min(c.TopRight, 12), Math.Min(c.BottomRight, 12), Math.Min(c.BottomLeft, 12));
        CardTileFill.CornerRadius = square; CardTileEdge.CornerRadius = square;
        CardGlyph.Visibility = Visibility.Collapsed;
        CardCap.Visibility = Visibility.Collapsed;
    }

    /// <summary>The switch starts in the old position and flips once the card has opened, so you see it change.</summary>
    private void FlipSwitch(bool on, Color color)
    {
        const double travel = 16;   // 40 track − 20 knob − 2 × 2 inset
        var track = new SolidColorBrush(on ? SwitchOff : color);
        CardSwitchTrack.Background = track;
        CardKnobShift.X = on ? 0 : travel;
        _switchDelay?.Stop();
        _switchDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Motion.Intensity > 0.01 ? 220 : 0) };
        _switchDelay.Tick += (_, _) =>
        {
            _switchDelay!.Stop();
            Springs.To(CardKnobShift, TranslateTransform.XProperty, on ? travel : 0, 0.3, 0.62);
            track.BeginAnimation(SolidColorBrush.ColorProperty,
                new System.Windows.Media.Animation.ColorAnimation(on ? color : SwitchOff, TimeSpan.FromMilliseconds(Motion.Intensity > 0.01 ? 200 : 0)));
        };
        _switchDelay.Start();
    }

    private void EndCard()
    {
        if (!_card) return;
        _card = false;
        _cardIsMedia = false;
        SetCardActions(null, null, null);
        _cardTimer?.Stop();
        _switchDelay?.Stop();
        Motion.To(Card, OpacityProperty, 0, 120, null, () => { if (!_card) Card.Visibility = Visibility.Collapsed; });
        if (_expanded) return;   // opening the panel takes over the size
        Motion.To(Compact, OpacityProperty, 1, 240);
        Springs.To(Pill, HeightProperty, CollapsedHeight, 0.42, 0.8);
        Springs.To(this, RadiusProperty, CollapsedRadius, 0.42, 0.8);
        Pop(CNormal, 0.92);
        RefreshCompact();   // springs the width back to the capsule's content
    }
}

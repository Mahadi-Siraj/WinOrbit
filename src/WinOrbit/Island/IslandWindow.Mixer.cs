using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinOrbit.Design;
using WinOrbit.Launcher;
using WinOrbit.Services;

namespace WinOrbit.Island;

/// <summary>
/// Volume by app, in the Control Centre: the mixer button beside the volume slider swaps the tiles for one slider per
/// app that has audio (the same sessions the Windows volume mixer lists). While it is open the list refreshes every
/// 1.5 s, so apps that start or stop playing, and changes made elsewhere, show up; it stops when the island closes.
/// </summary>
public partial class IslandWindow
{
    private const string GlyphMixer = "";
    private bool _mixerOpen, _mixerLoading;
    private DateTime _mixerTouched = DateTime.MinValue;
    private DispatcherTimer? _mixerTimer;
    private readonly Dictionary<string, (Slider Slider, TextBlock Value, Button Mute, FrameworkElement Row)> _mixerRows = new();
    private string _mixerShape = "";

    private void OnMixerButton(object sender, RoutedEventArgs e) { ShowMixer(!_mixerOpen); Poke(); }

    private void ShowMixer(bool open)
    {
        _mixerOpen = open;
        QShortcuts.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        QMixerScroll.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        QMixerButton.Content = open ? Glyph.Tiles : GlyphMixer;
        QMixerButton.ToolTip = open ? "Back to the controls" : "Volume by app";
        System.Windows.Automation.AutomationProperties.SetName(QMixerButton, (string)QMixerButton.ToolTip);
        if (open)
        {
            _mixerShape = "";   // rebuild the rows from scratch
            RenderMixer();
            Motion.Enter(QMixerScroll, 6, 200);
            _mixerTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(1.5), DispatcherPriority.Background, (_, _) => MixerTick(), Dispatcher);
            _mixerTimer.Start();
        }
        else
        {
            _mixerTimer?.Stop();
            Motion.Enter(QShortcuts, 6, 200);
        }
    }

    private void MixerTick()
    {
        if (!_expanded || _tab != "controls" || !IsVisible) { ShowMixer(false); return; }   // back to the tiles next time
        if ((DateTime.Now - _mixerTouched).TotalSeconds > 1.2) RenderMixer();   // don't fight a slider being moved
    }

    private void RenderMixer()
    {
        var apps = AppVolume.List();
        var shape = string.Join("|", apps.Select(a => a.Key + ":" + a.Active));
        if (shape != _mixerShape)
        {
            _mixerShape = shape;
            _mixerRows.Clear();
            QMixer.Children.Clear();
            if (apps.Count == 0)
                QMixer.Children.Add(Ui.Text("No apps are playing sound right now.", "Text.Caption", 12, brush: "T.Text3").With(t => t.Margin = new Thickness(4, 6, 0, 0)));
            foreach (var app in apps) QMixer.Children.Add(MixerRow(app));
        }
        _mixerLoading = true;
        foreach (var app in apps)
        {
            if (!_mixerRows.TryGetValue(app.Key, out var r)) continue;
            r.Slider.Value = Math.Round(app.Volume * 100);
            r.Value.Text = $"{app.Volume * 100:0}%";
            r.Mute.Content = app.Muted ? Glyph.Mute : Glyph.Volume;
            r.Slider.Opacity = app.Muted ? 0.45 : 1;
        }
        _mixerLoading = false;
    }

    private FrameworkElement MixerRow(AppVolume.App app)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6), Opacity = app.Active || app.System ? 1 : 0.7 };
        foreach (var w in new[] { 30.0, 116, double.NaN, 44, 38 })
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(w) ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });

        FrameworkElement icon = app.ExePath is { } exe && AppIcons.ForFile(exe, 32) is { } img
            ? new Image { Source = img, Width = 20, Height = 20 }
            : new TextBlock { Text = app.System ? Glyph.Volume : GlyphMixer, FontSize = 15, FontFamily = (FontFamily)FindResource("F.Icons") };
        icon.HorizontalAlignment = HorizontalAlignment.Center; icon.VerticalAlignment = VerticalAlignment.Center;
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);

        var name = Ui.Text(app.Name, "Text.Body", 12.5);
        name.TextTrimming = TextTrimming.CharacterEllipsis; name.TextWrapping = TextWrapping.NoWrap;
        name.VerticalAlignment = VerticalAlignment.Center; name.Margin = new Thickness(4, 0, 6, 0); name.ToolTip = app.Name;

        var slider = new Slider { Minimum = 0, Maximum = 100, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        System.Windows.Automation.AutomationProperties.SetName(slider, app.Name + " volume");
        var value = Ui.Text("", "Text.Caption", 12);
        value.TextAlignment = TextAlignment.Right; value.VerticalAlignment = VerticalAlignment.Center;
        var mute = new Button { Content = Glyph.Volume, FontSize = 13, Width = 30, Height = 30, ToolTip = "Mute " + app.Name };
        mute.SetResourceReference(StyleProperty, "Button.Icon");
        System.Windows.Automation.AutomationProperties.SetName(mute, "Mute " + app.Name);

        var key = app.Key;
        slider.ValueChanged += (_, e) =>
        {
            if (_mixerLoading) return;
            _mixerTouched = DateTime.Now;
            AppVolume.SetVolume(key, e.NewValue / 100);
            value.Text = $"{e.NewValue:0}%";
            Poke();
        };
        mute.Click += (_, _) =>
        {
            _mixerTouched = DateTime.Now;
            var muted = (string)mute.Content != Glyph.Mute;
            AppVolume.SetMute(key, muted);
            mute.Content = muted ? Glyph.Mute : Glyph.Volume;
            slider.Opacity = muted ? 0.45 : 1;
            Poke();
        };

        Grid.SetColumn(name, 1); Grid.SetColumn(slider, 2); Grid.SetColumn(value, 3); Grid.SetColumn(mute, 4);
        row.Children.Add(icon); row.Children.Add(name); row.Children.Add(slider); row.Children.Add(value); row.Children.Add(mute);
        _mixerRows[key] = (slider, value, mute, row);
        return row;
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Glasswave.Design;

/// <summary>Small themed modal dialogs: confirm, prompt, message. Used wherever the user must decide something.</summary>
public static class Dialogs
{
    private sealed class DialogWindow : Window
    {
        public readonly StackPanel Body = new();
        public readonly StackPanel Buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };

        public DialogWindow(string title, double width)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = width + 60;
            Title = title;
            SetResourceReference(FontFamilyProperty, "F.Family");
            SetResourceReference(ForegroundProperty, "T.Text");
            var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible && w is not DialogWindow && w.ShowInTaskbar);
            if (owner is not null) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            else { WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = true; }

            var card = new Border { Margin = new Thickness(30), Padding = new Thickness(26, 24, 26, 22), CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1) };
            card.SetResourceReference(Border.BackgroundProperty, "T.Popup");
            card.SetResourceReference(Border.BorderBrushProperty, "T.StrokeStrong");
            card.Effect = new DropShadowEffect { BlurRadius = 34, ShadowDepth = 10, Direction = 270, Opacity = 0.38 };
            var stack = new StackPanel();
            stack.Children.Add(Ui.Text(title, "Text.Sub", 18).With(t => t.Margin = new Thickness(0, 0, 0, 10)));
            stack.Children.Add(Body);
            stack.Children.Add(Buttons);
            card.Child = stack;
            Content = card;
            card.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch { } } };
            PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; } };
            Loaded += (_, _) => Motion.Enter(card, 10, 220);
        }

        public Button AddButton(string text, bool? result, string? style = null, bool isDefault = false)
        {
            var b = new Button { Content = text, MinWidth = 92, Margin = new Thickness(8, 0, 0, 0), IsDefault = isDefault, IsCancel = result == false };
            if (style is not null) b.SetResourceReference(StyleProperty, style);
            b.Click += (_, _) => DialogResult = result;
            Buttons.Children.Add(b);
            return b;
        }
    }

    public static bool Confirm(string title, string message, string ok = "Continue", string cancel = "Cancel", string? detail = null)
    {
        var d = new DialogWindow(title, 440);
        d.Body.Children.Add(Ui.Text(message, "Text.Muted"));
        if (!string.IsNullOrEmpty(detail))
        {
            var box = new Border { Margin = new Thickness(0, 14, 0, 0) };
            box.SetResourceReference(FrameworkElement.StyleProperty, "Card.Inset");
            box.Child = Ui.Text(detail, "Text.Caption");
            d.Body.Children.Add(box);
        }
        d.AddButton(cancel, false);
        d.AddButton(ok, true, "Button.Accent", true);
        return d.ShowDialog() == true;
    }

    public static void Message(string title, string message, string? detail = null)
    {
        var d = new DialogWindow(title, 440);
        d.Body.Children.Add(Ui.Text(message, "Text.Muted"));
        if (!string.IsNullOrEmpty(detail))
        {
            var box = new Border { Margin = new Thickness(0, 14, 0, 0) };
            box.SetResourceReference(FrameworkElement.StyleProperty, "Card.Inset");
            var tb = new TextBox { Text = detail, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 12 };
            tb.SetResourceReference(FrameworkElement.StyleProperty, "TextBox.Bare");
            box.Child = tb;
            d.Body.Children.Add(box);
        }
        d.AddButton("OK", true, "Button.Accent", true);
        d.ShowDialog();
    }

    /// <summary>Ask for one line of text. Returns null when cancelled.</summary>
    public static string? Prompt(string title, string label, string initial = "", string ok = "Save")
    {
        var d = new DialogWindow(title, 400);
        d.Body.Children.Add(Ui.Text(label, "Text.Muted").With(t => t.Margin = new Thickness(0, 0, 0, 10)));
        var box = new TextBox { Text = initial };
        d.Body.Children.Add(box);
        d.AddButton("Cancel", false);
        d.AddButton(ok, true, "Button.Accent", true);
        d.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return d.ShowDialog() == true ? box.Text.Trim() : null;
    }

    /// <summary>Pick one of several options. Returns the index or -1.</summary>
    public static int Choose(string title, string label, IList<string> options, int selected = 0)
    {
        var d = new DialogWindow(title, 400);
        d.Body.Children.Add(Ui.Text(label, "Text.Muted").With(t => t.Margin = new Thickness(0, 0, 0, 10)));
        var list = new ListBox { MaxHeight = 280 };
        foreach (var o in options) list.Items.Add(o);
        list.SelectedIndex = Math.Clamp(selected, 0, Math.Max(0, options.Count - 1));
        list.MouseDoubleClick += (_, _) => d.DialogResult = true;
        d.Body.Children.Add(list);
        d.AddButton("Cancel", false);
        d.AddButton("Select", true, "Button.Accent", true);
        return d.ShowDialog() == true ? list.SelectedIndex : -1;
    }
}

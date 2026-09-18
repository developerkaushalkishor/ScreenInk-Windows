using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ScreenInk.Core;

namespace ScreenInk.App.Views;

internal sealed class TextEntryWindow : Window
{
    private readonly System.Windows.Controls.TextBox _textBox;
    internal string Value => _textBox.Text;

    internal TextEntryWindow(string fontFamily, double fontSize, InkTextAlignment alignment)
    {
        Title = "ScreenInk Text";
        Width = 420;
        Height = 130;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        var panel = new DockPanel { Margin = new Thickness(12) };
        _textBox = new System.Windows.Controls.TextBox
        {
            FontFamily = new System.Windows.Media.FontFamily(fontFamily),
            FontSize = fontSize,
            VerticalContentAlignment = VerticalAlignment.Center,
            TextAlignment = alignment switch
            {
                InkTextAlignment.Center => TextAlignment.Center,
                InkTextAlignment.Right => TextAlignment.Right,
                _ => TextAlignment.Left
            }
        };
        _textBox.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter) { DialogResult = true; Close(); }
            else if (args.Key == Key.Escape) { DialogResult = false; Close(); }
        };
        panel.Children.Add(_textBox);
        Content = panel;
        Loaded += (_, _) => _textBox.Focus();
    }
}

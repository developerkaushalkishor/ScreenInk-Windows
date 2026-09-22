using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ScreenInk.App.Interop;
using ScreenInk.Core;

namespace ScreenInk.App.Views;

internal sealed class TextEntryWindow : Window
{
    private readonly System.Windows.Controls.TextBox _textBox;
    internal string Value => _textBox.Text;
    internal double TextWidth { get; }

    internal TextEntryWindow(InkStroke stroke, Point physicalOrigin, double availableWidth)
    {
        Title = "ScreenInk Text — Enter to save; Shift+Enter for a new line; Escape to cancel";
        TextWidth = Math.Min(stroke.TextWidth, availableWidth);
        Width = TextWidth + 2;
        MinHeight = stroke.FontSize * 1.6 + 2;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        _textBox = new System.Windows.Controls.TextBox
        {
            Text = stroke.Text ?? string.Empty,
            FontFamily = new FontFamily(stroke.FontFamily),
            FontSize = stroke.FontSize,
            Foreground = new SolidColorBrush(Color.FromRgb((byte)(stroke.Color >> 16),
                (byte)(stroke.Color >> 8), (byte)stroke.Color)),
            Background = new SolidColorBrush(Color.FromArgb(24, 20, 20, 24)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = stroke.TextAlignment switch
            {
                InkTextAlignment.Center => TextAlignment.Center,
                InkTextAlignment.Right => TextAlignment.Right,
                _ => TextAlignment.Left
            }
        };
        _textBox.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            { args.Handled = true; DialogResult = true; }
            else if (args.Key == Key.Escape) { args.Handled = true; DialogResult = false; }
        };
        Content = new Border { BorderBrush = Brushes.DeepSkyBlue, BorderThickness = new Thickness(1), Child = _textBox };
        SourceInitialized += (_, _) => NativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle,
            NativeMethods.HwndTopmost, (int)Math.Round(physicalOrigin.X) - 1,
            (int)Math.Round(physicalOrigin.Y) - 1, 0, 0, NativeMethods.SwpNoSize);
        Loaded += (_, _) => { Activate(); _textBox.Focus(); _textBox.CaretIndex = _textBox.Text.Length; };
    }
}

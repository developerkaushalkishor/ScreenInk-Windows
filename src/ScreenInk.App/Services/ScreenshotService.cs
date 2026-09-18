using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Forms;
using ScreenInk.App.Views;

namespace ScreenInk.App.Services;

internal static class ScreenshotService
{
    internal static void Capture(DisplayInfo display, Window toolbar)
    {
        var wasVisible = toolbar.IsVisible;
        toolbar.Hide();
        try
        {
            using var bitmap = new Bitmap((int)display.Width, (int)display.Height,
                PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.CopyFromScreen((int)display.Left, (int)display.Top, 0, 0, bitmap.Size,
                    CopyPixelOperation.SourceCopy);
            using var dialog = new SaveFileDialog
            {
                Filter = "PNG image (*.png)|*.png",
                FileName = $"ScreenInk {DateTime.Now:yyyy-MM-dd 'at' HH.mm.ss}.png",
                AddExtension = true,
                DefaultExt = "png"
            };
            if (dialog.ShowDialog() == DialogResult.OK)
                bitmap.Save(dialog.FileName, ImageFormat.Png);
        }
        finally
        {
            if (wasVisible) toolbar.Show();
        }
    }
}

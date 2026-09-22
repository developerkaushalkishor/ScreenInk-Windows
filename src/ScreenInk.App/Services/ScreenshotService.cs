using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using ScreenInk.App.Views;

namespace ScreenInk.App.Services;

internal static class ScreenshotService
{
    internal static async Task CaptureAsync(DisplayInfo display, ToolbarWindow toolbar, bool region, bool clipboard)
    {
        var wasVisible = toolbar.IsVisible;
        toolbar.ClosePopups();
        toolbar.Hide();
        try
        {
            // Wait for the desktop compositor to remove the toolbar and its popups.
            await Task.Delay(150);
            using var bitmap = new Bitmap((int)display.Width, (int)display.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.CopyFromScreen((int)display.Left, (int)display.Top, 0, 0, bitmap.Size,
                    CopyPixelOperation.SourceCopy);
            BitmapSource image;
            using (var stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);
                stream.Position = 0;
                image = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                image.Freeze();
            }
            if (region)
            {
                var picker = new CaptureRegionWindow(display, image);
                if (picker.ShowDialog() != true) return;
                image = new CroppedBitmap(image, picker.Region);
            }
            if (clipboard) System.Windows.Clipboard.SetImage(image);
            else
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "PNG image (*.png)|*.png",
                    FileName = $"ScreenInk {DateTime.Now:yyyy-MM-dd 'at' HH.mm.ss}.png",
                    AddExtension = true,
                    DefaultExt = ".png"
                };
                if (dialog.ShowDialog() == true)
                {
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                    using var output = File.Create(dialog.FileName); encoder.Save(output);
                }
            }
        }
        catch (Exception error)
        {
            System.Windows.MessageBox.Show($"The screenshot could not be saved. {error.Message}",
                "ScreenInk screenshot", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally { if (wasVisible) toolbar.ShowImmediately(); }
    }
}

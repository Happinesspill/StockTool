using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace StockTool.Services;

// 读取剪贴板图片：兼容微信输入法同步、截图工具的 PNG/文件格式
internal static class ClipboardImageHelper
{
    private static readonly string[] ImageFormats =
    [
        "PNG",
        "image/png",
        "image/jpeg",
        "JFIF",
        DataFormats.Bitmap,
        DataFormats.Dib,
        "DeviceIndependentBitmap"
    ];

    private static readonly string[] ImageExts = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"];

    public static BitmapSource? TryGetImage()
    {
        try
        {
            if (Clipboard.ContainsImage())
            {
                var img = Clipboard.GetImage();
                if (img is not null)
                    return Freeze(img);
            }
        }
        catch
        {
            // 微信等来源 ContainsImage 为真但 GetImage 失败
        }

        IDataObject? data;
        try
        {
            data = Clipboard.GetDataObject();
        }
        catch
        {
            data = null;
        }

        if (data is not null)
        {
            foreach (var format in ImageFormats)
            {
                if (!data.GetDataPresent(format, true)) continue;
                try
                {
                    var bmp = ToBitmap(data.GetData(format, true));
                    if (bmp is not null) return bmp;
                }
                catch
                {
                    // 尝试下一种格式
                }
            }

            if (data.GetDataPresent(DataFormats.FileDrop, true))
            {
                try
                {
                    if (data.GetData(DataFormats.FileDrop, true) is string[] files)
                    {
                        var file = files.FirstOrDefault(f =>
                            !string.IsNullOrWhiteSpace(f)
                            && File.Exists(f)
                            && ImageExts.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
                        if (file is not null)
                            return LoadFromFile(file);
                    }
                }
                catch
                {
                    // ignore
                }
            }
        }

        try
        {
            if (System.Windows.Forms.Clipboard.ContainsImage())
            {
                using var gdi = System.Windows.Forms.Clipboard.GetImage();
                if (gdi is not null)
                    return FromGdiImage(gdi);
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static BitmapSource? ToBitmap(object? data)
    {
        switch (data)
        {
            case BitmapSource src:
                return Freeze(src);
            case MemoryStream ms:
                return LoadFromStream(ms);
            case Stream stream:
                return LoadFromStream(stream);
            case byte[] bytes:
                return LoadFromStream(new MemoryStream(bytes));
            default:
                return null;
        }
    }

    private static BitmapSource? LoadFromFile(string path)
    {
        using var fs = File.OpenRead(path);
        return LoadFromStream(fs);
    }

    private static BitmapSource? LoadFromStream(Stream stream)
    {
        using var copy = new MemoryStream();
        stream.Position = stream.CanSeek ? stream.Position : 0;
        stream.CopyTo(copy);
        if (copy.Length == 0) return null;
        copy.Position = 0;
        var decoder = BitmapDecoder.Create(copy, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return decoder.Frames.Count > 0 ? Freeze(decoder.Frames[0]) : null;
    }

    private static BitmapSource FromGdiImage(System.Drawing.Image image)
    {
        using var bmp = image as System.Drawing.Bitmap ?? new System.Drawing.Bitmap(image);
        var hBitmap = bmp.GetHbitmap();
        try
        {
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            return Freeze(source);
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }

    private static BitmapSource Freeze(BitmapSource source)
    {
        if (source.CanFreeze)
            source.Freeze();
        return source;
    }

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}

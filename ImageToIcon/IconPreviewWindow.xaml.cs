using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImageToIcon
{
    public partial class IconPreviewWindow : Window
    {
        public IconPreviewWindow(string icoPath)
        {
            InitializeComponent();

            if (!File.Exists(icoPath))
            {
                MessageBox.Show("Icon file not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            try
            {
                using var fs = File.OpenRead(icoPath);
                var images = LoadImagesFromIcoStream(fs);

                ShowImages(images);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load preview: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        // Constructor to accept in-memory bytes (preview-before-save)
        public IconPreviewWindow(byte[] icoBytes)
        {
            InitializeComponent();

            try
            {
                using var ms = new MemoryStream(icoBytes);
                var images = LoadImagesFromIcoStream(ms);

                ShowImages(images);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load preview: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        private void ShowImages(List<(int size, BitmapImage bmp)> images)
        {
            if (images.Count == 0)
            {
                MessageBox.Show("No images found inside the .ico file.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
                return;
            }

            // Query DPI scale so we can display exact pixel sizes on any monitor.
            // WPF device-independent units (DIP) = physical pixels / DpiScaleX
            var dpi = VisualTreeHelper.GetDpi(this);
            double dpiScaleX = dpi.DpiScaleX; // e.g. 1.0 for 96dpi, 1.5 for 144dpi

            foreach (var (size, bmp) in images)
            {
                var sp = new StackPanel
                {
                    Margin = new Thickness(6),
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                // To display the icon at N physical pixels, set width/height in DIPs:
                double widthInDip = size / dpiScaleX;
                double heightInDip = size / dpiScaleX;

                var img = new Image
                {
                    Source = bmp,
                    Width = widthInDip,
                    Height = heightInDip,
                    Stretch = Stretch.Uniform, 
                    SnapsToDevicePixels = true
                };

                // Improve rendering crispness
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);

                var txt = new TextBlock
                {
                    Text = $"{size}×{size}",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 6, 0, 0)
                };

                sp.Children.Add(img);
                sp.Children.Add(txt);
                wpImages.Children.Add(sp);
            }
        }

        private static List<(int size, BitmapImage bmp)> LoadImagesFromIcoStream(Stream stream)
        {
            var list = new List<(int size, BitmapImage bmp)>();

            using var br = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);

            // Header
            stream.Seek(0, SeekOrigin.Begin);
            ushort reserved = br.ReadUInt16(); // 0
            ushort type = br.ReadUInt16();     // 1 for icon
            ushort count = br.ReadUInt16();

            if (reserved != 0 || (type != 1 && type != 2) || count == 0)
                return list;

            // Read directory entries (remember them first)
            var entries = new List<(int width, int height, int bytesInRes, int offset)>();
            for (int i = 0; i < count; i++)
            {
                byte widthByte = br.ReadByte();
                byte heightByte = br.ReadByte();
                br.ReadByte(); // colorCount
                br.ReadByte(); // reserved
                br.ReadInt16(); // planes
                br.ReadInt16(); // bitCount
                int bytesInRes = br.ReadInt32();
                int imageOffset = br.ReadInt32();

                int width = widthByte == 0 ? 256 : widthByte;
                int height = heightByte == 0 ? 256 : heightByte;

                entries.Add((width, height, bytesInRes, imageOffset));
            }

            // Extract each image
            foreach (var (width, height, bytesInRes, offset) in entries)
            {
                if (bytesInRes <= 0) continue;

                stream.Seek(offset, SeekOrigin.Begin);
                byte[] data = br.ReadBytes(bytesInRes);

                try
                {
                    // PNG signature: 89 50 4E 47 0D 0A 1A 0A
                    bool isPng = data.Length >= 8 &&
                                 data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47;

                    BitmapImage bmp;
                    if (isPng)
                    {
                        bmp = CreateBitmapImageFromPngBytes(data);
                    }
                    else
                    {
                        bmp = CreateBitmapImageFromDibBytes(data);
                    }

                    if (bmp != null)
                        list.Add((width, bmp));
                }
                catch
                {
                    // skip corrupt entry
                }
            }

            // Sort by ascending size
            list.Sort((a, b) => a.size.CompareTo(b.size));
            return list;
        }

        /// <summary>
        /// Create a BitmapImage directly from PNG bytes (in-memory).
        /// </summary>
        private static BitmapImage CreateBitmapImageFromPngBytes(byte[] pngBytes)
        {
            using var ms = new MemoryStream(pngBytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad; // load into memory so stream can be closed
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        /// <summary>
        /// Parse a BMP/DIB image (BITMAPINFOHEADER + pixel data + AND mask) produced by our Pack/CreateBmpIconDib,
        /// and convert it to a BitmapImage by creating a BitmapSource then encoding to PNG in-memory.
        /// width/height are the logical size stored in directory (height is for XOR image height).
        /// </summary>
        private static BitmapImage CreateBitmapImageFromDibBytes(byte[] dib)
        {
            if (dib.Length < 40) throw new InvalidOperationException("Invalid BMP/DIB in ICO.");

            using var ms = new MemoryStream(dib);
            using var r = new BinaryReader(ms);

            int biSize = r.ReadInt32();
            if (biSize < 40) throw new InvalidOperationException("Unsupported BITMAPINFOHEADER size.");

            // Read full BITMAPINFOHEADER fields explicitly (40 bytes total)
            int width = r.ReadInt32();
            int height2 = r.ReadInt32();
            short planes = r.ReadInt16();
            short bitCount = r.ReadInt16();
            int compression = r.ReadInt32();
            int sizeImage = r.ReadInt32();
            int xpels = r.ReadInt32();
            int ypels = r.ReadInt32();
            int clrUsed = r.ReadInt32();
            int clrImportant = r.ReadInt32();

            int height = Math.Abs(height2 / 2); // stored as XOR+AND => double height

            if (bitCount != 32) throw new InvalidOperationException("Only 32bpp BMP/DIB icons are supported by preview.");

            // Pixel data starts immediately after header
            int rowBytes = width * 4;
            int pixelDataSize = rowBytes * height;

            if (pixelDataSize < 0 || pixelDataSize > dib.Length - 40)
                throw new InvalidOperationException("BMP pixel data size appears invalid.");

            // Read pixel data (bottom-up order, as we wrote it)
            byte[] pixelData = r.ReadBytes(pixelDataSize);

            // Build top-down buffer for WPF
            var topDown = new byte[pixelDataSize];
            for (int y = 0; y < height; y++)
            {
                int srcRow = (height - 1 - y) * rowBytes;
                int dstRow = y * rowBytes;
                Array.Copy(pixelData, srcRow, topDown, dstRow, rowBytes);
            }

            // Create BitmapSource from raw BGRA bytes
            int stride = width * 4;
            var bmpSource = BitmapSource.Create(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, topDown, stride);

            // Encode to PNG in-memory, then load into BitmapImage so we can Freeze it easily and use same handling as PNG entries
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmpSource));
            using var outMs = new MemoryStream();
            encoder.Save(outMs);
            outMs.Position = 0;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = outMs;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }


    }
}

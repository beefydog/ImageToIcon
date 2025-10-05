//using System;
//using System.Collections.Generic;
//using System.IO;
//using System.Windows;
//using System.Windows.Controls;
//using System.Windows.Media.Imaging;

//namespace ImageToIcon
//{
//    public partial class IconPreviewWindow : Window
//    {
//        public IconPreviewWindow(string icoPath)
//        {
//            InitializeComponent();

//            if (!File.Exists(icoPath))
//            {
//                MessageBox.Show("Icon file not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
//                Close();
//                return;
//            }

//            try
//            {
//                var images = LoadImagesFromIco(icoPath);

//                if (images.Count == 0)
//                {
//                    MessageBox.Show("No images found inside the .ico file.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
//                    Close();
//                    return;
//                }

//                // For each extracted image, add a StackPanel with the image and a label
//                foreach (var (size, bmp) in images)
//                {
//                    var sp = new StackPanel
//                    {
//                        Margin = new Thickness(6),
//                        HorizontalAlignment = HorizontalAlignment.Center
//                    };

//                    var img = new Image
//                    {
//                        Source = bmp,
//                        Width = size,
//                        Height = size,
//                        Stretch = System.Windows.Media.Stretch.Uniform
//                    };

//                    var txt = new TextBlock
//                    {
//                        Text = $"{size} × {size}",
//                        HorizontalAlignment = HorizontalAlignment.Center,
//                        Margin = new Thickness(0, 6, 0, 0)
//                    };

//                    sp.Children.Add(img);
//                    sp.Children.Add(txt);
//                    wpImages.Children.Add(sp);
//                }
//            }
//            catch (Exception ex)
//            {
//                MessageBox.Show($"Failed to load preview: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
//                Close();
//            }
//        }

//        /// <summary>
//        /// Reads an .ico file and extracts image entries (PNG or BMP) as BitmapImage objects.
//        /// Returns list of (size, BitmapImage).
//        /// </summary>
//        private static List<(int size, BitmapImage bmp)> LoadImagesFromIco(string icoPath)
//        {
//            var list = new List<(int size, BitmapImage bmp)>();

//            using var fs = File.OpenRead(icoPath);
//            using var br = new BinaryReader(fs);

//            // Header
//            ushort reserved = br.ReadUInt16(); // 0
//            ushort type = br.ReadUInt16();     // 1 for icon
//            ushort count = br.ReadUInt16();

//            if (reserved != 0 || (type != 1 && type != 2) || count == 0)
//                return list;

//            // Read directory entries
//            var entries = new List<(int width, int height, int bytesInRes, int offset)>();
//            for (int i = 0; i < count; i++)
//            {
//                byte widthByte = br.ReadByte();
//                byte heightByte = br.ReadByte();
//                br.ReadByte(); // colorCount
//                br.ReadByte(); // reserved
//                br.ReadInt16(); // planes
//                br.ReadInt16(); // bitCount
//                int bytesInRes = br.ReadInt32();
//                int imageOffset = br.ReadInt32();

//                int width = widthByte == 0 ? 256 : widthByte;
//                int height = heightByte == 0 ? 256 : heightByte;

//                entries.Add((width, height, bytesInRes, imageOffset));
//            }

//            // Extract each image
//            foreach (var (width, height, bytesInRes, offset) in entries)
//            {
//                if (bytesInRes <= 0) continue;
//                fs.Seek(offset, SeekOrigin.Begin);
//                byte[] data = br.ReadBytes(bytesInRes);

//                try
//                {
//                    using var ms = new MemoryStream(data);
//                    var bmp = new BitmapImage();
//                    bmp.BeginInit();
//                    bmp.CacheOption = BitmapCacheOption.OnLoad;
//                    bmp.StreamSource = ms;
//                    bmp.EndInit();
//                    bmp.Freeze();

//                    list.Add((width, bmp));
//                }
//                catch
//                {
//                    // skip corrupt entry
//                }
//            }

//            // Sort by ascending size
//            list.Sort((a, b) => a.size.CompareTo(b.size));
//            return list;
//        }
//    }
//}


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

        /// <summary>
        /// Reads an .ico stream and extracts image entries (PNG or BMP) as BitmapImage objects.
        /// Returns list of (size, BitmapImage).
        /// </summary>
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
                    using var ms = new MemoryStream(data);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad; // load into memory so we can close stream
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();

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
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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
                var images = LoadImagesFromIco(icoPath);

                if (images.Count == 0)
                {
                    MessageBox.Show("No images found inside the .ico file.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    Close();
                    return;
                }

                // For each extracted image, add a StackPanel with the image and a label
                foreach (var (size, bmp) in images)
                {
                    var sp = new StackPanel
                    {
                        Margin = new Thickness(6),
                        HorizontalAlignment = HorizontalAlignment.Center
                    };

                    var img = new Image
                    {
                        Source = bmp,
                        Width = size,
                        Height = size,
                        Stretch = System.Windows.Media.Stretch.Uniform
                    };

                    var txt = new TextBlock
                    {
                        Text = $"{size} × {size}",
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 6, 0, 0)
                    };

                    sp.Children.Add(img);
                    sp.Children.Add(txt);
                    wpImages.Children.Add(sp);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load preview: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        /// <summary>
        /// Reads an .ico file and extracts image entries (PNG or BMP) as BitmapImage objects.
        /// Returns list of (size, BitmapImage).
        /// </summary>
        private static List<(int size, BitmapImage bmp)> LoadImagesFromIco(string icoPath)
        {
            var list = new List<(int size, BitmapImage bmp)>();

            using var fs = File.OpenRead(icoPath);
            using var br = new BinaryReader(fs);

            // Header
            ushort reserved = br.ReadUInt16(); // 0
            ushort type = br.ReadUInt16();     // 1 for icon
            ushort count = br.ReadUInt16();

            if (reserved != 0 || (type != 1 && type != 2) || count == 0)
                return list;

            // Read directory entries
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
                fs.Seek(offset, SeekOrigin.Begin);
                byte[] data = br.ReadBytes(bytesInRes);

                try
                {
                    using var ms = new MemoryStream(data);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
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

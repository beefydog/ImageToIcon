using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Png;

namespace ImageToIcon
{
    public partial class MainWindow : Window
    {
        private List<string> _selectedFilePaths = [];

        // Standard icon sizes.
        private readonly int[] _requiredSizes = [16, 24, 32, 48, 64, 72, 96, 128, 256];

        public MainWindow()
        {
            InitializeComponent();
        }

        private void BtnSelectImages_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "All Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.ico|" +
                         "PNG (*.png)|*.png|" +
                         "JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg|" +
                         "Bitmap (*.bmp)|*.bmp|" +
                         "GIF (*.gif)|*.gif|" +
                         "TIFF (*.tif;*.tiff)|*.tif;*.tiff|" +
                         "WebP (*.webp)|*.webp|" +
                         "Icon (*.ico)|*.ico",
                Multiselect = true
            };

            if (dlg.ShowDialog() == true)
            {
                _selectedFilePaths = [.. dlg.FileNames];
                lstSelectedFiles.ItemsSource = _selectedFilePaths;
                btnCreateIcon.IsEnabled = _selectedFilePaths.Count != 0;
                txtStatus.Text = btnCreateIcon.IsEnabled
                    ? $"Selected {_selectedFilePaths.Count} file(s)."
                    : "No files selected.";
            }
        }

        private void BtnCreateIcon_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFilePaths.Count == 0)
            {
                MessageBox.Show("Please select at least one image file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                string largestPath = _selectedFilePaths
                    .OrderByDescending(f =>
                    {
                        var info = Image.Identify(f)
                                   ?? throw new InvalidOperationException("Cannot read image info.");
                        return info.Width * info.Height;
                    })
                    .First();

                // Load & convert to Rgba32 (32‑bit RGBA).
                using Image<Rgba32> original = Image.Load<Rgba32>(largestPath);

                // Generate resized image blobs.
                var imgList = new List<(int size, byte[] img)>();
                foreach (int size in _requiredSizes)
                {
                    using Image<Rgba32> clone = original.Clone(ctx => ctx.Resize(new ResizeOptions
                    {
                        Size = new SixLabors.ImageSharp.Size(size, size),
                        Sampler = KnownResamplers.Lanczos3,
                        Mode = SixLabors.ImageSharp.Processing.ResizeMode.Max,
                        Compand = true
                    }));
                    using var ms = new MemoryStream();
                    clone.Save(ms, new PngEncoder());
                    imgList.Add((size, ms.ToArray()));
                }

                byte[] icoBytes = CreateIconFromImages(imgList);

                var saveDlg = new SaveFileDialog
                {
                    Filter = "Icon Files (*.ico)|*.ico",
                    DefaultExt = "ico",
                    FileName = "app.ico"
                };
                if (saveDlg.ShowDialog() == true)
                {
                    File.WriteAllBytes(saveDlg.FileName, icoBytes);
                    MessageBox.Show("Icon created successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    txtStatus.Text = "Icon file saved.";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to create icon: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Packs header, directory, and image data into a .ico byte array.
        private static byte[] CreateIconFromImages(List<(int size, byte[] img)> images)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            // ICO header.
            writer.Write((short)0);
            writer.Write((short)1);
            writer.Write((short)images.Count);

            // Directory entries.
            int offset = 6 + (16 * images.Count);
            foreach (var (size, img) in images)
            {
                byte b = (byte)(size >= 256 ? 0 : size);
                writer.Write(b);                   // width
                writer.Write(b);                   // height
                writer.Write((byte)0);             // color palette
                writer.Write((byte)0);             // reserved
                writer.Write((short)1);            // color planes
                writer.Write((short)32);           // bits per pixel
                writer.Write(img.Length);          // data length
                writer.Write(offset);              // data offset
                offset += img.Length;
            }

            // Image data.
            foreach (var (_, img) in images)
            {
                writer.Write(img);
            }

            return ms.ToArray();
        }
    }
}
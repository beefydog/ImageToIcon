using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
        private static readonly int[] _requiredSizes = [16, 24, 32, 48, 64, 72, 96, 128, 256];

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

        private async void BtnCreateIcon_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFilePaths.Count == 0)
            {
                MessageBox.Show("Please select at least one image file.",
                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                // Run all image processing off the UI thread:
                byte[] icoBytes = await Task.Run(() => CreateIconBytes(_selectedFilePaths));

                // Prompt for save (UI thread):
                var saveDlg = new SaveFileDialog
                {
                    Filter = "Icon Files (*.ico)|*.ico",
                    DefaultExt = "ico",
                    FileName = "app.ico"
                };
                if (saveDlg.ShowDialog() == true)
                {
                    await File.WriteAllBytesAsync(saveDlg.FileName, icoBytes);
                    MessageBox.Show("Icon created successfully!",
                                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    txtStatus.Text = "Icon file saved.";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to create icon: {ex.Message}",
                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        //private static byte[] CreateIconBytes(IEnumerable<string> filePaths)
        //{
        //    // pick largest image by pixel count without a using:
        //    string largest = filePaths
        //        .OrderByDescending(path =>
        //        {
        //            byte[] data = File.ReadAllBytes(path);
        //            var info = Image.Identify(data)
        //                       ?? throw new InvalidOperationException("Cannot read image info.");
        //            return info.Width * info.Height;
        //        })
        //        .First();

        //    // now load & resize exactly as before...
        //    byte[] originalBytes = File.ReadAllBytes(largest);
        //    using var original = Image.Load<Rgba32>(originalBytes);

        //    var bag = new ConcurrentBag<(int size, byte[] png)>();
        //    Parallel.ForEach(_requiredSizes, size =>
        //    {
        //        using var resized = original.Clone(ctx => ctx.Resize(size, size));
        //        using var ms = new MemoryStream();
        //        resized.Save(ms, new PngEncoder());
        //        bag.Add((size, ms.ToArray()));
        //    });

        //    return PackIco([.. bag.OrderBy(x => x.size)]);
        //}

        private static byte[] CreateIconBytes(IEnumerable<string> filePaths)
        {
            // Collect required sizes into a HashSet for quick lookup
            var requiredSet = new HashSet<int>(_requiredSizes);

            // Map of sizes already provided by input files -> PNG bytes
            var provided = new Dictionary<int, byte[]>();

            // We'll need to pick the largest image (by pixel count) for resizing.
            // Keep a list of tuples (path, width, height, area) for that:
            var infos = new List<(string path, int w, int h, long area)>();

            foreach (var path in filePaths)
            {
                byte[] data = File.ReadAllBytes(path);
                var info = Image.Identify(data) ?? throw new InvalidOperationException($"Cannot read image info: {path}");

                // Normalize width/height into ints
                int w = info.Width;
                int h = info.Height;

                // Record for largest selection
                infos.Add((path, w, h, (long)w * h));

                // If image is square and matches a required size, register it as provided.
                // Convert it to PNG bytes so PackIco always receives PNG data.
                if (w == h && requiredSet.Contains(w))
                {
                    // Avoid duplicate registrations (first wins)
                    if (!provided.ContainsKey(w))
                    {
                        // Convert to PNG bytes (handles any input format)
                        using var img = Image.Load<Rgba32>(data);
                        using var ms = new MemoryStream();
                        img.Save(ms, new PngEncoder());
                        provided[w] = ms.ToArray();
                    }
                }
            }

            if (infos.Count == 0)
                throw new InvalidOperationException("No valid images found.");

            // Choose the largest image by area (same as before)
            var largestInfo = infos.OrderByDescending(x => x.area).First();
            byte[] largestBytes = File.ReadAllBytes(largestInfo.path);
            using var original = Image.Load<Rgba32>(largestBytes);

            // Build final list of (size, png bytes). For sizes present in `provided`, use those.
            // For sizes not present, resize the largest image.
            var bag = new ConcurrentBag<(int size, byte[] png)>();

            Parallel.ForEach(_requiredSizes, size =>
            {
                if (provided.TryGetValue(size, out var pngBytes))
                {
                    // Use the provided image (already PNG)
                    bag.Add((size, pngBytes));
                }
                else
                {
                    // Resize largest image to this size
                    using var resized = original.Clone(ctx => ctx.Resize(size, size));
                    using var ms = new MemoryStream();
                    resized.Save(ms, new PngEncoder());
                    bag.Add((size, ms.ToArray()));
                }
            });

            // Ensure deterministic ordering (ascending sizes) when packing
            var list = bag.OrderBy(x => x.size).ToList();
            return PackIco(list);
        }


        private static byte[] PackIco(List<(int size, byte[] png)> images)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);

            w.Write((short)0);               // reserved
            w.Write((short)1);               // type = icon
            w.Write((short)images.Count);

            int offset = 6 + 16 * images.Count;
            foreach (var (size, png) in images)
            {
                byte b = (byte)(size >= 256 ? 0 : size);
                w.Write(b);                  // width
                w.Write(b);                  // height
                w.Write((byte)0);            // palette
                w.Write((byte)0);            // reserved
                w.Write((short)1);           // planes
                w.Write((short)32);          // bpp
                w.Write(png.Length);         // data length
                w.Write(offset);
                offset += png.Length;
            }

            foreach (var (_, png) in images)
            {
                w.Write(png);
            }

            return ms.ToArray();
        }

        // Drag & Drop handlers:
        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effects = DragDropEffects.Copy;
            else
                e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                _selectedFilePaths = [.. files];
                lstSelectedFiles.ItemsSource = _selectedFilePaths;
                btnCreateIcon.IsEnabled = _selectedFilePaths.Count != 0;
                txtStatus.Text = $"Selected {_selectedFilePaths.Count} file(s).";
            }
        }
    }
}

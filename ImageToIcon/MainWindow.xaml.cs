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

        // Button click stays async (UI thread)
        private async void BtnCreateIcon_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFilePaths.Count == 0)
            {
                MessageBox.Show("Please select at least one image file.",
                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            btnCreateIcon.IsEnabled = false;
            txtStatus.Text = "Preparing...";

            // Progress reports run on the UI context (Progress<T> captures SynchronizationContext)
            var progress = new Progress<string>(s => txtStatus.Text = s);

            try
            {
                // Run the CPU-bound image processing on a threadpool thread,
                // but pass an IProgress<string> so the worker can update UI.
                byte[] icoBytes = await Task.Run(() =>
                    CreateIconBytes(_selectedFilePaths, progress));

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
                    txtStatus.Text = $"Icon saved to {saveDlg.FileName}";
                }
                else
                {
                    txtStatus.Text = "Icon creation completed (not saved).";
                }
            }
            catch (Exception ex)
            {
                // Show a short, user-friendly message. If you want more detail for power users, append ex.ToString().
                MessageBox.Show($"Failed to create icon: {ex.Message}",
                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);

                // Also reflect in the status bar
                txtStatus.Text = $"Failed: {ex.Message}";
            }
            finally
            {
                btnCreateIcon.IsEnabled = true;
            }
        }


        // Updated CreateIconBytes that reports progress and handles per-file errors
        private static byte[] CreateIconBytes(IEnumerable<string> filePaths, IProgress<string>? progress)
        {
            progress?.Report("Scanning images...");

            var requiredSet = new HashSet<int>(_requiredSizes);

            var provided = new Dictionary<int, byte[]>(); // sizes exactly provided
            var infos = new List<(string path, int w, int h, long area)>();
            var errors = new List<string>();

            foreach (var path in filePaths)
            {
                try
                {
                    byte[] data = File.ReadAllBytes(path);
                    var info = Image.Identify(data)
                               ?? throw new InvalidOperationException("Cannot read image info.");

                    int w = info.Width;
                    int h = info.Height;

                    infos.Add((path, w, h, (long)w * h));

                    if (w == h && requiredSet.Contains(w))
                    {
                        if (!provided.ContainsKey(w))
                        {
                            using var img = Image.Load<Rgba32>(data);
                            using var ms = new MemoryStream();
                            img.Save(ms, new PngEncoder());
                            provided[w] = ms.ToArray();
                            progress?.Report($"Found provided image for {w}x{w}: {Path.GetFileName(path)}");
                        }
                        else
                        {
                            // duplicate provided size — keep first, but note it
                            progress?.Report($"Ignored additional {w}x{w}: {Path.GetFileName(path)}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"{Path.GetFileName(path)} ({ex.Message})");
                }
            }

            if (infos.Count == 0)
            {
                // All files failed to load
                string msg = "No valid images found.";
                if (errors.Count > 0)
                    msg += " Errors: " + string.Join("; ", errors);
                throw new InvalidOperationException(msg);
            }

            if (errors.Count > 0)
            {
                // Report non-fatal errors to user via progress (they will also see final exception if everything fails)
                progress?.Report($"Some files skipped: {string.Join(", ", errors.Select(e => e.Split(' ')[0]))}");
            }

            // Choose largest image by area
            var largestInfo = infos.OrderByDescending(x => x.area).First();
            progress?.Report($"Using {Path.GetFileName(largestInfo.path)} as source for resizing ({largestInfo.w}x{largestInfo.h}).");

            byte[] largestBytes = File.ReadAllBytes(largestInfo.path);
            using var original = Image.Load<Rgba32>(largestBytes);

            var providedSizes = provided.Keys.OrderBy(x => x).ToList();
            var resizedSizes = new List<int>();
            var bag = new ConcurrentBag<(int size, byte[] png)>();

            Parallel.ForEach(_requiredSizes, size =>
            {
                if (provided.TryGetValue(size, out var pngBytes))
                {
                    bag.Add((size, pngBytes));
                }
                else
                {
                    // Resize the largest image to this size
                    using var resized = original.Clone(ctx => ctx.Resize(size, size));
                    using var ms = new MemoryStream();
                    resized.Save(ms, new PngEncoder());
                    bag.Add((size, ms.ToArray()));
                    // track resized sizes in a thread-safe manner by collecting to a concurrent bag or lock
                    lock (resizedSizes) { resizedSizes.Add(size); }
                }
            });

            providedSizes.Sort();
            resizedSizes.Sort();

            progress?.Report(
                $"Provided: {(providedSizes.Count == 0 ? "none" : string.Join(", ", providedSizes.Select(s => s + "x" + s)))}; " +
                $"Resized: {(resizedSizes.Count == 0 ? "none" : string.Join(", ", resizedSizes.Select(s => s + "x" + s)))}"
            );

            var list = bag.OrderBy(x => x.size).ToList();
            progress?.Report("Packing .ico...");
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

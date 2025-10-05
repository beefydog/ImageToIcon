//using System;
//using System.Collections.Concurrent;
//using System.Collections.Generic;
//using System.Diagnostics;
//using System.IO;
//using System.Linq;
//using System.Threading.Tasks;
//using System.Windows;
//using Microsoft.Win32;
//using SixLabors.ImageSharp;
//using SixLabors.ImageSharp.PixelFormats;
//using SixLabors.ImageSharp.Processing;
//using SixLabors.ImageSharp.Formats.Png;

//namespace ImageToIcon
//{
//    public partial class MainWindow : Window
//    {
//        // keep up to 9 files
//        private List<string> _selectedFilePaths = new List<string>();
//        private string? _lastSavedIconPath;
//        private const int _maxFiles = 9;
//        private static readonly int[] _requiredSizes = new[] { 16, 24, 32, 48, 64, 72, 96, 128, 256 };

//        public MainWindow()
//        {
//            InitializeComponent();

//            // initialize UI state
//            HideOpenFolderAndPreview();
//        }

//        private void BtnSelectImages_Click(object sender, RoutedEventArgs e)
//        {
//            var dlg = new OpenFileDialog
//            {
//                Filter = "All Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.ico|" +
//                         "PNG (*.png)|*.png|" +
//                         "JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg|" +
//                         "Bitmap (*.bmp)|*.bmp|" +
//                         "GIF (*.gif)|*.gif|" +
//                         "TIFF (*.tif;*.tiff)|*.tif;*.tiff|" +
//                         "WebP (*.webp)|*.webp|" +
//                         "Icon (*.ico)|*.ico",
//                Multiselect = true
//            };

//            if (dlg.ShowDialog() == true)
//            {
//                var chosen = dlg.FileNames.ToList();

//                if (chosen.Count > _maxFiles)
//                {
//                    MessageBox.Show($"You selected {chosen.Count} files. The maximum is {_maxFiles}. Only the first {_maxFiles} will be used.",
//                                    "Too many files", MessageBoxButton.OK, MessageBoxImage.Warning);
//                    chosen = chosen.Take(_maxFiles).ToList();
//                }

//                _selectedFilePaths = chosen;
//                lstSelectedFiles.ItemsSource = null;
//                lstSelectedFiles.ItemsSource = _selectedFilePaths;

//                btnCreateIcon.IsEnabled = _selectedFilePaths.Count != 0;
//                txtStatus.Text = btnCreateIcon.IsEnabled
//                    ? $"Selected {_selectedFilePaths.Count} file(s)."
//                    : "No files selected.";

//                // Auto-hide Open Folder & Preview when user selects new images
//                HideOpenFolderAndPreview();
//            }
//        }

//        // Button click stays async (UI thread)
//        private async void BtnCreateIcon_Click(object sender, RoutedEventArgs e)
//        {
//            if (_selectedFilePaths.Count == 0)
//            {
//                MessageBox.Show("Please select at least one image file.",
//                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
//                return;
//            }

//            btnCreateIcon.IsEnabled = false;
//            txtStatus.Text = "Preparing...";

//            // Progress reports run on the UI context (Progress<T> captures SynchronizationContext)
//            var progress = new Progress<string>(s => txtStatus.Text = s);

//            try
//            {
//                // Run the CPU-bound image processing on a threadpool thread,
//                // but pass an IProgress<string> so the worker can update UI.
//                byte[] icoBytes = await Task.Run(() =>
//                    CreateIconBytes(_selectedFilePaths, progress));

//                var saveDlg = new SaveFileDialog
//                {
//                    Filter = "Icon Files (*.ico)|*.ico",
//                    DefaultExt = "ico",
//                    FileName = "app.ico"
//                };

//                if (saveDlg.ShowDialog() == true)
//                {
//                    await File.WriteAllBytesAsync(saveDlg.FileName, icoBytes);

//                    _lastSavedIconPath = saveDlg.FileName;

//                    // Show and enable Open Folder; set tooltip to saved path
//                    btnOpenFolder.Visibility = Visibility.Visible;
//                    btnOpenFolder.IsEnabled = true;
//                    btnOpenFolder.ToolTip = _lastSavedIconPath;

//                    // Show and enable Preview button
//                    btnPreview.Visibility = Visibility.Visible;
//                    btnPreview.IsEnabled = true;
//                    btnPreview.ToolTip = "Preview the saved icon";

//                    MessageBox.Show("Icon created successfully!",
//                                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);
//                    txtStatus.Text = $"Icon saved to {_lastSavedIconPath}";
//                }
//                else
//                {
//                    txtStatus.Text = "Icon creation completed (not saved).";
//                }
//            }
//            catch (Exception ex)
//            {
//                // Show a short, user-friendly message.
//                MessageBox.Show($"Failed to create icon: {ex.Message}",
//                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);

//                // Also reflect in the status bar
//                txtStatus.Text = $"Failed: {ex.Message}";
//            }
//            finally
//            {
//                btnCreateIcon.IsEnabled = true;
//            }
//        }


//        // Updated CreateIconBytes that reports progress and handles per-file errors
//        private static byte[] CreateIconBytes(IEnumerable<string> filePaths, IProgress<string>? progress)
//        {
//            progress?.Report("Scanning images...");

//            var requiredSet = new HashSet<int>(_requiredSizes);

//            var provided = new Dictionary<int, byte[]>(); // sizes exactly provided
//            var infos = new List<(string path, int w, int h, long area)>();
//            var errors = new List<string>();

//            foreach (var path in filePaths)
//            {
//                try
//                {
//                    byte[] data = File.ReadAllBytes(path);
//                    var info = Image.Identify(data)
//                               ?? throw new InvalidOperationException("Cannot read image info.");

//                    int w = info.Width;
//                    int h = info.Height;

//                    infos.Add((path, w, h, (long)w * h));

//                    if (w == h && requiredSet.Contains(w))
//                    {
//                        if (!provided.ContainsKey(w))
//                        {
//                            using var img = Image.Load<Rgba32>(data);
//                            using var ms = new MemoryStream();
//                            img.Save(ms, new PngEncoder());
//                            provided[w] = ms.ToArray();
//                            progress?.Report($"Found provided image for {w}x{w}: {Path.GetFileName(path)}");
//                        }
//                        else
//                        {
//                            // duplicate provided size — keep first, but note it
//                            progress?.Report($"Ignored additional {w}x{w}: {Path.GetFileName(path)}");
//                        }
//                    }
//                }
//                catch (Exception ex)
//                {
//                    errors.Add($"{Path.GetFileName(path)} ({ex.Message})");
//                }
//            }

//            if (infos.Count == 0)
//            {
//                // All files failed to load
//                string msg = "No valid images found.";
//                if (errors.Count > 0)
//                    msg += " Errors: " + string.Join("; ", errors);
//                throw new InvalidOperationException(msg);
//            }

//            if (errors.Count > 0)
//            {
//                // Report non-fatal errors to user via progress
//                progress?.Report($"Some files skipped: {string.Join(", ", errors.Select(e => e.Split(' ')[0]))}");
//            }

//            // Choose largest image by area
//            var largestInfo = infos.OrderByDescending(x => x.area).First();
//            progress?.Report($"Using {Path.GetFileName(largestInfo.path)} as source for resizing ({largestInfo.w}x{largestInfo.h}).");

//            byte[] largestBytes = File.ReadAllBytes(largestInfo.path);
//            using var original = Image.Load<Rgba32>(largestBytes);

//            var providedSizes = provided.Keys.OrderBy(x => x).ToList();
//            var resizedSizes = new List<int>();
//            var bag = new ConcurrentBag<(int size, byte[] png)>();

//            Parallel.ForEach(_requiredSizes, size =>
//            {
//                if (provided.TryGetValue(size, out var pngBytes))
//                {
//                    bag.Add((size, pngBytes));
//                }
//                else
//                {
//                    // Resize the largest image to this size
//                    using var resized = original.Clone(ctx => ctx.Resize(size, size));
//                    using var ms = new MemoryStream();
//                    resized.Save(ms, new PngEncoder());
//                    bag.Add((size, ms.ToArray()));
//                    lock (resizedSizes) { resizedSizes.Add(size); }
//                }
//            });

//            providedSizes.Sort();
//            resizedSizes.Sort();

//            progress?.Report(
//                $"Provided: {(providedSizes.Count == 0 ? "none" : string.Join(", ", providedSizes.Select(s => s + "x" + s)))}; " +
//                $"Resized: {(resizedSizes.Count == 0 ? "none" : string.Join(", ", resizedSizes.Select(s => s + "x" + s)))}"
//            );

//            var list = bag.OrderBy(x => x.size).ToList();
//            progress?.Report("Packing .ico...");
//            return PackIco(list);
//        }

//        private static byte[] PackIco(List<(int size, byte[] png)> images)
//        {
//            using var ms = new MemoryStream();
//            using var w = new BinaryWriter(ms);

//            w.Write((short)0);               // reserved
//            w.Write((short)1);               // type = icon
//            w.Write((short)images.Count);

//            int offset = 6 + 16 * images.Count;
//            foreach (var (size, png) in images)
//            {
//                byte b = (byte)(size >= 256 ? 0 : size);
//                w.Write(b);                  // width
//                w.Write(b);                  // height
//                w.Write((byte)0);            // palette
//                w.Write((byte)0);            // reserved
//                w.Write((short)1);           // planes
//                w.Write((short)32);          // bpp
//                w.Write(png.Length);         // data length
//                w.Write(offset);
//                offset += png.Length;
//            }

//            foreach (var (_, png) in images)
//            {
//                w.Write(png);
//            }

//            return ms.ToArray();
//        }

//        // Drag & Drop handlers:
//        private void Window_DragOver(object sender, DragEventArgs e)
//        {
//            if (e.Data.GetDataPresent(DataFormats.FileDrop))
//                e.Effects = DragDropEffects.Copy;
//            else
//                e.Effects = DragDropEffects.None;
//            e.Handled = true;
//        }

//        private void Window_Drop(object sender, DragEventArgs e)
//        {
//            if (e.Data.GetDataPresent(DataFormats.FileDrop))
//            {
//                var files = ((string[])e.Data.GetData(DataFormats.FileDrop)).ToList();

//                if (files.Count > _maxFiles)
//                {
//                    MessageBox.Show($"You dropped {files.Count} files. The maximum is {_maxFiles}. Only the first {_maxFiles} will be used.",
//                                    "Too many files", MessageBoxButton.OK, MessageBoxImage.Warning);
//                    files = files.Take(_maxFiles).ToList();
//                }

//                _selectedFilePaths = files;
//                lstSelectedFiles.ItemsSource = null;
//                lstSelectedFiles.ItemsSource = _selectedFilePaths;

//                btnCreateIcon.IsEnabled = _selectedFilePaths.Count != 0;
//                txtStatus.Text = btnCreateIcon.IsEnabled
//                    ? $"Selected {_selectedFilePaths.Count} file(s)."
//                    : "No files selected.";

//                // Auto-hide Open Folder & Preview when user drops new images
//                HideOpenFolderAndPreview();
//            }
//        }

//        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
//        {
//            try
//            {
//                if (!string.IsNullOrEmpty(_lastSavedIconPath))
//                {
//                    string folder = Path.GetDirectoryName(_lastSavedIconPath)!;

//                    if (Directory.Exists(folder))
//                    {
//                        // Open the folder and select the saved icon
//                        Process.Start("explorer.exe", $"/select,\"{_lastSavedIconPath}\"");
//                    }
//                    else
//                    {
//                        MessageBox.Show("The folder no longer exists.",
//                                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
//                    }
//                }
//                else
//                {
//                    MessageBox.Show("No saved icon path found.",
//                                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
//                }
//            }
//            catch (Exception ex)
//            {
//                MessageBox.Show($"Unable to open folder: {ex.Message}",
//                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
//            }
//        }

//        private void BtnPreview_Click(object sender, RoutedEventArgs e)
//        {
//            if (string.IsNullOrEmpty(_lastSavedIconPath))
//            {
//                MessageBox.Show("No saved icon to preview.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
//                return;
//            }

//            try
//            {
//                var preview = new IconPreviewWindow(_lastSavedIconPath);
//                preview.Owner = this;
//                preview.ShowDialog();
//            }
//            catch (Exception ex)
//            {
//                MessageBox.Show($"Unable to open preview: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
//            }
//        }

//        // Helper: hide/disable open-folder & preview buttons and clear tooltip
//        private void HideOpenFolderAndPreview()
//        {
//            btnOpenFolder.Visibility = Visibility.Collapsed;
//            btnOpenFolder.IsEnabled = false;
//            btnOpenFolder.ToolTip = null;

//            btnPreview.Visibility = Visibility.Collapsed;
//            btnPreview.IsEnabled = false;
//            btnPreview.ToolTip = null;
//        }
//    }
//}


using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
        // keep up to 9 files
        private List<string> _selectedFilePaths = new List<string>();
        private string? _lastSavedIconPath;
        private byte[]? _lastSavedIconBytes; // hold ico bytes for preview-before-save
        private const int _maxFiles = 9;
        private static readonly int[] _requiredSizes = new[] { 16, 24, 32, 48, 64, 72, 96, 128, 256 };

        // persistent file location for last saved path
        private static readonly string _appSettingsDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImageToIcon");
        private static readonly string _lastPathFile = Path.Combine(_appSettingsDir, "lastpath.txt");

        public MainWindow()
        {
            InitializeComponent();

            // initialize UI state
            HideOpenFolderAndPreview();

            // Try to load persisted last-saved path
            LoadPersistedLastPath();
        }

        private void LoadPersistedLastPath()
        {
            try
            {
                if (File.Exists(_lastPathFile))
                {
                    var path = File.ReadAllText(_lastPathFile).Trim();
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        _lastSavedIconPath = path;
                        // show buttons because saved file still exists on disk
                        btnOpenFolder.Visibility = Visibility.Visible;
                        btnOpenFolder.IsEnabled = true;
                        btnOpenFolder.ToolTip = _lastSavedIconPath;

                        btnPreview.Visibility = Visibility.Visible;
                        btnPreview.IsEnabled = true;
                        btnPreview.ToolTip = "Preview the saved icon";
                    }
                }
            }
            catch
            {
                // ignore errors loading persisted path (do not block UI)
            }
        }

        private void PersistLastPath(string path)
        {
            try
            {
                Directory.CreateDirectory(_appSettingsDir);
                File.WriteAllText(_lastPathFile, path);
            }
            catch
            {
                // ignore persistence failures (non-fatal)
            }
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
                var chosen = dlg.FileNames.ToList();

                if (chosen.Count > _maxFiles)
                {
                    MessageBox.Show($"You selected {chosen.Count} files. The maximum is {_maxFiles}. Only the first {_maxFiles} will be used.",
                                    "Too many files", MessageBoxButton.OK, MessageBoxImage.Warning);
                    chosen = chosen.Take(_maxFiles).ToList();
                }

                _selectedFilePaths = chosen;
                lstSelectedFiles.ItemsSource = null;
                lstSelectedFiles.ItemsSource = _selectedFilePaths;

                btnCreateIcon.IsEnabled = _selectedFilePaths.Count != 0;
                txtStatus.Text = btnCreateIcon.IsEnabled
                    ? $"Selected {_selectedFilePaths.Count} file(s)."
                    : "No files selected.";

                // Auto-hide Open Folder & Preview when user selects new images (do not delete persisted path)
                HideOpenFolderAndPreview(keepPersistedPath: true);
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
                // Generate ico bytes on background thread
                byte[] icoBytes = await Task.Run(() => CreateIconBytes(_selectedFilePaths, progress));

                // Keep bytes in memory for preview-before-save
                _lastSavedIconBytes = icoBytes;

                // Enable preview immediately (even before saving)
                btnPreview.Visibility = Visibility.Visible;
                btnPreview.IsEnabled = true;
                btnPreview.ToolTip = "Preview the generated (unsaved) icon";

                // Prompt for save (if user wants)
                var saveDlg = new SaveFileDialog
                {
                    Filter = "Icon Files (*.ico)|*.ico",
                    DefaultExt = "ico",
                    FileName = "app.ico"
                };

                if (saveDlg.ShowDialog() == true)
                {
                    await File.WriteAllBytesAsync(saveDlg.FileName, icoBytes);

                    _lastSavedIconPath = saveDlg.FileName;

                    // Show and enable Open Folder; set tooltip to saved path
                    btnOpenFolder.Visibility = Visibility.Visible;
                    btnOpenFolder.IsEnabled = true;
                    btnOpenFolder.ToolTip = _lastSavedIconPath;

                    // Update preview tooltip (now points to saved icon as well)
                    btnPreview.ToolTip = "Preview the saved icon";

                    // Persist path across restarts
                    PersistLastPath(_lastSavedIconPath);

                    MessageBox.Show("Icon created successfully!",
                                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    txtStatus.Text = $"Icon saved to {_lastSavedIconPath}";
                }
                else
                {
                    txtStatus.Text = "Icon created (not saved) — you can preview before saving.";
                }
            }
            catch (Exception ex)
            {
                // Show a short, user-friendly message.
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
                // Report non-fatal errors to user via progress
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
                var files = ((string[])e.Data.GetData(DataFormats.FileDrop)).ToList();

                if (files.Count > _maxFiles)
                {
                    MessageBox.Show($"You dropped {files.Count} files. The maximum is {_maxFiles}. Only the first {_maxFiles} will be used.",
                                    "Too many files", MessageBoxButton.OK, MessageBoxImage.Warning);
                    files = files.Take(_maxFiles).ToList();
                }

                _selectedFilePaths = files;
                lstSelectedFiles.ItemsSource = null;
                lstSelectedFiles.ItemsSource = _selectedFilePaths;

                btnCreateIcon.IsEnabled = _selectedFilePaths.Count != 0;
                txtStatus.Text = btnCreateIcon.IsEnabled
                    ? $"Selected {_selectedFilePaths.Count} file(s)."
                    : "No files selected.";

                // Auto-hide Open Folder & Preview when user drops new images (keep persisted path intact)
                HideOpenFolderAndPreview(keepPersistedPath: true);
            }
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(_lastSavedIconPath) && File.Exists(_lastSavedIconPath))
                {
                    string folder = Path.GetDirectoryName(_lastSavedIconPath)!;

                    if (Directory.Exists(folder))
                    {
                        // Open the folder and select the saved icon
                        Process.Start("explorer.exe", $"/select,\"{_lastSavedIconPath}\"");
                    }
                    else
                    {
                        MessageBox.Show("The folder no longer exists.",
                                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    MessageBox.Show("No saved icon path found on disk.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to open folder: {ex.Message}",
                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnPreview_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_lastSavedIconBytes != null)
                {
                    // preview unsaved/generated bytes
                    var preview = new IconPreviewWindow(_lastSavedIconBytes);
                    preview.Owner = this;
                    preview.ShowDialog();
                    return;
                }

                if (!string.IsNullOrEmpty(_lastSavedIconPath) && File.Exists(_lastSavedIconPath))
                {
                    var preview = new IconPreviewWindow(_lastSavedIconPath);
                    preview.Owner = this;
                    preview.ShowDialog();
                    return;
                }

                MessageBox.Show("No saved or generated icon to preview.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to open preview: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Helper: hide/disable open-folder & preview buttons and clear tooltip
        // keepPersistedPath==true will hide UI but not erase persisted last path on disk
        private void HideOpenFolderAndPreview(bool keepPersistedPath = false)
        {
            btnOpenFolder.Visibility = Visibility.Collapsed;
            btnOpenFolder.IsEnabled = false;
            btnOpenFolder.ToolTip = null;

            btnPreview.Visibility = Visibility.Collapsed;
            btnPreview.IsEnabled = false;
            btnPreview.ToolTip = null;

            // clear in-memory generated bytes so preview will prefer saved file next time (if any)
            _lastSavedIconBytes = null;

            if (!keepPersistedPath)
            {
                _lastSavedIconPath = null;
            }
        }
    }
}


using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QSLauncher.App.Views;

public sealed class FileBrowserPlace
{
    public FileBrowserPlace(string name, string path)
    {
        Name = name;
        Path = path;
    }

    public string Name { get; }

    public string Path { get; }
}

public sealed class FileBrowserEntry
{
    public FileBrowserEntry(string name, string fullPath, bool isDirectory, string detail)
    {
        Name = name;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        Detail = detail;
    }

    public string Name { get; }

    public string FullPath { get; }

    public bool IsDirectory { get; }

    public string Detail { get; }

    public string Glyph => IsDirectory ? "D" : "F";
}

public partial class FileBrowserWindow : Window
{
    private readonly ObservableCollection<FileBrowserPlace> _places = [];
    private readonly ObservableCollection<FileBrowserEntry> _entries = [];
    private readonly bool _pickFolder;
    private readonly string[] _patterns;
    private string _currentDirectory;
    private bool _suppressSelection;

    public FileBrowserWindow(
        string title,
        bool pickFolder,
        string? initialPath,
        string filter)
    {
        InitializeComponent();
        _pickFolder = pickFolder;
        _patterns = ParseFilter(filter);
        _currentDirectory = ResolveInitialDirectory(initialPath);

        TitleText.Text = title;
        Title = title;
        PrimaryButton.Content = pickFolder ? "选择此文件夹" : "选择文件";
        NameLabel.Visibility = pickFolder ? Visibility.Collapsed : Visibility.Visible;
        FileNameBox.Visibility = pickFolder ? Visibility.Collapsed : Visibility.Visible;
        FolderHint.Visibility = pickFolder ? Visibility.Visible : Visibility.Collapsed;

        PlaceList.ItemsSource = _places;
        EntryList.ItemsSource = _entries;
        BuildPlaces();
        LoadDirectory(_currentDirectory);
    }

    public string? SelectedPath { get; private set; }

    private void BuildPlaces()
    {
        AddPlace("用户目录", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        AddPlace("桌面", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        AddPlace("下载", Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
        AddPlace("文档", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        AddPlace("图片", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.IsReady)
                {
                    _places.Add(new FileBrowserPlace(
                        string.IsNullOrWhiteSpace(drive.VolumeLabel)
                            ? drive.Name
                            : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})",
                        drive.RootDirectory.FullName));
                }
            }
            catch (IOException)
            {
                // A drive can disappear between enumeration and inspection.
            }
        }
    }

    private void AddPlace(string name, string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            _places.Add(new FileBrowserPlace(name, path));
        }
    }

    private void LoadDirectory(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                fullPath = Path.GetDirectoryName(fullPath) ?? fullPath;
            }

            if (!Directory.Exists(fullPath))
            {
                ShowError("目录不存在");
                return;
            }

            _currentDirectory = fullPath;
            PathBox.Text = fullPath;
            ErrorTextReset();
            ClearImagePreview();
            _entries.Clear();

            var directories = new DirectoryInfo(fullPath)
                .EnumerateDirectories("*", new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = false
                })
                .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            foreach (var directory in directories)
            {
                _entries.Add(new FileBrowserEntry(directory.Name, directory.FullName, true, "文件夹"));
            }

            if (!_pickFolder)
            {
                var files = new DirectoryInfo(fullPath)
                    .EnumerateFiles("*", new EnumerationOptions
                    {
                        IgnoreInaccessible = true,
                        RecurseSubdirectories = false
                    })
                    .Where(f => MatchesFilter(f.Name))
                    .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Take(1200)
                    .ToList();

                foreach (var file in files)
                {
                    _entries.Add(new FileBrowserEntry(
                        file.Name,
                        file.FullName,
                        false,
                        FormatSize(file.Length)));
                }
            }

            TitleText.Text = _pickFolder
                ? $"选择文件夹 · {_entries.Count} 项"
                : $"选择文件 · {_entries.Count} 项";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException)
        {
            ShowError("无法打开此位置");
        }
    }

    private bool MatchesFilter(string fileName)
    {
        if (_patterns.Length == 0 || _patterns.Contains("*") || _patterns.Contains("*.*"))
        {
            return true;
        }

        var extension = Path.GetExtension(fileName);
        return _patterns.Any(pattern =>
        {
            var trimmed = pattern.Trim();
            if (trimmed.StartsWith("*.", StringComparison.Ordinal))
            {
                return extension.Equals(trimmed[1..], StringComparison.OrdinalIgnoreCase);
            }

            return fileName.Equals(trimmed, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static string[] ParseFilter(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return ["*"];
        }

        var parts = filter.Split('|');
        for (var index = 0; index + 1 < parts.Length; index += 2)
        {
            var patterns = parts[index + 1]
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (patterns.Length > 0)
            {
                return patterns;
            }
        }

        return ["*"];
    }

    private static string ResolveInitialDirectory(string? initialPath)
    {
        var candidate = initialPath;
        while (!string.IsNullOrWhiteSpace(candidate))
        {
            try
            {
                if (Directory.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }

                if (File.Exists(candidate))
                {
                    return Path.GetDirectoryName(Path.GetFullPath(candidate))
                           ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                }

                candidate = Path.GetDirectoryName(candidate);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                break;
            }
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024L * 1024L * 1024L)
        {
            return $"{bytes / (1024d * 1024 * 1024):0.0} GB";
        }

        if (bytes >= 1024L * 1024L)
        {
            return $"{bytes / (1024d * 1024):0.0} MB";
        }

        if (bytes >= 1024)
        {
            return $"{bytes / 1024d:0} KB";
        }

        return $"{bytes} B";
    }

    private void ShowError(string message)
    {
        NameLabel.Text = message;
        NameLabel.Foreground = (Brush)FindResource("DangerBrush");
        NameLabel.Visibility = Visibility.Visible;
    }

    private void ErrorTextReset()
    {
        NameLabel.Text = "文件名";
        NameLabel.Foreground = (Brush)FindResource("TextTertiaryBrush");
        NameLabel.Visibility = _pickFolder ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnPlaceSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection || PlaceList.SelectedItem is not FileBrowserPlace place)
        {
            return;
        }

        LoadDirectory(place.Path);
        _suppressSelection = true;
        PlaceList.SelectedItem = null;
        _suppressSelection = false;
    }

    private void OnEntrySelected(object sender, SelectionChangedEventArgs e)
    {
        if (EntryList.SelectedItem is FileBrowserEntry entry)
        {
            if (!entry.IsDirectory && !_pickFolder)
            {
                FileNameBox.Text = entry.Name;
            }

            UpdateImagePreview(entry);
        }
        else
        {
            ClearImagePreview();
        }
    }

    private void UpdateImagePreview(FileBrowserEntry entry)
    {
        ClearImagePreview();
        if (entry.IsDirectory || !IsPreviewImage(entry.FullPath))
        {
            return;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(entry.FullPath, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 420;
            image.EndInit();
            PreviewImage.Source = image;
            PreviewHint.Visibility = Visibility.Collapsed;
            PreviewCaption.Text = $"{entry.Name}\n{image.PixelWidth} × {image.PixelHeight}";
        }
        catch (Exception ex)
        {
            PreviewCaption.Text = $"无法预览：{ex.Message}";
        }
    }

    private void ClearImagePreview()
    {
        PreviewImage.Source = null;
        PreviewHint.Visibility = Visibility.Visible;
        PreviewCaption.Text = string.Empty;
    }

    private static bool IsPreviewImage(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif"
            or ".tif" or ".tiff" or ".ico";
    }

    private void OnEntryDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (EntryList.SelectedItem is not FileBrowserEntry entry)
        {
            return;
        }

        if (entry.IsDirectory)
        {
            LoadDirectory(entry.FullPath);
            return;
        }

        if (!_pickFolder)
        {
            SelectedPath = entry.FullPath;
            DialogResult = true;
        }
    }

    private void OnUpClick(object sender, RoutedEventArgs e)
    {
        var parent = Directory.GetParent(_currentDirectory);
        if (parent is not null)
        {
            LoadDirectory(parent.FullName);
        }
    }

    private void OnGoClick(object sender, RoutedEventArgs e)
    {
        LoadDirectory(PathBox.Text);
    }

    private void OnPathKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            LoadDirectory(PathBox.Text);
            e.Handled = true;
        }
    }

    private void OnFileNameChanged(object sender, TextChangedEventArgs e)
    {
        if (_pickFolder || string.IsNullOrWhiteSpace(FileNameBox.Text))
        {
            return;
        }

        try
        {
            var name = FileNameBox.Text.Trim();
            var candidate = Path.IsPathRooted(name)
                ? Path.GetFullPath(name)
                : Path.GetFullPath(Path.Combine(_currentDirectory, name));
            if (File.Exists(candidate) && IsPreviewImage(candidate))
            {
                UpdateImagePreview(new FileBrowserEntry(
                    Path.GetFileName(candidate), candidate, false, string.Empty));
            }
            else
            {
                ClearImagePreview();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or NotSupportedException
                                       or PathTooLongException)
        {
            ClearImagePreview();
        }
    }

    private void OnFileNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Confirm();
            e.Handled = true;
        }
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        Confirm();
    }

    private void Confirm()
    {
        if (_pickFolder)
        {
            if (EntryList.SelectedItem is FileBrowserEntry { IsDirectory: true } folder)
            {
                SelectedPath = folder.FullPath;
            }
            else
            {
                SelectedPath = _currentDirectory;
            }

            DialogResult = true;
            return;
        }

        var name = FileNameBox.Text.Trim();
        if (name.Length == 0)
        {
            ShowError("请输入文件名");
            return;
        }

        string candidate;
        try
        {
            candidate = Path.IsPathRooted(name) ? Path.GetFullPath(name) : Path.GetFullPath(Path.Combine(_currentDirectory, name));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            ShowError("路径无效");
            return;
        }

        if (Directory.Exists(candidate))
        {
            LoadDirectory(candidate);
            FileNameBox.Clear();
            return;
        }

        if (!File.Exists(candidate))
        {
            ShowError("文件不存在");
            return;
        }

        SelectedPath = candidate;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The pointer was released before the drag started.
        }
    }

    public static string? PickFile(string title, string filter, string? initialPath = null)
        => Show(title, false, initialPath, filter);

    public static string? PickFolder(string title, string? initialPath = null)
        => Show(title, true, initialPath, "*");

    private static string? Show(string title, bool pickFolder, string? initialPath, string filter)
    {
        var application = Application.Current;
        if (application is null)
        {
            return null;
        }

        if (!application.Dispatcher.CheckAccess())
        {
            return application.Dispatcher.Invoke(() => Show(title, pickFolder, initialPath, filter));
        }

        var window = new FileBrowserWindow(title, pickFolder, initialPath, filter);
        var owner = application.MainWindow;
        if (owner is not null && owner.IsLoaded)
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return window.ShowDialog() == true ? window.SelectedPath : null;
    }
}

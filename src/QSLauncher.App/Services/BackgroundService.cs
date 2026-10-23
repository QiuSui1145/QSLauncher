using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QSLauncher.Core.Models;
using QSLauncher.Core.Services;

namespace QSLauncher.App.Services;

public sealed class BackgroundService : ViewModels.ObservableObject
{
    private readonly PortablePaths _paths;
    private readonly SettingsService _settings;
    private ImageSource? _imageSource;
    private ImageSource? _logoSource;
    private Uri? _videoUri;
    private bool _isImage;
    private bool _isVideo;
    private double _dimOpacity;
    private double _blurRadius;
    private Stretch _stretch = Stretch.UniformToFill;
    private bool _muted = true;
    private bool _loop = true;

    public BackgroundService(PortablePaths paths, SettingsService settings)
    {
        _paths = paths;
        _settings = settings;
        _settings.Changed += (_, _) =>
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is not null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(Refresh);
            }
            else
            {
                Refresh();
            }
        };
    }

    public ImageSource? ImageSource
    {
        get => _imageSource;
        private set => SetProperty(ref _imageSource, value);
    }

    public ImageSource? LogoSource
    {
        get => _logoSource;
        private set
        {
            if (SetProperty(ref _logoSource, value))
            {
                OnPropertyChanged(nameof(HasCustomLogo));
            }
        }
    }

    public bool HasCustomLogo => LogoSource is not null;

    public Uri? VideoUri
    {
        get => _videoUri;
        private set => SetProperty(ref _videoUri, value);
    }

    public bool IsImage
    {
        get => _isImage;
        private set => SetProperty(ref _isImage, value);
    }

    public bool IsVideo
    {
        get => _isVideo;
        private set => SetProperty(ref _isVideo, value);
    }

    public double DimOpacity
    {
        get => _dimOpacity;
        private set => SetProperty(ref _dimOpacity, value);
    }

    public double BlurRadius
    {
        get => _blurRadius;
        private set => SetProperty(ref _blurRadius, value);
    }

    public Stretch Stretch
    {
        get => _stretch;
        private set => SetProperty(ref _stretch, value);
    }

    public bool Muted
    {
        get => _muted;
        private set => SetProperty(ref _muted, value);
    }

    public bool Loop
    {
        get => _loop;
        private set => SetProperty(ref _loop, value);
    }

    public bool HasCustomBackground => IsImage || IsVideo;

    public void Refresh()
    {
        var background = _settings.Current.Appearance.Background;
        IsImage = false;
        IsVideo = false;
        ImageSource = null;
        VideoUri = null;
        LogoSource = LoadLogo();

        DimOpacity = background.Dim;
        Muted = background.Muted;
        Loop = background.Loop;
        Stretch = background.Fit switch
        {
            BackgroundFit.Fit => Stretch.Uniform,
            BackgroundFit.Fill => Stretch.UniformToFill,
            _ => Stretch.UniformToFill
        };

        // Only the explicit background blur affects the wallpaper. Glass blur is
        // applied to panel surfaces, never to the full-resolution media.
        BlurRadius = Math.Clamp(background.Blur, 0, 40);

        if (background.Mode == BackgroundMode.None || string.IsNullOrWhiteSpace(background.Path))
        {
            OnPropertyChanged(nameof(HasCustomBackground));
            return;
        }

        string fullPath;
        try
        {
            fullPath = _paths.ResolveDataPath(background.Path);
        }
        catch
        {
            OnPropertyChanged(nameof(HasCustomBackground));
            return;
        }

        if (!File.Exists(fullPath))
        {
            AppLog.Warn($"背景文件不存在: {fullPath}");
            OnPropertyChanged(nameof(HasCustomBackground));
            return;
        }

        if (background.Mode == BackgroundMode.Image)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.UriSource = new Uri(fullPath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                ImageSource = bitmap;
                IsImage = true;
            }
            catch (Exception ex)
            {
                AppLog.Error("加载背景图片失败", ex);
            }
        }
        else
        {
            VideoUri = new Uri(fullPath, UriKind.Absolute);
            IsVideo = true;
        }

        OnPropertyChanged(nameof(HasCustomBackground));
    }

    private ImageSource? LoadLogo()
    {
        var relative = _settings.Current.Appearance.LogoPath;
        if (string.IsNullOrWhiteSpace(relative))
        {
            return null;
        }

        try
        {
            var fullPath = _paths.ResolveDataPath(relative);
            if (!File.Exists(fullPath))
            {
                AppLog.Warn($"Logo 文件不存在: {fullPath}");
                return null;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = new Uri(fullPath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            AppLog.Error("加载自定义 Logo 失败", ex);
            return null;
        }
    }
}

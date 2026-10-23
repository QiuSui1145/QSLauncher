using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace QSLauncher.App.Controls;

/// <summary>
/// Renders a muted video with DrawingContext.DrawVideo instead of MediaElement.
/// This keeps the video inside the WPF visual tree, so it can sit behind glass
/// panels without the HWND/airspace problems of MediaElement.
/// </summary>
public sealed class VideoSurface : FrameworkElement, IDisposable
{
    private MediaPlayer? _player;
    private bool _isLoaded;

    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(Uri), typeof(VideoSurface),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSourceChanged));

    public static readonly DependencyProperty IsMutedProperty = DependencyProperty.Register(
        nameof(IsMuted), typeof(bool), typeof(VideoSurface),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender, OnMutedChanged));

    public static readonly DependencyProperty LoopProperty = DependencyProperty.Register(
        nameof(Loop), typeof(bool), typeof(VideoSurface),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StretchProperty = DependencyProperty.Register(
        nameof(Stretch), typeof(Stretch), typeof(VideoSurface),
        new FrameworkPropertyMetadata(Stretch.UniformToFill, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BlurRadiusProperty = DependencyProperty.Register(
        nameof(BlurRadius), typeof(double), typeof(VideoSurface),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, OnBlurRadiusChanged));

    public VideoSurface()
    {
        ClipToBounds = true;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    public Uri? Source
    {
        get => (Uri?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public bool IsMuted
    {
        get => (bool)GetValue(IsMutedProperty);
        set => SetValue(IsMutedProperty, value);
    }

    public bool Loop
    {
        get => (bool)GetValue(LoopProperty);
        set => SetValue(LoopProperty, value);
    }

    public Stretch Stretch
    {
        get => (Stretch)GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    public double BlurRadius
    {
        get => (double)GetValue(BlurRadiusProperty);
        set => SetValue(BlurRadiusProperty, value);
    }

    public event EventHandler? MediaOpened;

    public event EventHandler? MediaFailed;

    public void Pause()
    {
        _player?.Pause();
    }

    public void Resume()
    {
        if (_isLoaded)
        {
            _player?.Play();
        }
    }

    public void Dispose()
    {
        ClosePlayer();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (_player is null ||
            _player.NaturalVideoWidth <= 0 ||
            _player.NaturalVideoHeight <= 0 ||
            ActualWidth <= 0 ||
            ActualHeight <= 0)
        {
            return;
        }

        var viewport = new Rect(0, 0, ActualWidth, ActualHeight);
        var destination = CalculateDestination(
            viewport,
            _player.NaturalVideoWidth,
            _player.NaturalVideoHeight,
            Stretch);

        drawingContext.PushClip(new RectangleGeometry(viewport));
        drawingContext.DrawVideo(_player, destination);
        drawingContext.Pop();
    }

    private static Rect CalculateDestination(Rect viewport, double sourceWidth, double sourceHeight, Stretch stretch)
    {
        var scale = stretch switch
        {
            Stretch.None => 1d,
            Stretch.Fill => double.NaN,
            Stretch.Uniform => Math.Min(viewport.Width / sourceWidth, viewport.Height / sourceHeight),
            _ => Math.Max(viewport.Width / sourceWidth, viewport.Height / sourceHeight)
        };

        if (double.IsNaN(scale))
        {
            return viewport;
        }

        var width = sourceWidth * scale;
        var height = sourceHeight * scale;
        return new Rect(
            (viewport.Width - width) / 2,
            (viewport.Height - height) / 2,
            width,
            height);
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((VideoSurface)d).OpenSource((Uri?)e.NewValue);

    private static void OnMutedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var surface = (VideoSurface)d;
        if (surface._player is not null)
        {
            surface._player.IsMuted = (bool)e.NewValue;
        }
    }

    private static void OnBlurRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var radius = (double)e.NewValue;
        ((VideoSurface)d).Effect = radius > 0.1
            ? new BlurEffect { Radius = radius, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance }
            : null;
    }

    private void OpenSource(Uri? uri)
    {
        ClosePlayer();
        if (uri is null)
        {
            InvalidateVisual();
            return;
        }

        try
        {
            _player = new MediaPlayer
            {
                Volume = 0,
                IsMuted = IsMuted,
                ScrubbingEnabled = false
            };
            _player.MediaOpened += (_, _) =>
            {
                MediaOpened?.Invoke(this, EventArgs.Empty);
                InvalidateVisual();
                if (_isLoaded)
                {
                    _player?.Play();
                }
            };
            _player.MediaEnded += (_, _) =>
            {
                if (Loop && _player is not null)
                {
                    _player.Position = TimeSpan.Zero;
                    _player.Play();
                }
            };
            _player.MediaFailed += (_, args) =>
            {
                MediaFailed?.Invoke(this, EventArgs.Empty);
                InvalidateVisual();
            };
            _player.Open(uri);
            if (_isLoaded)
            {
                _player.Play();
            }
        }
        catch
        {
            MediaFailed?.Invoke(this, EventArgs.Empty);
            ClosePlayer();
        }

        InvalidateVisual();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        _player?.Play();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        _player?.Pause();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            _player?.Play();
        }
        else
        {
            _player?.Pause();
        }
    }

    private void ClosePlayer()
    {
        if (_player is null)
        {
            return;
        }

        try
        {
            _player.Stop();
            _player.Close();
        }
        catch
        {
            // Ignore media shutdown races.
        }

        _player = null;
    }
}

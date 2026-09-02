using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Barakhloskop.Infrastructure;
using Barakhloskop.Models;
using Barakhloskop.ViewModels;

namespace Barakhloskop.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private readonly DispatcherTimer _tick;
    private readonly List<Border> _bars = new();
    private readonly Random _random = Random.Shared;

    private bool _seekDragging;
    private bool _isPlaying;
    private bool _suppressSelection;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        _vm.PreviewRequested += OnPreviewRequested;

        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _tick.Tick += OnTick;

        BuildEqualizer();
    }

    // ================= Заголовок окна =================

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _tick.Stop();
        StopMedia();
        _vm.Shutdown();
    }

    // ================= Предпросмотр =================

    private void OnPreviewRequested(MediaFile? file)
    {
        StopMedia();
        PreviewImage.Source = null;

        if (file is null) return;

        switch (file.Kind)
        {
            case MediaKind.Image:
                LoadImage(file);
                break;

            case MediaKind.Audio:
            case MediaKind.Video:
                LoadMedia(file);
                break;
        }
    }

    private void LoadImage(MediaFile file)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            // Крупные фото уменьшаем при декодировании — иначе память улетает.
            bitmap.DecodePixelWidth = 1600;
            bitmap.UriSource = new Uri(file.Path);
            bitmap.EndInit();
            bitmap.Freeze();

            PreviewImage.Source = bitmap;
            FadeIn(PreviewImage);
        }
        catch (Exception)
        {
            // Битый файл или неизвестный кодек — показываем плашку «не поддерживается».
            _vm.PreviewFailed = true;
        }
    }

    private void LoadMedia(MediaFile file)
    {
        try
        {
            PreviewMedia.Source = new Uri(file.Path);
            PreviewMedia.Position = TimeSpan.Zero;

            if (_vm.AutoPlay)
            {
                PreviewMedia.Play();
                _isPlaying = true;
            }
            else
            {
                PreviewMedia.Pause();
                _isPlaying = false;
            }

            UpdatePlayGlyph();
            _tick.Start();
        }
        catch (Exception)
        {
            _vm.PreviewFailed = true;
        }
    }

    private void StopMedia()
    {
        _tick.Stop();
        _isPlaying = false;

        try
        {
            PreviewMedia.Stop();
            PreviewMedia.Source = null;
        }
        catch (Exception)
        {
            // MediaElement иногда капризничает при быстрой смене источника.
        }

        Seek.Value = 0;
        Seek.Maximum = 1;
        TimeLabel.Text = "0:00 / 0:00";
        UpdatePlayGlyph();
    }

    private static void FadeIn(UIElement element)
    {
        var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        element.BeginAnimation(OpacityProperty, animation);
    }

    // ================= Плеер =================

    private void OnMediaOpened(object sender, RoutedEventArgs e)
    {
        if (PreviewMedia.NaturalDuration.HasTimeSpan)
        {
            var total = PreviewMedia.NaturalDuration.TimeSpan;
            Seek.Maximum = Math.Max(0.1, total.TotalSeconds);
            TimeLabel.Text = $"0:00 / {Humanize.Duration(total)}";
        }

        FadeIn(PreviewMedia);
        UpdatePlayGlyph();
    }

    private void OnMediaFailed(object? sender, ExceptionRoutedEventArgs e)
    {
        _tick.Stop();
        _isPlaying = false;
        _vm.PreviewFailed = true;
        UpdatePlayGlyph();
    }

    private void OnMediaEnded(object sender, RoutedEventArgs e)
    {
        PreviewMedia.Position = TimeSpan.Zero;
        PreviewMedia.Pause();
        _isPlaying = false;
        UpdatePlayGlyph();
    }

    private void OnPlayPause(object sender, RoutedEventArgs e)
    {
        if (PreviewMedia.Source is null) return;

        if (_isPlaying)
        {
            PreviewMedia.Pause();
            _isPlaying = false;
        }
        else
        {
            PreviewMedia.Play();
            _isPlaying = true;
            _tick.Start();
        }

        UpdatePlayGlyph();
    }

    private void OnStop(object sender, RoutedEventArgs e)
    {
        if (PreviewMedia.Source is null) return;

        PreviewMedia.Stop();
        PreviewMedia.Position = TimeSpan.Zero;
        _isPlaying = false;
        Seek.Value = 0;
        UpdatePlayGlyph();
    }

    private void UpdatePlayGlyph() => PlayButton.Content = _isPlaying ? "\uE769" : "\uE768";

    private void OnSeekPress(object sender, MouseButtonEventArgs e) => _seekDragging = true;

    private void OnSeekRelease(object sender, MouseButtonEventArgs e)
    {
        _seekDragging = false;
        if (PreviewMedia.Source is null) return;

        try
        {
            PreviewMedia.Position = TimeSpan.FromSeconds(Seek.Value);
        }
        catch (Exception)
        {
            // Некоторые контейнеры не поддерживают перемотку.
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (PreviewMedia.Source is null)
        {
            _tick.Stop();
            return;
        }

        if (!_seekDragging && PreviewMedia.NaturalDuration.HasTimeSpan)
        {
            var total = PreviewMedia.NaturalDuration.TimeSpan;
            Seek.Value = PreviewMedia.Position.TotalSeconds;
            TimeLabel.Text = $"{Humanize.Duration(PreviewMedia.Position)} / {Humanize.Duration(total)}";
        }

        if (_vm.IsAudioPreview) AnimateEqualizer();
    }

    // ================= Эквалайзер для аудио =================

    private void BuildEqualizer()
    {
        var brushes = new[] { "B.Lime", "B.Cyan", "B.Magenta", "B.Violet", "B.Amber" };

        for (int i = 0; i < 22; i++)
        {
            var bar = new Border
            {
                Width = 5,
                Height = 6,
                Margin = new Thickness(2, 0, 2, 0),
                CornerRadius = new CornerRadius(3),
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = TryFindResource(brushes[i % brushes.Length]) as Brush ?? Brushes.Gray,
                Opacity = 0.85
            };

            _bars.Add(bar);
            Equalizer.Children.Add(bar);
        }
    }

    private void AnimateEqualizer()
    {
        // Честного FFT у MediaElement нет, так что рисуем правдоподобный шум.
        foreach (var bar in _bars)
        {
            double target = _isPlaying ? 6 + _random.NextDouble() * 40 : 6;
            var animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(210))
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            bar.BeginAnimation(HeightProperty, animation);
        }
    }

    // ================= Список находок =================

    private void OnFindingSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection) return;
        if (sender is not ListBox list || list.SelectedItem is not MediaFile file) return;

        _suppressSelection = true;
        _vm.ShowFileCommand.Execute(file);
        list.SelectedItem = null;
        _suppressSelection = false;
    }
}

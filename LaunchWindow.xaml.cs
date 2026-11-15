using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace RxNeL;

/// <summary>
/// 类似 Bloxstrap bootstrapper 的小进度窗：启动流程期间显示步骤、百分比与进度条。
/// </summary>
public partial class LaunchWindow : Window
{
    private const double TrackWidth = 396;

    private double _progress;

    public LaunchWindow()
    {
        InitializeComponent();
    }

    public void SetStatus(string text, double progress, string? detail = null)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetStatus(text, progress, detail));
            return;
        }

        StatusText.Text = text;

        if (detail is not null)
            DetailText.Text = detail;

        _progress = Math.Clamp(progress, 0, 1);
        ProgressFill.Width = Math.Max(_progress * TrackWidth, 4);
    }

    /// <summary>不确定进度：让进度条来回爬（Bloxstrap 的连接/检查阶段就是这种表现）。</summary>
    public void SetIndeterminate(string text, string? detail = null)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetIndeterminate(text, detail));
            return;
        }

        StatusText.Text = text;

        if (detail is not null)
            DetailText.Text = detail;

        var animation = new DoubleAnimation(6, TrackWidth - 60, TimeSpan.FromSeconds(0.9))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };

        ProgressFill.Width = 60;
        ProgressFill.BeginAnimation(WidthProperty, animation);
    }

    public void StopIndeterminate()
    {
        ProgressFill.BeginAnimation(WidthProperty, null);
        ProgressFill.Width = Math.Max(_progress * TrackWidth, 4);
    }

    public void Fail(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => Fail(message));
            return;
        }

        StopIndeterminate();
        StatusText.Text = message;
        StatusText.Foreground = (Brush)FindResource("DangerBrush");
        ProgressFill.Background = (Brush)FindResource("DangerBrush");
        ProgressFill.Width = TrackWidth;
    }
}

using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace RxNeL;

/// <summary>把主窗口逐页渲染成 PNG（浅色 / 深色两套），用于回归检查。</summary>
public static class UiTest
{
    private static readonly string[] Sections = { "general", "engine", "content", "appearance", "log" };

    public static void Run(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        Logger.Info($"界面自检开始，输出目录: {outputDir}");

        var window = new MainWindow();
        window.Show();
        window.UpdateLayout();
        window.HideLoadingForTest();
        Pump();

        foreach (var mode in new[] { ThemeMode.Dark, ThemeMode.Light })
        {
            ThemeManager.SetMode(mode);
            window.UpdateLayout();
            Pump();

            for (var i = 0; i < window.PageCount; i++)
            {
                window.SelectPage(i);
                window.UpdateLayout();
                Pump();

                var path = Path.Combine(outputDir,
                    $"tab{i}-{window.PageNameAt(i)}-{(mode == ThemeMode.Dark ? "dark" : "light")}.png");

                Capture(window, path);
                Logger.Info($"已截图: {path}");
            }
        }

        // 设置页的各个分区
        foreach (var mode in new[] { ThemeMode.Dark, ThemeMode.Light })
        {
            ThemeManager.SetMode(mode);
            window.SelectPage(3);
            window.UpdateLayout();
            Pump();

            if (window.SettingsHub is { } hub)
            {
                foreach (var section in Sections)
                {
                    hub.SelectSectionForTest(section);
                    window.UpdateLayout();
                    Pump();

                    Capture(window, Path.Combine(outputDir,
                        $"setting-{section}-{(mode == ThemeMode.Dark ? "dark" : "light")}.png"));
                }
            }
        }

        // 账号页：密码显示开关
        foreach (var mode in new[] { ThemeMode.Dark, ThemeMode.Light })
        {
            ThemeManager.SetMode(mode);
            window.SelectPage(1);
            window.UpdateLayout();
            Pump();

            if (window.PageView(1) is RxNeL.Views.AccountsView accounts)
            {
                accounts.SelectRowForTest(1);
                accounts.TogglePasswordForTest();
                window.UpdateLayout();
                Pump();

                Capture(window, Path.Combine(outputDir,
                    $"accounts-shown-{(mode == ThemeMode.Dark ? "dark" : "light")}.png"));

                accounts.SelectRowForTest(1);
                accounts.TogglePasswordForTest();
            }
        }

        // 主题化对话框
        foreach (var mode in new[] { ThemeMode.Dark, ThemeMode.Light })
        {
            ThemeManager.SetMode(mode);

            var dialog = new DialogWindow();
            dialog.Setup("删除账号", "删除账号记录 demo_user？此操作不可撤销。", confirm: true, error: false);
            dialog.WindowStartupLocation = WindowStartupLocation.Manual;
            dialog.Left = 0;
            dialog.Top = 0;
            dialog.Show();
            dialog.UpdateLayout();
            Pump();

            Capture(dialog, Path.Combine(outputDir,
                $"dialog-{(mode == ThemeMode.Dark ? "dark" : "light")}.png"));

            dialog.Close();
        }

        // HUD 小窗
        foreach (var mode in new[] { ThemeMode.Dark, ThemeMode.Light })
        {
            ThemeManager.SetMode(mode);

            var hud = new HudWindow();
            hud.Show();
            hud.UpdateLayout();
            Pump();

            Capture(hud, Path.Combine(outputDir,
                $"hud-{(mode == ThemeMode.Dark ? "dark" : "light")}.png"));

            hud.Close();
        }

        // 启动遮罩
        foreach (var mode in new[] { ThemeMode.Dark, ThemeMode.Light })
        {
            ThemeManager.SetMode(mode);

            window.ShowLoadingForTest("正在准备界面… 3/6", 0.62);
            window.UpdateLayout();
            Pump();

            Capture(window, Path.Combine(outputDir,
                $"loading-{(mode == ThemeMode.Dark ? "dark" : "light")}.png"));

            window.CompleteLoading();
            Pump();
        }

        // 顺便把启动进度窗也渲染一张
        foreach (var mode in new[] { ThemeMode.Dark, ThemeMode.Light })
        {
            ThemeManager.SetMode(mode);

            var dialog = new LaunchWindow();
            dialog.Show();
            dialog.SetStatus("正在下载客户端… 42%", 0.45, "version-4ea8e85481e34f46");
            dialog.UpdateLayout();
            Pump();

            Capture(dialog, Path.Combine(outputDir,
                $"launch-dialog-{(mode == ThemeMode.Dark ? "dark" : "light")}.png"));

            dialog.Close();
        }

        window.Close();
    }

    private static void Pump() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);

    private static void Capture(Window window, string path)
    {
        if (window.Content is not FrameworkElement root) return;

        var width = (int)Math.Max(root.ActualWidth, 1);
        var height = (int)Math.Max(root.ActualHeight, 1);

        var visual = new DrawingVisual();

        using (var dc = visual.RenderOpen())
        {
            // 先铺窗口底色，否则透明区域在 PNG 里是 alpha=0
            var backdrop = window.Background;

            if (backdrop is null || backdrop is SolidColorBrush { Color.A: 0 })
                backdrop = (Brush)Application.Current.Resources["WindowBrush"];

            dc.DrawRectangle(backdrop, null, new Rect(0, 0, width, height));
            dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, width, height));
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}

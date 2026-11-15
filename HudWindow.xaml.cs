using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using RxNeL.Accounts;

namespace RxNeL;

/// <summary>
/// 常驻小窗（第二屏 HUD）：显示客户端版本、多开状态、账号数、运行时长。
/// 拖动移动，热键 Ctrl+Alt+H 或设置里开关。
/// </summary>
public partial class HudWindow : Window
{
    private readonly DispatcherTimer _timer;
    private readonly DateTime _started = DateTime.Now;

    public HudWindow()
    {
        InitializeComponent();

        var screen = SystemParameters.WorkArea;
        Left = screen.Right - Width - 24;
        Top = screen.Bottom - Height - 24;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();

        Loaded += (_, _) => Refresh();
    }

    public void Refresh()
    {
        var settings = AppSettings.Current;

        var version = Runtime.InstalledVersion ?? Deployment.GetNewestInstalledVersion();
        VersionText.Text = version is null ? "客户端：未安装" : $"客户端：{version}";

        MultiText.Text = settings.MultiInstance ? "多开：开（启动前解除单实例锁）" : "多开：关";

        AccountVault.Load();
        AccountText.Text = $"账号：{AccountVault.Accounts.Count} 个";

        var uptime = DateTime.Now - _started;
        UptimeText.Text = $"运行 {uptime:hh\\:mm\\:ss}";
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }
}

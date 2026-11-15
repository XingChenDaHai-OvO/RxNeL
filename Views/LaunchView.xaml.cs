using System;
using System.Windows;
using System.Windows.Controls;

namespace RxNeL.Views;

/// <summary>首页只保留两件事：多开开关 + 启动按钮（做大）。</summary>
public partial class LaunchView : UserControl
{
    public LaunchView()
    {
        InitializeComponent();

        MultiInstanceCheck.IsChecked = AppSettings.Current.MultiInstance;
        MultiInstanceCheck.Checked += (_, _) => SaveMultiInstance(true);
        MultiInstanceCheck.Unchecked += (_, _) => SaveMultiInstance(false);

        Runtime.StatusChanged += OnStatus;

        Loaded += (_, _) => RefreshState();
        Unloaded += (_, _) => Runtime.StatusChanged -= OnStatus;
    }

    private void OnStatus(string text)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OnStatus(text));
            return;
        }

        StatusText.Text = text;
    }

    private void SaveMultiInstance(bool value)
    {
        if (AppSettings.Current.MultiInstance == value) return;

        AppSettings.Current.MultiInstance = value;
        AppSettings.Current.Save();

        Runtime.Status(value ? "多开已开启" : "多开已关闭");
    }

    public void RefreshState()
    {
        Runtime.InstalledVersion ??= Deployment.GetNewestInstalledVersion();

        var installed = Runtime.InstalledVersion;

        StatusText.Text = installed is null
            ? "尚未安装客户端 —— 点下面按钮会自动下载并启动"
            : Deployment.IsComplete(installed)
                ? $"客户端 {installed} 就绪"
                : $"客户端 {installed} 不完整，启动时会自动修复";
    }

    private async void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        var owner = Window.GetWindow(this);

        button.IsEnabled = false;

        try
        {
            await LaunchFlow.RunAsync(owner, LaunchHandler.CreateAppTarget(AppSettings.Current));
        }
        finally
        {
            button.IsEnabled = true;
            RefreshState();
        }
    }
}

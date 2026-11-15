using System;
using System.Windows;
using System.Windows.Controls;

namespace RxNeL.Views;

public partial class SettingsView : UserControl
{
    private bool _loading;

    public SettingsView()
    {
        InitializeComponent();

        ThemeBox.Items.Add("跟随系统");
        ThemeBox.Items.Add("浅色");
        ThemeBox.Items.Add("深色");

        LoadFromSettings();

        ThemeManager.Changed += SyncThemeBox;
        Loaded += (_, _) => SyncThemeBox();
    }

    private void LoadFromSettings()
    {
        var settings = AppSettings.Current;

        _loading = true;

        ThemeBox.SelectedIndex = ThemeManager.Mode switch
        {
            ThemeMode.Light => 1,
            ThemeMode.Dark => 2,
            _ => 0
        };

        FastFlagCheck.IsChecked = settings.EnableFastFlags;
        RpcCheck.IsChecked = settings.EnableDiscordRpc;
        ModsCheck.IsChecked = settings.EnableMods;
        AutoUpdateCheck.IsChecked = settings.AutoUpdate;
        MinimizeCheck.IsChecked = settings.MinimizeOnLaunch;
        AppUpdateCheck.IsChecked = settings.AutoCheckAppUpdate;

        HudCheck.IsChecked = settings.ShowHud;
        HotkeyCheck.IsChecked = settings.EnableHotkeys;
        ProxyBox.Text = settings.ProxyUrl;
        SupabaseUrlBox.Text = settings.SupabaseUrl;
        SupabaseKeyBox.Password = settings.SupabaseAnonKey;
        RobloxLocaleBox.Text = settings.RobloxLocale;
        GameLocaleBox.Text = settings.GameLocale;
        DiscordIdBox.Text = settings.DiscordClientId;
        TemplateBox.Text = settings.LaunchArgsTemplate;
        UpdateRepoBox.Text = settings.UpdateRepo;
        UpdateManifestBox.Text = settings.UpdateManifestUrl;

        UpdateStatusText.Text = $"当前版本：v{UpdateChecker.LocalVersion}";

        _loading = false;
    }

    public void SyncThemeBox()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(SyncThemeBox);
            return;
        }

        _loading = true;

        ThemeBox.SelectedIndex = ThemeManager.Mode switch
        {
            ThemeMode.Light => 1,
            ThemeMode.Dark => 2,
            _ => 0
        };

        _loading = false;
    }

    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;

        var mode = ThemeBox.SelectedIndex switch
        {
            1 => ThemeMode.Light,
            2 => ThemeMode.Dark,
            _ => ThemeMode.System
        };

        ThemeManager.SetMode(mode);

        AppSettings.Current.ThemeMode = ThemeManager.ModeToSetting(mode);
        AppSettings.Current.Save();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var settings = AppSettings.Current;

        settings.EnableFastFlags = FastFlagCheck.IsChecked == true;
        settings.EnableDiscordRpc = RpcCheck.IsChecked == true;
        settings.EnableMods = ModsCheck.IsChecked == true;
        settings.AutoUpdate = AutoUpdateCheck.IsChecked == true;
        settings.MinimizeOnLaunch = MinimizeCheck.IsChecked == true;
        settings.AutoCheckAppUpdate = AppUpdateCheck.IsChecked == true;

        settings.ShowHud = HudCheck.IsChecked == true;
        settings.EnableHotkeys = HotkeyCheck.IsChecked == true;
        settings.SupabaseUrl = SupabaseUrlBox.Text.Trim().TrimEnd('/');
        settings.SupabaseAnonKey = SupabaseKeyBox.Password.Trim();
        settings.ProxyUrl = ProxyBox.Text.Trim();
        settings.RobloxLocale = string.IsNullOrWhiteSpace(RobloxLocaleBox.Text) ? "en_us" : RobloxLocaleBox.Text.Trim();
        settings.GameLocale = string.IsNullOrWhiteSpace(GameLocaleBox.Text) ? "en_us" : GameLocaleBox.Text.Trim();
        settings.DiscordClientId = string.IsNullOrWhiteSpace(DiscordIdBox.Text) ? "1155498055195816006" : DiscordIdBox.Text.Trim();
        settings.LaunchArgsTemplate = string.IsNullOrWhiteSpace(TemplateBox.Text)
            ? LaunchHandler.DefaultArgsTemplate
            : TemplateBox.Text.Trim();

        settings.UpdateRepo = UpdateRepoBox.Text.Trim();
        settings.UpdateManifestUrl = UpdateManifestBox.Text.Trim();

        settings.Save();

        BackendStatus.Text = Backend.SupabaseClient.Configured
            ? "已配置：注册会走 Supabase Auth（邮箱验证 + 唯一约束）"
            : "未配置：注册只写本机";

        MainWindow.Instance?.ApplyHudSetting();
        Runtime.Status("设置已保存");
    }

    private async void TestProxy_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.ProxyUrl = ProxyBox.Text.Trim();
        AppSettings.Current.Save();

        ProxyStatus.Text = "正在测试…";

        Accounts.RobloxAuth.ResetClient();
        var result = await Accounts.RobloxAuth.LookupAsync("Roblox");

        ProxyStatus.Text = result.Reachable
            ? $"网络可用（{AppSettings.Current.ProxyUrl}）：{result.Message}"
            : $"不通：{result.Message}";
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        var settings = AppSettings.Current;

        if (string.IsNullOrWhiteSpace(settings.UpdateManifestUrl) && string.IsNullOrWhiteSpace(settings.UpdateRepo))
        {
            UpdateStatusText.Text = $"当前版本：v{UpdateChecker.LocalVersion}（未配置更新源）";
            AppDialog.Info(Window.GetWindow(this), "启动器更新",
                "还没有配置更新源。在下方填 GitHub 仓库（owner/repo）或自定义清单 URL。");
            return;
        }

        try
        {
            UpdateStatusText.Text = "正在检查更新…";

            var info = await UpdateChecker.CheckAsync(settings);

            if (!UpdateChecker.IsNewer(info.Version, UpdateChecker.LocalVersion))
            {
                UpdateStatusText.Text = $"当前版本：v{UpdateChecker.LocalVersion}（已是最新）";
                return;
            }

            UpdateStatusText.Text = $"当前版本：v{UpdateChecker.LocalVersion} → 新版本 v{info.Version}";

            if (!AppDialog.Confirm(Window.GetWindow(this), "启动器更新",
                    $"发现新版本 v{info.Version}，现在下载并重启更新？"))
                return;

            var path = await UpdateChecker.DownloadAsync(info);
            UpdateChecker.ApplyAndRestart(path);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = $"检查失败：{ex.Message}";
        }
    }
}

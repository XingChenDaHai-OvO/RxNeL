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

        LegacyArgsCheck.IsChecked = settings.UseLegacyArgs;
        CleanLaunchCheck.IsChecked = settings.CleanLaunch;
        MobileCheck.IsChecked = settings.MobileRemote;
        HudCheck.IsChecked = settings.ShowHud;
        HotkeyCheck.IsChecked = settings.EnableHotkeys;
        ProxyBox.Text = settings.ProxyUrl;
        SupabaseUrlBox.Text = settings.SupabaseUrl;
        AppwriteEndpointBox.Text = settings.AppwriteEndpoint;
        AppwriteProjectBox.Text = settings.AppwriteProjectId;
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

        settings.UseLegacyArgs = LegacyArgsCheck.IsChecked == true;
        settings.CleanLaunch = CleanLaunchCheck.IsChecked == true;
        settings.MobileRemote = MobileCheck.IsChecked == true;
        settings.ShowHud = HudCheck.IsChecked == true;
        settings.EnableHotkeys = HotkeyCheck.IsChecked == true;
        settings.AppwriteEndpoint = AppwriteEndpointBox.Text.Trim().TrimEnd('/');
        settings.AppwriteProjectId = AppwriteProjectBox.Text.Trim();
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

        BackendStatus.Text = Backend.AppwriteClient.Configured
            ? "已配置 Appwrite：注册走它的邮箱验证码（自带发信）"
            : Backend.SupabaseClient.Configured
                ? "已配置 Supabase：注册走它的验证码/链接"
                : "未配置：注册只写本机";

        MainWindow.Instance?.ApplyHudSetting();
        Runtime.Status("设置已保存");
    }

    private async void NetDiag_Click(object sender, RoutedEventArgs e)
    {
        DiagHint.Text = "正在诊断…";

        try
        {
            var report = await Diagnostics.NetDiagAsync();
            DiagHint.Text = "诊断完成";

            AppDialog.Info(Window.GetWindow(this), "网络诊断", report);
        }
        catch (Exception ex)
        {
            DiagHint.Text = $"诊断失败：{ex.Message}";
        }
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


    private static async Task<string> DownloadSetupAsync(string url)
    {
        System.IO.Directory.CreateDirectory(Paths.UpdateDir);

        var target = System.IO.Path.Combine(Paths.UpdateDir, "RxNeL-Setup.exe");

        if (System.IO.File.Exists(target))
            System.IO.File.Delete(target);

        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var bytes = await http.GetByteArrayAsync(url);

        await System.IO.File.WriteAllBytesAsync(target, bytes);

        Logger.Info($"安装包已下载：{target}（{bytes.Length} 字节）");
        return target;
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

            var installed = UpdateChecker.IsInstalled(out var location);

            // 安装版：优先用安装包覆盖安装，版本号/卸载器才会一起更新
            if (installed && !string.IsNullOrWhiteSpace(info.SetupUrl))
            {
                UpdateStatusText.Text = "正在下载安装包…";

                var setupPath = await DownloadSetupAsync(info.SetupUrl);

                UpdateChecker.RunInstaller(setupPath, location);
                Application.Current.Shutdown();
                return;
            }

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

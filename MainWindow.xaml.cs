using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Interop;
using RxNeL.Views;

namespace RxNeL;

public partial class MainWindow : Window
{
    public static MainWindow? Instance { get; private set; }

    public event Action? Ready;

    private static readonly (string Title, string Subtitle)[] NavItems =
    {
        ("概览", "检查更新并启动客户端"),
        ("账号", "记录并验证 Roblox 账号（仅存本机）"),
        ("好友", "局域网好友 / 群聊 / 在线状态"),
        ("设置", "引擎 / 内容 / 美化 / 通用 / 日志")
    };

    private readonly Dictionary<int, UserControl> _views = new();
    private readonly List<RadioButton> _navButtons = new();
    private readonly string? _initialUri;
    private int _activePage = -1;
    private bool _readyFired;

    private HudWindow? _hud;
    private HwndSource? _hotkeySource;
    private const int HotkeyShowMain = 0x9001;
    private const int HotkeyToggleHud = 0x9002;

    public MainWindow(string? initialUri = null)
    {
        InitializeComponent();

        Instance = this;
        _initialUri = initialUri;

        SidebarVersion.Text = $"{AppInfo.VersionLabel}   ·   rx core";

        BuildNav();

        ChannelBox.ItemsSource = new[] { "production", "ztrprod", "zfeatured", "zintegration", "zlive" };
        ChannelBox.SelectedItem = AppSettings.Current.Channel;
        ChannelBox.SelectionChanged += ChannelBox_SelectionChanged;

        Runtime.StatusChanged += text => Dispatcher.Invoke(() => StatusText.Text = text);

        ThemeManager.Changed += RefreshThemeChrome;
        SingleInstance.MessageReceived += OnIpcMessage;

        // 首页也推迟到窗口显示之后再建，尽量缩短"点了没反应"的时间
        RefreshThemeChrome();

        // 启动遮罩：同一个窗口内覆盖，避免再开一个窗口付 1 秒初始化
        LoadingOverlay.Visibility = Visibility.Visible;
        LoadingFill.Width = 6;

        Loaded += (_, _) =>
        {
            var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.6))
            {
                RepeatBehavior = RepeatBehavior.Forever
            };

            LoadingArc.BeginAnimation(RotateTransform.AngleProperty, spin);
        };

        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        Closing += (_, _) =>
        {
            SingleInstance.MessageReceived -= OnIpcMessage;
            ThemeManager.Changed -= RefreshThemeChrome;

            if (_hotkeySource is not null)
            {
                NativeMethods.UnregisterHotKey(_hotkeySource.Handle, HotkeyShowMain);
                NativeMethods.UnregisterHotKey(_hotkeySource.Handle, HotkeyToggleHud);
                _hotkeySource.RemoveHook(HotkeyHook);
            }

            _hud?.Close();
        };
    }

    // ─────────────────────────── HUD / 热键 ───────────────────────────

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (!AppSettings.Current.EnableHotkeys) return;

        _hotkeySource = (HwndSource)PresentationSource.FromVisual(this)!;
        _hotkeySource.AddHook(HotkeyHook);

        // Ctrl+Alt+R 唤起启动器，Ctrl+Alt+H 切换 HUD
        NativeMethods.RegisterHotKey(_hotkeySource.Handle, HotkeyShowMain, 0x0002 | 0x0001, 0x52);
        NativeMethods.RegisterHotKey(_hotkeySource.Handle, HotkeyToggleHud, 0x0002 | 0x0001, 0x48);
    }

    private IntPtr HotkeyHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmHotkey = 0x0312;

        if (msg != WmHotkey) return IntPtr.Zero;

        switch (wParam.ToInt32())
        {
            case HotkeyShowMain:
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Show();
                Activate();
                handled = true;
                break;

            case HotkeyToggleHud:
                ToggleHud();
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    public void ToggleHud()
    {
        if (_hud is { IsLoaded: true })
        {
            _hud.Close();
            _hud = null;
            AppSettings.Current.ShowHud = false;
            AppSettings.Current.Save();
            return;
        }

        _hud = new HudWindow();
        _hud.Closed += (_, _) => _hud = null;
        _hud.Show();

        AppSettings.Current.ShowHud = true;
        AppSettings.Current.Save();
    }

    public void ApplyHudSetting()
    {
        if (AppSettings.Current.ShowHud && _hud is null)
            ToggleHud();
        else if (!AppSettings.Current.ShowHud && _hud is not null)
            ToggleHud();
    }

    // ─────────────────────────── 构建 ───────────────────────────

    /// <summary>按需构建页面：启动只建「概览」，其余在空闲时预热。</summary>
    private UserControl EnsureView(int index)
    {
        if (_views.TryGetValue(index, out var existing)) return existing;

        UserControl view = index switch
        {
            0 => new LaunchView(),
            1 => new AccountsView(),
            2 => new FriendsView(),
            _ => new SettingsHubView()
        };

        _views[index] = view;
        return view;
    }

    private void BuildNav()
    {
        for (var i = 0; i < NavItems.Length; i++)
        {
            var button = new RadioButton
            {
                Content = NavItems[i].Title,
                Style = (Style)FindResource("NavButton"),
                GroupName = "nav"
            };

            var index = i;
            button.Checked += (_, _) => SelectPage(index);

            NavPanel.Children.Add(button);
            _navButtons.Add(button);
        }
    }

    /// <summary>
    /// 在遮罩还盖着的时候把所有页面「显示一次 + 完成布局渲染」，
    /// 把首次切换的 100~700ms 成本挪到启动阶段（用户看到的是遮罩，不是卡顿）。
    /// </summary>
    public void PrewarmAllPages(Action<int, int>? progress = null)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();

        // 全量预热：每页现在都只有几十到一百毫秒，换来点击瞬开
        for (var i = 0; i < NavItems.Length; i++)
        {
            var pageWatch = System.Diagnostics.Stopwatch.StartNew();

            PageHost.Content = EnsureView(i);
            UpdateLayout();
            Dispatcher.Invoke(() => { }, DispatcherPriority.Render);

            Logger.Info($"[预热] {NavItems[i].Title}: {pageWatch.ElapsedMilliseconds} ms");

            progress?.Invoke(i + 1, NavItems.Length);
        }

        SelectPage(0);
        UpdateLayout();
        Dispatcher.Invoke(() => { }, DispatcherPriority.Render);

        Logger.Info($"[预热] 全部页面完成: {watch.ElapsedMilliseconds} ms");
    }

    /// <summary>异步预热：每页之后让出一次消息循环，遮罩动画不会长时间僵住。</summary>
    public async Task PrewarmAllPagesAsync(Action<int, int>? progress = null)
    {
        for (var i = 0; i < NavItems.Length; i++)
        {
            PageHost.Content = EnsureView(i);
            UpdateLayout();

            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Task.Delay(16);

            progress?.Invoke(i + 1, NavItems.Length);
        }

        SelectPage(0);
        UpdateLayout();

        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

        Logger.Info("[预热] 全部页面完成（异步）");
    }

    /// <summary>只给截图/回归用：把启动遮罩重新显示出来。</summary>
    public void ShowLoadingForTest(string text, double progress)
    {
        LoadingOverlay.Visibility = Visibility.Visible;
        LoadingOverlay.Opacity = 1;
        SetLoadingStatus(text, progress);
    }

    public void SetLoadingStatus(string text, double progress)
    {
        LoadingStatus.Text = text;
        LoadingFill.Width = Math.Max(Math.Clamp(progress, 0, 1) * 260, 6);
    }

    /// <summary>截图/回归用：直接收起遮罩，不跑淡出动画。</summary>
    public void HideLoadingForTest()
    {
        LoadingArc.BeginAnimation(RotateTransform.AngleProperty, null);
        LoadingOverlay.BeginAnimation(OpacityProperty, null);
        LoadingOverlay.Visibility = Visibility.Collapsed;
    }

    public void CompleteLoading()
    {
        LoadingArc.BeginAnimation(RotateTransform.AngleProperty, null);

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(140));
        fade.Completed += (_, _) => LoadingOverlay.Visibility = Visibility.Collapsed;

        LoadingOverlay.BeginAnimation(OpacityProperty, fade);
    }

    public int PageCount => NavItems.Length;

    public UserControl? PageView(int index) => _views.TryGetValue(index, out var view) ? view : null;

    public SettingsHubView? SettingsHub =>
        _views.Count > 3 ? _views[3] as SettingsHubView : null;

    public string PageNameAt(int index) => index >= 0 && index < NavItems.Length ? NavItems[index].Title : "?";

    public void SelectPage(int index)
    {
        if (index < 0 || index >= NavItems.Length) return;

        var watch = System.Diagnostics.Stopwatch.StartNew();

        PageHost.Content = EnsureView(index);
        PageTitle.Text = NavItems[index].Title;
        PageSubtitle.Text = NavItems[index].Subtitle;

        if (index < _navButtons.Count)
            _navButtons[index].IsChecked = true;

        _activePage = index;

        Logger.Info($"[切页] {NavItems[index].Title}: {watch.ElapsedMilliseconds} ms");
    }

    // ─────────────────────────── 生命周期 ───────────────────────────

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_readyFired) return;
        _readyFired = true;

        // 等一帧渲染完成，再让 App 关掉遮罩
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        Ready?.Invoke();

        ApplyHudSetting();

        if (!string.IsNullOrWhiteSpace(_initialUri))
            await LaunchUriAsync(_initialUri!);
    }

    private void OnIpcMessage(string message)
    {
        Dispatcher.Invoke(() =>
        {
            if (message == SingleInstance.ShowCommand)
            {
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Activate();
                return;
            }

            _ = LaunchUriAsync(message);
        });
    }

    // ─────────────────────────── 顶栏 ───────────────────────────

    private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void MinButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaxButton_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        var next = ThemeManager.IsDark ? ThemeMode.Light : ThemeMode.Dark;
        ThemeManager.SetMode(next);

        AppSettings.Current.ThemeMode = ThemeManager.ModeToSetting(next);
        AppSettings.Current.Save();

        if (PageHost.Content is SettingsView settings)
            settings.SyncThemeBox();
    }

    private void RefreshThemeChrome()
    {
        ThemeButton.Content = ThemeManager.IsDark ? "☀  浅色" : "☾  深色";
    }

    private void ChannelBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ChannelBox.SelectedItem is not string channel) return;

        Runtime.Channel = channel;
        AppSettings.Current.Channel = channel;
        AppSettings.Current.Save();

        Runtime.Status($"已切换频道：{channel}");
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        await LaunchFlow.RunAsync(this, null);
    }

    // ─────────────────────────── 状态 ───────────────────────────

    public void SetStatus(string text)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetStatus(text));
            return;
        }

        StatusText.Text = text;
    }

    public void SetProgress(double? percent)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetProgress(percent));
            return;
        }

        if (percent is null)
        {
            StatusProgress.Visibility = Visibility.Collapsed;
            StatusProgress.Value = 0;
            return;
        }

        StatusProgress.Visibility = Visibility.Visible;
        StatusProgress.Value = Math.Clamp(percent.Value, 0, 100);
    }

    // ─────────────────────────── 启动 URI ───────────────────────────

    public async Task LaunchUriAsync(string uri)
    {
        if (!LaunchHandler.TryParse(uri, out var target, out var error))
        {
            SetStatus($"启动 URI 无效：{error}");
            return;
        }

        await LaunchFlow.RunAsync(this, target);
    }
}

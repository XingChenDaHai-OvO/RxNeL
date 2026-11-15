using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using RxNeL.Accounts;
using RxNeL.Backend;
using RxNeL.Social;

namespace RxNeL.Views;

/// <summary>
/// 好友 / 群聊：局域网发现好友、看在线状态（含是否在 Roblox 里），
/// 点对点发文字、图片，链接自动变成可点击样式。
/// </summary>
public partial class FriendsView : UserControl
{
    private static readonly Regex UrlRegex =
        new(@"https?://[^\s<>""]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly LanPresence _presence = new();
    private readonly ChatService _chat = new();
    private readonly DispatcherTimer _refreshTimer;

    private Conversation? _current;
    private string _selfName = Environment.MachineName;
    private bool _socialStarted;
    private bool _skipBackendPrompt;
    private CloudFriend? _cloudFriend;
    private DispatcherTimer? _cloudTimer;
    private DateTimeOffset _cloudCursor = DateTimeOffset.UtcNow;

    public FriendsView()
    {
        InitializeComponent();

        ChatStore.Load();
        Profile.Load();

        _presence.PeersChanged += () => Dispatcher.Invoke(RefreshPeerList);
        _chat.MessageReceived += OnMessageReceived;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _refreshTimer.Tick += (_, _) => RefreshPeerList();

        Loaded += (_, _) =>
        {
            ApplyBackendMode();
            ApplyRegistrationState();
        };

        Unloaded += (_, _) =>
        {
            _refreshTimer.Stop();
            _socialStarted = false;
            _presence.Dispose();
            _chat.Dispose();
        };
    }

    // ─────────────────────────── 注册 ───────────────────────────

    private void ApplyRegistrationState()
    {
        var profile = Profile.Current;

        var needsBackend = Backend.SupabaseClient.Configured && !profile.EmailConfirmed && !_skipBackendPrompt;

        if (profile.HasAccount && needsBackend)
        {
            // 本机档案存在但没绑定后端：引导补做验证码注册
            RegisterCard.Visibility = Visibility.Visible;
            FriendsRoot.Visibility = Visibility.Collapsed;
            SkipButton.Visibility = Visibility.Visible;

            EmailBox.Text = profile.Email;
            RobloxNameBox.Text = profile.RobloxUsername;

            RegisterStatus.Text = "检测到本机档案尚未绑定后端。补做一次验证码注册即可上云；也可以先跳过继续用本机档案。";
            return;
        }

        SkipButton.Visibility = Visibility.Collapsed;

        if (!profile.HasAccount)
        {
            RegisterCard.Visibility = Visibility.Visible;
            FriendsRoot.Visibility = Visibility.Collapsed;

            if (Profile.MachineAlreadyRegistered())
                RegisterStatus.Text = "本机标记为已注册，但资料文件缺失。可以重新绑定一次。";

            return;
        }

        RegisterCard.Visibility = Visibility.Collapsed;
        FriendsRoot.Visibility = Visibility.Visible;

        SelfNameText.Text = string.IsNullOrWhiteSpace(profile.DisplayName) ? profile.RobloxUsername : profile.DisplayName;
        SelfEmailText.Text = profile.Email;
        SelfText.Text = $"@{profile.RobloxUsername} · ID {profile.UserId}";

        _ = LoadSelfAvatarAsync(profile);
        StartSocial(profile);
    }

    private void ApplyBackendMode()
    {
        var configured = Backend.SupabaseClient.Configured;

        RegisterButton.Content = configured ? "完成注册" : "验证并注册（仅本机）";
        SendCodeButton.Visibility = configured ? Visibility.Visible : Visibility.Collapsed;
        CodePanel.Visibility = configured ? Visibility.Visible : Visibility.Collapsed;
        PasswordLabel.Text = configured ? "启动器密码（与 Roblox 无关）" : "密码（可选，仅本机保存）";
        BackendHint.Visibility = configured ? Visibility.Collapsed : Visibility.Visible;
        QuotaHint.Visibility = configured ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void SendCode_Click(object sender, RoutedEventArgs e)
    {
        var email = EmailBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(email))
        {
            RegisterStatus.Text = "先填邮箱";
            return;
        }

        SendCodeButton.IsEnabled = false;
        RegisterStatus.Text = "正在发送验证码…";

        try
        {
            var result = await Backend.Registration.SendCodeAsync(email);

            RegisterStatus.Text = result.Message;

            if (result.Ok)
            {
                SendCodeButton.Content = "重新发送";
                CodeBox.Focus();
            }
        }
        finally
        {
            SendCodeButton.IsEnabled = true;
        }
    }


    private async void Register_Click(object sender, RoutedEventArgs e)
    {
        if (Profile.MachineAlreadyRegistered() && Profile.Current.HasAccount)
        {
            RegisterStatus.Text = "本机已经注册过了，一个机器只能注册一次（可先清空再注册）";
            return;
        }

        var email = EmailBox.Text.Trim();
        var username = RobloxNameBox.Text.Trim();

        var emailCheck = Profile.ValidateEmail(email);

        if (!emailCheck.Ok)
        {
            RegisterStatus.Text = emailCheck.Message;
            return;
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            RegisterStatus.Text = "请填 Roblox 用户名";
            return;
        }

        // 配置了后端：验证码换 session → 设置启动器密码 → 绑定 Roblox → 写库
        if (Backend.SupabaseClient.Configured)
        {
            var code = CodeBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(code))
            {
                RegisterStatus.Text = "先点「发送验证码」，再把邮件里的 6 位数字填进来";
                return;
            }

            RegisterStatus.Text = "正在完成注册…";

            try
            {
                var result = await Backend.Registration.CompleteAsync(email, code, username, PasswordBox.Password);

                RegisterStatus.Text = result.Message;

                if (result.Ok)
                {
                    PasswordBox.Clear();
                    CodeBox.Clear();
                    ApplyRegistrationState();
                }
            }
            catch (Exception ex)
            {
                RegisterStatus.Text = $"注册失败：{ex.Message}";
            }

            return;
        }

        RegisterStatus.Text = "正在核对 Roblox 账号…";

        var lookup = await RobloxAuth.LookupAsync(username);

        if (!lookup.Exists)
        {
            RegisterStatus.Text = lookup.Message + "（用户名不通过，无法注册）";
            return;
        }

        RegisterStatus.Text = "正在拉取 Roblox 头像…";

        var avatarUrl = await AvatarClient.GetAvatarUrlAsync(lookup.UserId, 180) ?? "";
        var bytes = await AvatarClient.GetAvatarBytesAsync(lookup.UserId, 180);

        var profile = new Profile
        {
            Email = email,
            RobloxUsername = lookup.LoginName ?? username,
            DisplayName = lookup.DisplayName ?? username,
            UserId = lookup.UserId,
            AvatarUrl = avatarUrl,
            MachineTag = Environment.MachineName
        };

        profile.Save();

        if (bytes is not null)
            SelfAvatar.Source = TryDecodeImage(Convert.ToBase64String(bytes));

        RegisterStatus.Text = "注册完成";
        Logger.Info($"注册完成：{profile.RobloxUsername}（{profile.Email}）");

        ApplyRegistrationState();
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        _skipBackendPrompt = true;
        ApplyRegistrationState();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var profile = Profile.Current;

        if (!profile.HasAccount)
        {
            RegisterStatus.Text = "本机还没有注册记录";
            return;
        }

        if (!AppDialog.Confirm(Window.GetWindow(this), "清空注册",
                $"清空本机注册（{profile.RobloxUsername}）？好友与聊天记录保留，但需要重新绑定才能继续使用。"))
            return;

        Profile.Reset();
        RegisterStatus.Text = "已清空，可以重新注册";
        ApplyRegistrationState();
    }

    private async Task LoadSelfAvatarAsync(Profile profile)
    {
        if (!string.IsNullOrEmpty(profile.AvatarUrl))
        {
            try
            {
                using var http = new System.Net.Http.HttpClient();
                var bytes = await http.GetByteArrayAsync(profile.AvatarUrl);

                if (bytes.Length > 0)
                {
                    SelfAvatar.Source = TryDecodeImage(Convert.ToBase64String(bytes));
                    return;
                }
            }
            catch
            {
                // 退回按 ID 拉取
            }
        }

        var fallback = await AvatarClient.GetAvatarBytesAsync(profile.UserId, 180);

        if (fallback is not null)
            SelfAvatar.Source = TryDecodeImage(Convert.ToBase64String(fallback));
    }

    private void StartSocial(Profile profile)
    {
        if (_socialStarted) return;

        _socialStarted = true;

        _selfName = profile.RobloxUsername;

        _chat.SelfId = _presence.SelfId;
        _chat.SelfName = _selfName;

        _presence.SelfName = profile.DisplayName is { Length: > 0 } dn ? dn : _selfName;
        _presence.RobloxName = profile.RobloxUsername;
        _presence.RobloxUserId = profile.UserId;
        _presence.AvatarUrl = profile.AvatarUrl;

        _chat.Start();
        _presence.ChatPort = _chat.Port;
        _presence.Start();

        RefreshPeerList();
        RefreshGroupList();
        _refreshTimer.Start();

        _ = InitCloudAsync();
    }

    // ─────────────────────────── 云端好友 ───────────────────────────

    private async Task InitCloudAsync()
    {
        if (!Backend.CloudSocial.Ready) return;

        var state = await Backend.CloudSocial.SignInAsync();

        MyIdText.Text = Backend.CloudSocial.MyId is { Length: > 0 } id ? id : "（登录失败）";

        if (!Backend.CloudSocial.SignedIn)
        {
            CloudEmptyText.Text = state;
            return;
        }

        await RefreshCloudAsync();

        _cloudTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _cloudTimer.Tick += async (_, _) =>
        {
            await Backend.CloudSocial.HeartbeatAsync();
            await PollCloudAsync();
        };
        _cloudTimer.Start();

        await Backend.CloudSocial.HeartbeatAsync();
    }

    private async Task RefreshCloudAsync()
    {
        var friends = await Backend.CloudSocial.ListFriendsAsync();

        CloudList.Children.Clear();
        CloudEmptyText.Visibility = friends.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MyIdText.Text = Backend.CloudSocial.MyId;

        foreach (var friend in friends)
        {
            var row = new Border
            {
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 6),
                CornerRadius = new CornerRadius(8),
                Background = (Brush)FindResource("SurfaceAltBrush")
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 8, Height = 8, Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Fill = (Brush)FindResource(friend.Online ? "SuccessBrush" : "MutedBrush")
            };

            var text = new StackPanel();
            text.Children.Add(new TextBlock
            {
                Text = friend.Title, FontSize = 12, Foreground = (Brush)FindResource("TextBrush")
            });
            text.Children.Add(new TextBlock
            {
                Text = $"{friend.LauncherId} · {friend.OnlineText}", FontSize = 10,
                Foreground = (Brush)FindResource("MutedBrush")
            });

            var chat = new Button
            {
                Content = "聊天", Style = (Style)FindResource("GhostButton"),
                Height = 24, Padding = new Thickness(10, 2, 10, 2), FontSize = 11
            };

            var captured = friend;
            chat.Click += (_, _) => OpenCloudChat(captured);

            Grid.SetColumn(dot, 0);
            Grid.SetColumn(text, 1);
            Grid.SetColumn(chat, 2);

            grid.Children.Add(dot);
            grid.Children.Add(text);
            grid.Children.Add(chat);

            row.Child = grid;
            CloudList.Children.Add(row);
        }
    }

    private async void AddById_Click(object sender, RoutedEventArgs e)
    {
        var input = FriendIdBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(input))
        {
            Runtime.Status("先输入对方的启动器 ID");
            return;
        }

        try
        {
            var (friend, message) = await Backend.CloudSocial.SearchAsync(input);

            if (friend is null)
            {
                Runtime.Status(message);
                return;
            }

            Runtime.Status(await Backend.CloudSocial.AddFriendAsync(friend));
            FriendIdBox.Clear();

            await RefreshCloudAsync();
        }
        catch (Exception ex)
        {
            Runtime.Status($"加好友失败：{ex.Message}");
        }
    }

    private void CopyMyId_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Backend.CloudSocial.MyId)) return;

        Clipboard.SetText(Backend.CloudSocial.MyId);
        Runtime.Status($"已复制我的 ID：{Backend.CloudSocial.MyId}");
    }

    private void OpenCloudChat(CloudFriend friend)
    {
        _cloudFriend = friend;

        _current = ChatStore.GetOrCreateDirect("cloud-" + friend.Id, friend.Title);

        ChatTitle.Text = friend.Title;
        ChatSubtitle.Text = $"{friend.LauncherId} · 云端私聊（对方不必在同一局域网）";

        RenderMessages();
    }

    private async Task PollCloudAsync()
    {
        if (!Backend.CloudSocial.SignedIn) return;

        try
        {
            var messages = await Backend.CloudSocial.PollAsync(_cloudCursor);

            foreach (var message in messages)
            {
                if (message.created_at > _cloudCursor)
                    _cloudCursor = message.created_at;

                if (_current is null || _cloudFriend is null) continue;
                if (message.sender != _cloudFriend.Id && message.recipient != _cloudFriend.Id) continue;

                var local = new ChatMessage
                {
                    Id = message.id,
                    ConversationId = _current.Id,
                    FromId = message.sender,
                    FromName = message.sender == Backend.CloudSocial.MyUserId ? _selfName : _cloudFriend.Title,
                    Kind = message.kind == "image" ? ChatKind.Image : ChatKind.Text,
                    Text = message.kind == "image" ? "" : (message.body ?? ""),
                    ImageBase64 = message.kind == "image" ? message.body : null,
                    SentAt = message.created_at
                };

                if (ChatStore.MessagesOf(_current.Id).Any(m => m.Id == local.Id)) continue;

                ChatStore.AddMessage(local);

                if (message.sender != Backend.CloudSocial.MyUserId)
                {
                    AppendMessage(local);
                    ScrollToEnd();
                }
            }
        }
        catch
        {
            // 轮询失败静默
        }
    }

    // ─────────────────────────── 左侧列表 ───────────────────────────

    private void RefreshPeerList()
    {
        PeerList.Children.Clear();

        var peers = _presence.Peers;

        NoPeerText.Visibility = peers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var peer in peers)
        {
            var row = new Border
            {
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 6),
                CornerRadius = new CornerRadius(8),
                Background = (Brush)FindResource("SurfaceAltBrush"),
                ToolTip = $"{peer.Address} · {peer.Version}"
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 8,
                Height = 8,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Fill = peer.Online
                    ? (Brush)FindResource(peer.RobloxRunning ? "AccentBrush" : "SuccessBrush")
                    : (Brush)FindResource("MutedBrush")
            };

            var avatar = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 8, 0),
                Background = (Brush)FindResource("SurfaceBrush"),
                ClipToBounds = true
            };

            var avatarImage = new Image { Stretch = Stretch.UniformToFill };
            avatar.Child = avatarImage;
            LoadPeerAvatar(peer, avatarImage);

            var text = new StackPanel();
            text.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(peer.RobloxName) ? peer.Name : peer.RobloxName,
                FontSize = 12,
                Foreground = (Brush)FindResource("TextBrush")
            });
            text.Children.Add(new TextBlock
            {
                Text = peer.PresenceText,
                FontSize = 10,
                Foreground = (Brush)FindResource("MutedBrush")
            });

            var chatButton = new Button
            {
                Content = "聊天",
                Style = (Style)FindResource("GhostButton"),
                Height = 24,
                Padding = new Thickness(10, 2, 10, 2),
                FontSize = 11
            };

            chatButton.Click += (_, _) => OpenDirect(peer);

            Grid.SetColumn(dot, 0);
            Grid.SetColumn(avatar, 1);
            Grid.SetColumn(text, 2);
            Grid.SetColumn(chatButton, 3);

            grid.Children.Add(dot);
            grid.Children.Add(avatar);
            grid.Children.Add(text);
            grid.Children.Add(chatButton);

            row.Child = grid;
            PeerList.Children.Add(row);
        }
    }

    private readonly Dictionary<string, BitmapImage> _avatarCache = new();

    private async void LoadPeerAvatar(PeerInfo peer, Image target)
    {
        var url = peer.AvatarUrl;

        if (string.IsNullOrEmpty(url) && peer.RobloxUserId > 0)
            url = await AvatarClient.GetAvatarUrlAsync(peer.RobloxUserId, 180);

        if (string.IsNullOrEmpty(url)) return;

        if (_avatarCache.TryGetValue(url, out var cached))
        {
            target.Source = cached;
            return;
        }

        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var bytes = await http.GetByteArrayAsync(url);
            var decoded = TryDecodeImage(Convert.ToBase64String(bytes));

            if (decoded is null) return;

            _avatarCache[url] = decoded;
            target.Source = decoded;
        }
        catch
        {
            // 忽略头像失败
        }
    }

    private void RefreshGroupList()
    {
        GroupList.Children.Clear();

        foreach (var conversation in ChatStore.Conversations.Where(c => c.IsGroup))
        {
            var button = new Button
            {
                Content = $"{conversation.Name}（{conversation.MemberIds.Count + 1} 人）",
                Style = (Style)FindResource("GhostButton"),
                Height = 28,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 6),
                HorizontalContentAlignment = HorizontalAlignment.Left
            };

            var captured = conversation;
            button.Click += (_, _) => OpenConversation(captured);

            GroupList.Children.Add(button);
        }
    }

    // ─────────────────────────── 会话 ───────────────────────────

    private void OpenDirect(PeerInfo peer)
    {
        OpenConversation(ChatStore.GetOrCreateDirect(peer.Id, peer.Name));
    }

    private void OpenConversation(Conversation conversation)
    {
        _cloudFriend = null;
        _current = conversation;

        ChatTitle.Text = conversation.Name;
        ChatSubtitle.Text = conversation.IsGroup
            ? $"群聊 · {conversation.MemberIds.Count + 1} 人 · 消息通过局域网点对点发送"
            : "私聊 · 消息只在局域网内传递";

        RenderMessages();
    }

    private void NewGroup_Click(object sender, RoutedEventArgs e)
    {
        var peers = _presence.Peers;

        if (peers.Count == 0)
        {
            AppDialog.Info(Window.GetWindow(this), "新建群聊", "还没有发现局域网内的好友。\n让好友打开本启动器，等几秒再试。");
            return;
        }

        var dialog = new GroupDialog(peers);
        dialog.Owner = Window.GetWindow(this);

        if (dialog.ShowDialog() != true) return;

        var conversation = ChatStore.CreateGroup(dialog.GroupName, dialog.SelectedPeerIds);

        RefreshGroupList();
        OpenConversation(conversation);
    }

    // ─────────────────────────── 收发 ───────────────────────────

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        var text = InputBox.Text.Trim();

        if (string.IsNullOrEmpty(text)) return;

        if (_current is null)
        {
            AppDialog.Info(Window.GetWindow(this), "发送", "先选择好友或群聊");
            return;
        }

        InputBox.Clear();

        var message = new ChatMessage
        {
            ConversationId = _current.Id,
            FromId = _chat.SelfId,
            FromName = _selfName,
            Kind = ChatKind.Text,
            Text = text
        };

        await SendToConversationAsync(message);
    }

    private async void SendImage_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null)
        {
            AppDialog.Info(Window.GetWindow(this), "发送图片", "先选择好友或群聊");
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "选择图片",
            Filter = "图片 (*.png;*.jpg;*.jpeg;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.gif;*.webp"
        };

        if (dialog.ShowDialog() != true) return;

        var info = new FileInfo(dialog.FileName);

        if (info.Length > 4 * 1024 * 1024)
        {
            AppDialog.Info(Window.GetWindow(this), "发送图片", "图片太大了（上限 4 MB）");
            return;
        }

        var bytes = await File.ReadAllBytesAsync(dialog.FileName);

        var message = new ChatMessage
        {
            ConversationId = _current.Id,
            FromId = _chat.SelfId,
            FromName = _selfName,
            Kind = ChatKind.Image,
            ImageName = info.Name,
            ImageBase64 = Convert.ToBase64String(bytes)
        };

        await SendToConversationAsync(message);
    }

    private async System.Threading.Tasks.Task SendToConversationAsync(ChatMessage message)
    {
        // 云好友：走 Supabase，不依赖局域网
        if (_cloudFriend is not null)
        {
            try
            {
                var body = message.Kind == ChatKind.Image ? message.ImageBase64 ?? "" : message.Text;
                var kind = message.Kind == ChatKind.Image ? "image" : "text";

                await CloudSocial.SendMessageAsync(_cloudFriend.Id, body, kind);

                ChatStore.AddMessage(message);
                AppendMessage(message);
                ScrollToEnd();
                Runtime.Status($"已发送给 {_cloudFriend.Title}");
            }
            catch (Exception ex)
            {
                Runtime.Status($"发送失败：{ex.Message}");
            }

            return;
        }

        ChatStore.AddMessage(message);
        AppendMessage(message);
        ScrollToEnd();

        if (_current is null) return;

        var addresses = _presence.Peers
            .Where(p => _current.MemberIds.Contains(p.Id))
            .Select(p => (p.Address, p.ChatPort))
            .ToList();

        if (addresses.Count == 0)
        {
            Runtime.Status("对方不在线，消息只保存在本地");
            return;
        }

        var sent = await _chat.SendAsync(addresses, message);
        Runtime.Status($"已发送给 {sent} 台设备");
    }

    private void OnMessageReceived(ChatMessage message)
    {
        Dispatcher.Invoke(() =>
        {
            ChatStore.AddMessage(message);

            if (_current is not null && message.ConversationId == _current.Id)
            {
                AppendMessage(message);
                ScrollToEnd();
            }

            Runtime.Status($"收到 {message.FromName} 的消息");
        });
    }

    private void InputBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            Send_Click(sender, e);
            e.Handled = true;
        }
    }

    // ─────────────────────────── 渲染 ───────────────────────────

    private void RenderMessages()
    {
        MessagePanel.Children.Clear();

        if (_current is null) return;

        foreach (var message in ChatStore.MessagesOf(_current.Id))
            AppendMessage(message);

        ScrollToEnd();
    }

    private void AppendMessage(ChatMessage message)
    {
        var mine = message.FromId == _chat.SelfId;

        var bubble = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 3, 0, 3),
            MaxWidth = 520,
            HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Background = (Brush)FindResource(mine ? "AccentSoftBrush" : "SurfaceAltBrush")
        };

        var stack = new StackPanel();

        stack.Children.Add(new TextBlock
        {
            Text = $"{message.FromName} · {message.SentAt.LocalDateTime:HH:mm}",
            FontSize = 10,
            Foreground = (Brush)FindResource("MutedBrush"),
            Margin = new Thickness(0, 0, 0, 3)
        });

        if (message.IsImage)
        {
            var image = TryDecodeImage(message.ImageBase64);

            if (image is not null)
            {
                stack.Children.Add(new Image
                {
                    Source = image,
                    MaxWidth = 420,
                    MaxHeight = 320,
                    Stretch = Stretch.Uniform
                });
            }
            else
            {
                stack.Children.Add(new TextBlock
                {
                    Text = $"（图片 {message.ImageName} 解码失败）",
                    Foreground = (Brush)FindResource("DangerBrush"),
                    FontSize = 11
                });
            }
        }
        else
        {
            stack.Children.Add(BuildLinkifiedText(message.Text, (Brush)FindResource("TextBrush")));
        }

        bubble.Child = stack;
        MessagePanel.Children.Add(bubble);
    }

    /// <summary>把文本里的 URL 拆成可点击的 Hyperlink。</summary>
    private static TextBlock BuildLinkifiedText(string text, Brush foreground)
    {
        var block = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = foreground,
            FontSize = 12,
            LineHeight = 18
        };

        var index = 0;

        foreach (Match match in UrlRegex.Matches(text))
        {
            if (match.Index > index)
                block.Inlines.Add(new Run(text[index..match.Index]));

            var link = new Hyperlink(new Run(match.Value))
            {
                Foreground = (Brush)Application.Current.Resources["AccentBrush"],
                TextDecorations = TextDecorations.Underline,
                ToolTip = "点击在浏览器打开"
            };

            var target = match.Value;
            link.Click += (_, _) => OpenUrl(target);

            block.Inlines.Add(link);
            index = match.Index + match.Length;
        }

        if (index < text.Length)
            block.Inlines.Add(new Run(text[index..]));

        return block;
    }

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Warn($"打开链接失败：{ex.Message}");
        }
    }

    private static BitmapImage? TryDecodeImage(string? base64)
    {
        if (string.IsNullOrEmpty(base64)) return null;

        try
        {
            var bytes = Convert.FromBase64String(base64);
            var bitmap = new BitmapImage();

            using var stream = new MemoryStream(bytes);

            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();

            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private void ScrollToEnd() => MessageScroll.ScrollToEnd();
}

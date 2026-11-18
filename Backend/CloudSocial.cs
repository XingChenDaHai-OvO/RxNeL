using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using RxNeL.Accounts;
using RxNeL.Social;

namespace RxNeL.Backend;

public sealed class CloudFriend
{
    public string Id { get; set; } = "";
    public string LauncherId { get; set; } = "";
    public string? RobloxUsername { get; set; }
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTimeOffset LastSeen { get; set; }

    public bool Online => (DateTimeOffset.Now - LastSeen).TotalSeconds < 90;

    public string OnlineText => Online ? "在线" : "离线";
    public string Title => string.IsNullOrWhiteSpace(DisplayName) ? (RobloxUsername ?? LauncherId) : DisplayName!;
}

/// <summary>
/// 云端好友与私聊：全部走 Supabase（经中转），不依赖同一局域网。
/// 会话用注册时保存的启动器密码自动登录换取 JWT。
/// </summary>
public static class CloudSocial
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };
    private static Session? _session;

    public static CloudFriend? Me { get; private set; }
    public static string MyId => Me?.LauncherId ?? "";

    public static bool Ready => SupabaseClient.Configured;
    public static bool SignedIn => _session is not null;

    private static string BaseUrl => AppSettings.Current.SupabaseUrl.TrimEnd('/');

    private static void Auth(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("apikey", AppSettings.Current.SupabaseAnonKey);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_session?.AccessToken}");

        var token = AppSettings.Current.RelayToken.Trim();
        if (token.Length > 0)
            request.Headers.TryAddWithoutValidation("x-relay-token", token);
    }

    /// <summary>恢复/建立会话：优先用本地 refresh token，没有就走匿名登录（无需邮箱验证码）。</summary>
    public static async Task<string> SignInAsync()
    {
        if (!SupabaseClient.Configured) return "未配置后端";

        var client = SupabaseClient.Current;

        try
        {
            var refresh = Profile.GetRefreshToken();

            if (!string.IsNullOrEmpty(refresh))
            {
                try
                {
                    _session = await client.RefreshAsync(refresh);
                }
                catch
                {
                    _session = null;
                }
            }

            if (_session is null)
            {
                _session = await client.SignInAnonymousAsync();
                Profile.Current.SetRefreshToken(_session.RefreshToken);
                Profile.Current.BackendUserId = _session.UserId;
                Profile.Current.EmailConfirmed = true;
            }
            else if (!string.IsNullOrEmpty(_session.RefreshToken))
            {
                Profile.Current.SetRefreshToken(_session.RefreshToken);
            }

            Me = await LoadMeAsync();

            return string.IsNullOrEmpty(Me?.LauncherId) ? "已登录云端" : $"已登录：{Me.LauncherId}";
        }
        catch (Exception ex)
        {
            return $"登录失败：{ex.Message}";
        }
    }

    /// <summary>开通云端好友：匿名账号 + 生成 ID + 写资料（不涉及邮箱/验证码）。</summary>
    public static async Task<string> OpenAsync(string robloxUsername)
    {
        if (!SupabaseClient.Configured) return "未配置后端";

        var lookup = await RobloxAuth.LookupAsync(robloxUsername);

        if (!lookup.Exists) return lookup.Message + "（Roblox 用户名不通过）";

        var state = await SignInAsync();

        if (_session is null) return state;

        var avatar = await AvatarClient.GetAvatarUrlAsync(lookup.UserId, 180) ?? "";

        try
        {
            await PostAsync("rx_profiles", new
            {
                id = _session.UserId,
                email = $"{_session.UserId[..8]}@anon.local",
                roblox_username = lookup.LoginName ?? robloxUsername,
                roblox_id = lookup.UserId,
                display_name = lookup.DisplayName ?? lookup.LoginName,
                avatar_url = avatar,
                machine_id = Registration.MachineId(),
                launcher_id = NewLauncherId()
            });
        }
        catch (Exception ex)
        {
            if (!ex.Message.Contains("23505") && !ex.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
                return $"写资料失败：{ex.Message}";
        }

        Me = await LoadMeAsync();

        var profile = Profile.Current;
        profile.Email = $"{_session.UserId[..8]}@anon.local";
        profile.RobloxUsername = lookup.LoginName ?? robloxUsername;
        profile.DisplayName = lookup.DisplayName ?? profile.RobloxUsername;
        profile.UserId = lookup.UserId;
        profile.AvatarUrl = avatar;
        profile.BackendUserId = _session.UserId;
        profile.EmailConfirmed = true;
        profile.MachineTag = Registration.MachineId();
        profile.Save();

        return $"已开通：{Me?.LauncherId}";
    }

    private static async Task<CloudFriend?> LoadMeAsync()
    {
        var uid = _session!.UserId;

        var json = await GetAsync($"rx_profiles?id=eq.{uid}&select=id,launcher_id,roblox_username,display_name,avatar_url,last_seen");
        var list = JsonSerializer.Deserialize<List<CloudFriend>>(json) ?? new List<CloudFriend>();

        if (list.Count == 0) return null;

        var me = list[0];

        if (string.IsNullOrWhiteSpace(me.LauncherId))
        {
            me.LauncherId = NewLauncherId();
            await PatchAsync($"rx_profiles?id=eq.{uid}", new { launcher_id = me.LauncherId });
        }

        return me;
    }

    public static string NewLauncherId()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var rnd = Random.Shared;
        var chars = new char[6];

        for (var i = 0; i < chars.Length; i++)
            chars[i] = alphabet[rnd.Next(alphabet.Length)];

        return "RX-" + new string(chars);
    }

    /// <summary>按启动器 ID 找人。</summary>
    public static async Task<(CloudFriend? Friend, string Message)> SearchAsync(string launcherId)
    {
        if (_session is null) return (null, "未登录云端");

        launcherId = launcherId.Trim().ToUpperInvariant();

        if (!launcherId.StartsWith("RX-")) launcherId = "RX-" + launcherId.TrimStart('-');

        var json = await GetAsync(
            $"rx_profiles?launcher_id=eq.{Uri.EscapeDataString(launcherId)}" +
            "&select=id,launcher_id,roblox_username,display_name,avatar_url,last_seen");

        var list = JsonSerializer.Deserialize<List<CloudFriend>>(json) ?? new List<CloudFriend>();

        if (list.Count == 0) return (null, $"没找到 ID 为 {launcherId} 的用户");

        var friend = list[0];

        if (friend.Id == _session.UserId) return (null, "这是你自己的 ID");

        return (friend, $"找到 {friend.Title}（{friend.LauncherId}）");
    }

    public static async Task<string> AddFriendAsync(CloudFriend friend)
    {
        if (_session is null) return "未登录云端";

        try
        {
            await PostAsync("rx_friends", new { owner = _session.UserId, friend = friend.Id });
            return $"已添加 {friend.Title}";
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("23505") || ex.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
                return "已经是好友了";

            return $"添加失败：{ex.Message}";
        }
    }

    public static async Task<List<CloudFriend>> ListFriendsAsync()
    {
        if (_session is null) return new List<CloudFriend>();

        var relations = JsonSerializer.Deserialize<List<RelationRow>>(
            await GetAsync($"rx_friends?select=owner,friend&or=(owner.eq.{_session.UserId},friend.eq.{_session.UserId})"))
            ?? new List<RelationRow>();

        var ids = new HashSet<string>();

        foreach (var relation in relations)
        {
            if (relation.owner != _session.UserId) ids.Add(relation.owner);
            if (relation.friend != _session.UserId) ids.Add(relation.friend);
        }

        if (ids.Count == 0) return new List<CloudFriend>();

        var filter = string.Join(",", ids);
        var json = await GetAsync(
            $"rx_profiles?id=in.({filter})&select=id,launcher_id,roblox_username,display_name,avatar_url,last_seen");

        return JsonSerializer.Deserialize<List<CloudFriend>>(json) ?? new List<CloudFriend>();
    }

    public static async Task HeartbeatAsync()
    {
        if (_session is null) return;

        try
        {
            await PatchAsync($"rx_profiles?id=eq.{_session.UserId}",
                new { last_seen = DateTimeOffset.UtcNow });
        }
        catch
        {
            // 心跳失败不影响使用
        }
    }

    public static async Task SendMessageAsync(string toId, string text, string kind = "text")
    {
        if (_session is null) throw new InvalidOperationException("未登录云端");

        await PostAsync("rx_messages", new
        {
            sender = _session.UserId,
            recipient = toId,
            kind,
            body = text
        });
    }

    public static async Task<List<CloudMessage>> PollAsync(DateTimeOffset since)
    {
        if (_session is null) return new List<CloudMessage>();

        var url = "rx_messages?select=id,sender,recipient,kind,body,created_at" +
                  $"&or=(sender.eq.{_session.UserId},recipient.eq.{_session.UserId})" +
                  $"&created_at=gt.{Uri.EscapeDataString(since.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"))}" +
                  "&order=created_at.asc";

        var json = await GetAsync(url);
        return JsonSerializer.Deserialize<List<CloudMessage>>(json) ?? new List<CloudMessage>();
    }

    public static string? MyUserId => _session?.UserId;

    // ─────────────────────────── HTTP ───────────────────────────

    private static async Task<string> GetAsync(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/rest/v1/{path}");
        Auth(request);

        using var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} {Trim(body)}");

        return body;
    }

    private static async Task PostAsync(string table, object payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/rest/v1/{table}");
        Auth(request);
        request.Headers.TryAddWithoutValidation("Prefer", "return=minimal");
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} {Trim(body)}");
    }

    private static async Task PatchAsync(string path, object payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"{BaseUrl}/rest/v1/{path}");
        Auth(request);
        request.Headers.TryAddWithoutValidation("Prefer", "return=minimal");
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} {Trim(body)}");
    }

    private static string Trim(string text) => text.Length > 160 ? text[..160] : text;

    private sealed class RelationRow
    {
        public string owner { get; set; } = "";
        public string friend { get; set; } = "";
    }
}

public sealed class CloudMessage
{
    public string id { get; set; } = "";
    public string sender { get; set; } = "";
    public string recipient { get; set; } = "";
    public string kind { get; set; } = "text";
    public string? body { get; set; }
    public DateTimeOffset created_at { get; set; }
}

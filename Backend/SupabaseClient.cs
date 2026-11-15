using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RxNeL.Backend;

public sealed class Session
{
    public string AccessToken { get; init; } = "";
    public string RefreshToken { get; init; } = "";
    public string UserId { get; init; } = "";
    public string? Email { get; init; }
    public bool EmailConfirmed { get; init; }
}

/// <summary>
/// 极简 Supabase 客户端：只用 Auth（邮箱+密码）+ PostgREST。
/// 客户端永远不碰 service_role key，权限全部交给 RLS。
/// </summary>
public sealed class SupabaseClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private readonly string _url;
    private readonly string _anon;

    public SupabaseClient(string url, string anonKey)
    {
        _url = url.TrimEnd('/');
        _anon = anonKey.Trim();
    }

    public static bool Configured =>
        !string.IsNullOrWhiteSpace(AppSettings.Current.SupabaseUrl) &&
        (!string.IsNullOrWhiteSpace(AppSettings.Current.SupabaseAnonKey) ||
         !string.IsNullOrWhiteSpace(AppSettings.Current.RelayToken));

    public static SupabaseClient Current => new(AppSettings.Current.SupabaseUrl, AppSettings.Current.SupabaseAnonKey);

    public bool LooksValid()
    {
        if (!Uri.TryCreate(_url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback) return false;

        var relay = AppSettings.Current.RelayToken.Trim();

        return relay.Length >= 8 || _anon.Length >= 20;
    }

    // ─────────────────────────── Auth ───────────────────────────

    public async Task<Session> SignUpAsync(string email, string password)
    {
        var body = await PostAuthAsync("signup", new { email, password });
        return ParseSession(body, email);
    }

    public async Task<Session> SignInAsync(string email, string password)
    {
        var body = await PostAuthAsync("token?grant_type=password", new { email, password });
        return ParseSession(body, email);
    }

    public async Task<Session> RefreshAsync(string refreshToken)
    {
        var body = await PostAuthAsync("token?grant_type=refresh_token", new { refresh_token = refreshToken });
        return ParseSession(body);
    }

    private async Task<string> PostAuthAsync(string path, object payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_url}/auth/v1/{path}")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        request.Headers.TryAddWithoutValidation("apikey", _anon);
        AddRelayToken(request);

        using var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(Describe(body, (int)response.StatusCode));

        return body;
    }

    private static void AddRelayToken(HttpRequestMessage request)
    {
        var token = AppSettings.Current.RelayToken.Trim();

        if (token.Length > 0)
            request.Headers.TryAddWithoutValidation("x-relay-token", token);
    }

    private static Session ParseSession(string body, string? emailHint = null)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var user = root.TryGetProperty("user", out var u) ? u : root;

        return new Session
        {
            AccessToken = root.TryGetProperty("access_token", out var at) ? at.GetString() ?? "" : "",
            RefreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() ?? "" : "",
            UserId = user.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
            Email = user.TryGetProperty("email", out var mail) ? mail.GetString() : emailHint,
            EmailConfirmed = user.TryGetProperty("email_confirmed_at", out var confirmed) &&
                             confirmed.ValueKind == JsonValueKind.String
        };
    }

    // ─────────────────────────── 表 ───────────────────────────

    public async Task<string> InsertProfileAsync(Session session, object profile)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_url}/rest/v1/rx_profiles");

        request.Headers.TryAddWithoutValidation("apikey", _anon);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {session.AccessToken}");
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        AddRelayToken(request);

        request.Content = new StringContent(JsonSerializer.Serialize(profile), Encoding.UTF8, "application/json");

        using var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(Describe(body, (int)response.StatusCode));

        return body;
    }

    // ───────────────────── 邮箱验证码（OTP）+ 设置密码 ─────────────────────

    /// <summary>发送 6 位邮箱验证码（不存在则创建用户）。</summary>
    public async Task SendEmailOtpAsync(string email)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_url}/auth/v1/otp")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { email, create_user = true }),
                Encoding.UTF8, "application/json")
        };

        request.Headers.TryAddWithoutValidation("apikey", _anon);
        AddRelayToken(request);

        using var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(Describe(body, (int)response.StatusCode));
    }

    /// <summary>用验证码换 session（验证成功即视为邮箱已验证）。</summary>
    public async Task<Session> VerifyEmailOtpAsync(string email, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_url}/auth/v1/verify")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { type = "email", email, token }),
                Encoding.UTF8, "application/json")
        };

        request.Headers.TryAddWithoutValidation("apikey", _anon);
        AddRelayToken(request);

        using var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(Describe(body, (int)response.StatusCode));

        return ParseSession(body, email);
    }

    /// <summary>给当前用户设置启动器密码（独立于 Roblox 密码）。</summary>
    public async Task SetPasswordAsync(Session session, string password)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{_url}/auth/v1/user")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { password }),
                Encoding.UTF8, "application/json")
        };

        request.Headers.TryAddWithoutValidation("apikey", _anon);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {session.AccessToken}");
        AddRelayToken(request);

        using var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(Describe(body, (int)response.StatusCode));
    }

    public async Task<string> GetMyProfileAsync(Session session)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_url}/rest/v1/rx_profiles?select=*");

        request.Headers.TryAddWithoutValidation("apikey", _anon);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {session.AccessToken}");
        AddRelayToken(request);

        using var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(Describe(body, (int)response.StatusCode));

        return body;
    }

    private static string Describe(string body, int status)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            foreach (var key in new[] { "msg", "message", "error_description", "error", "hint", "details" })
                if (root.TryGetProperty(key, out var node) && node.ValueKind == JsonValueKind.String)
                    return $"{status}: {node.GetString()}";
        }
        catch
        {
            // 非 JSON
        }

        return $"{status}: {(body.Length > 160 ? body[..160] : body)}";
    }
}

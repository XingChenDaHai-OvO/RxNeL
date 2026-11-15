using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RxNeL.Accounts;

public sealed class AccountCheckResult
{
    public bool Reachable { get; init; }
    public bool Exists { get; init; }
    public long UserId { get; init; }
    public string? DisplayName { get; init; }
    public string? LoginName { get; init; }
    public string Message { get; init; } = "";
}

/// <summary>
/// 账号验真：只确认用户名是否真实存在（users.roblox.com 公开接口）。
/// 密码不做任何验证，也不上传 —— 只在本机 DPAPI 加密保存。
/// </summary>
public static class RobloxAuth
{
    private static HttpClient? _client;

    private static HttpClient Client
    {
        get
        {
            if (_client is not null) return _client;

            var handler = new HttpClientHandler
            {
                UseCookies = false,
                UseProxy = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            var proxy = AppSettings.Current.ProxyUrl.Trim();

            if (!string.IsNullOrEmpty(proxy) && Uri.TryCreate(proxy, UriKind.Absolute, out var proxyUri))
                handler.Proxy = new WebProxy(proxyUri);

            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };

            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) RxNeL/" + AppInfo.VersionLabel);
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");

            _client = client;
            return client;
        }
    }

    public static void ResetClient()
    {
        _client?.Dispose();
        _client = null;
    }

    /// <summary>用户名是否存在（顺带拿到 id / displayName，用于确认不是同名仿冒）。</summary>
    public static async Task<AccountCheckResult> LookupAsync(string username)
    {
        username = username.Trim();

        if (username.Length is < 3 or > 20)
            return new AccountCheckResult { Message = "用户名长度应为 3~20 个字符" };

        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                usernames = new[] { username },
                excludeBannedUsers = false
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await Client.PostAsync("https://users.roblox.com/v1/usernames/users", content);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return new AccountCheckResult
                {
                    Reachable = false,
                    Message = $"查询失败：{(int)response.StatusCode} {Trim(body)}"
                };

            using var doc = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
                return new AccountCheckResult { Reachable = true, Message = "该用户名不存在" };

            var user = data[0];

            var name = user.TryGetProperty("name", out var nm) ? nm.GetString() ?? username : username;
            var display = user.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? name : name;
            var id = user.TryGetProperty("id", out var i) ? i.GetInt64() : 0;

            return new AccountCheckResult
            {
                Reachable = true,
                Exists = true,
                UserId = id,
                DisplayName = display,
                LoginName = name,
                Message = $"账号存在：{name}（{display}，ID {id}）"
            };
        }
        catch (Exception ex)
        {
            return new AccountCheckResult { Reachable = false, Message = $"连接 Roblox 失败：{ex.Message}" };
        }
    }

    private static string Trim(string body) => body.Length > 160 ? body[..160] : body;
}

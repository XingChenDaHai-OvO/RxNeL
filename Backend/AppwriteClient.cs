using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RxNeL.Backend;

/// <summary>
/// Appwrite Cloud 客户端：邮箱验证码（Email OTP）+ 账号 + 资料文档。
/// 相比 Supabase：自带发信（不用配 SMTP）、能直接连（cloud.appwrite.io 实测可达，不需要中转）。
/// </summary>
public sealed class AppwriteClient
{
    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        UseCookies = true,
        CookieContainer = new CookieContainer(),
        UseProxy = true
    })
    { Timeout = TimeSpan.FromSeconds(25) };

    private readonly string _endpoint;
    private readonly string _projectId;

    public AppwriteClient(string endpoint, string projectId)
    {
        _endpoint = endpoint.TrimEnd('/');
        _projectId = projectId.Trim();
    }

    public static bool Configured =>
        !string.IsNullOrWhiteSpace(AppSettings.Current.AppwriteEndpoint) &&
        !string.IsNullOrWhiteSpace(AppSettings.Current.AppwriteProjectId);

    public static AppwriteClient Current =>
        new(AppSettings.Current.AppwriteEndpoint, AppSettings.Current.AppwriteProjectId);

    private HttpRequestMessage Request(HttpMethod method, string path, object? payload = null)
    {
        var request = new HttpRequestMessage(method, $"{_endpoint}{path}");

        request.Headers.TryAddWithoutValidation("X-Appwrite-Project", _projectId);
        request.Headers.TryAddWithoutValidation("X-Appwrite-Response-Format", "1.6.0");

        if (payload is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        return request;
    }

    private static async Task<string> SendAsync(HttpRequestMessage request)
    {
        using var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} {Trim(body)}");

        return body;
    }

    /// <summary>创建账号（邮箱 + 密码 + 名字）。</summary>
    public async Task<string> CreateAccountAsync(string email, string password, string name)
    {
        var userId = "user_" + Guid.NewGuid().ToString("N")[..12];

        using var request = Request(HttpMethod.Post, "/account", new
        {
            userId,
            email,
            password,
            name
        });

        var body = await SendAsync(request);
        return body;
    }

    /// <summary>发送 6 位邮箱验证码。</summary>
    public async Task SendEmailTokenAsync(string userId, string email)
    {
        using var request = Request(HttpMethod.Post, "/account/tokens/email", new { userId, email });
        await SendAsync(request);
    }

    /// <summary>用验证码换会话。</summary>
    public async Task<string> CreateSessionWithTokenAsync(string userId, string secret)
    {
        using var request = Request(HttpMethod.Post, "/account/sessions/token", new { userId, secret });
        return await SendAsync(request);
    }

    public async Task<string> GetAccountAsync()
    {
        using var request = Request(HttpMethod.Get, "/account");
        return await SendAsync(request);
    }

    /// <summary>写资料文档到指定 database/collection。</summary>
    public async Task<string> CreateDocumentAsync(string databaseId, string collectionId, object data, string? documentId = null)
    {
        var id = documentId ?? "unique()";
        var path = $"/databases/{databaseId}/collections/{collectionId}/documents";

        using var request = Request(HttpMethod.Post, path, new { documentId = id, data });
        return await SendAsync(request);
    }

    private static string Trim(string text) => text.Length > 180 ? text[..180] : text;
}

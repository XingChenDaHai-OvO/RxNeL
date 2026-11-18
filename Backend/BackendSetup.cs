using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RxNeL.Backend;

/// <summary>后端配置自检 + 建表（建表需要 Personal Access Token，只在本次调用里用）。</summary>
public static class BackendSetup
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };

    public static string? RefFromUrl(string url)
    {
        if (!Uri.TryCreate(url.TrimEnd('/'), UriKind.Absolute, out var uri)) return null;

        return uri.Host.EndsWith(".supabase.co", StringComparison.OrdinalIgnoreCase)
            ? uri.Host[..uri.Host.IndexOf('.')]
            : null;
    }

    /// <summary>检查 anon key 是否有效（/auth/v1/settings），以及 rx_profiles 表是否已建。</summary>
    public static async Task<string> VerifyAsync(string url, string anonKey)
    {
        var baseUrl = url.TrimEnd('/');
        var lines = new StringBuilder();

        try
        {
            using var settingsReq = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/auth/v1/settings");
            settingsReq.Headers.TryAddWithoutValidation("apikey", anonKey);
            AddRelayToken(settingsReq);

            using var settingsResp = await Http.SendAsync(settingsReq);
            var settingsBody = await settingsResp.Content.ReadAsStringAsync();

            lines.AppendLine(settingsResp.IsSuccessStatusCode
                ? $"auth: OK（{(int)settingsResp.StatusCode}）"
                : $"auth: 失败（{(int)settingsResp.StatusCode}）{Trim(settingsBody)}");
        }
        catch (Exception ex)
        {
            lines.AppendLine($"auth: 连不上（{ex.Message}）");
        }

        try
        {
            using var restReq = new HttpRequestMessage(HttpMethod.Get,
                $"{baseUrl}/rest/v1/rx_profiles?select=id&limit=1");

            restReq.Headers.TryAddWithoutValidation("apikey", anonKey);
            AddRelayToken(restReq);

            using var restResp = await Http.SendAsync(restReq);
            var restBody = await restResp.Content.ReadAsStringAsync();

            var code = (int)restResp.StatusCode;

            lines.AppendLine(code switch
            {
                200 or 206 => "table: rx_profiles 已存在（RLS 只返回自己的行，匿名下为空数组属正常）",
                401 or 403 => "table: 需要登录（正常，RLS 生效）",
                404 => $"table: 不存在 → 先在 SQL Editor 执行 db/profiles.sql（{Trim(restBody)}）",
                _ => $"table: {(int)restResp.StatusCode} {Trim(restBody)}"
            });
        }
        catch (Exception ex)
        {
            lines.AppendLine($"table: 检查失败（{ex.Message}）");
        }

        return lines.ToString().TrimEnd();
    }

    /// <summary>用 Personal Access Token 通过管理 API 执行 db/profiles.sql。</summary>
    public static async Task<string> ApplySchemaAsync(string url, string personalAccessToken)
    {
        var reference = RefFromUrl(url);

        if (reference is null)
            return "URL 不是 https://<ref>.supabase.co 形式";

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.supabase.com/v1/projects/{reference}/database/query");

        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {personalAccessToken.Trim()}");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { query = Registration.SchemaSql }), Encoding.UTF8, "application/json");

        using var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        return response.IsSuccessStatusCode
            ? $"建表完成：{reference} → public.rx_profiles"
            : $"建表失败（{(int)response.StatusCode}）：{Trim(body)}";
    }

    private static void AddRelayToken(HttpRequestMessage request)
    {
        var token = AppSettings.Current.RelayToken.Trim();

        if (token.Length > 0)
            request.Headers.TryAddWithoutValidation("x-relay-token", token);
    }

    /// <summary>
    /// 用 Supabase 管理 API 配置自定义 SMTP，并把验证码邮件模板改成 6 位数字。
    /// 需要 Personal Access Token（sbp_...），只在本次调用里用。
    /// </summary>
    public static async Task<string> ApplySmtpAsync(
        string url, string pat, string host, int port, string user, string pass, string fromEmail, string fromName)
    {
        var reference = RefFromUrl(url);

        if (reference is null) return "URL 不是 https://<ref>.supabase.co 形式";

        var payload = new
        {
            smtp_host = host,
            smtp_port = port.ToString(),
            smtp_user = user,
            smtp_pass = pass,
            smtp_sender_name = fromName,
            smtp_admin_email = fromEmail,
            rate_limit_email_sent = 60,
            mailer_otp_length = 6,
            mailer_otp_exp = 600,
            mailer_subjects_confirmation = "Rx | NeL 注册验证码",
            mailer_templates_confirmation_content =
                "<h2>Rx | NeL 注册验证码</h2><p>你的验证码是：</p>" +
                "<p style=\"font-size:32px;letter-spacing:6px;font-weight:bold\">{{ .Token }}</p>" +
                "<p>10 分钟内有效。如果这不是你本人操作，忽略本邮件即可。</p>"
        };

        using var request = new HttpRequestMessage(HttpMethod.Patch,
            $"https://api.supabase.com/v1/projects/{reference}/config/auth");

        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {pat.Trim()}");
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        return response.IsSuccessStatusCode
            ? $"SMTP 已配置：{host}:{port}（发信人 {fromEmail}），验证码模板改为 6 位数字，限流 60/h"
            : $"配置失败（{(int)response.StatusCode}）：{Trim(body)}";
    }

    private static string Trim(string text) => text.Length > 200 ? text[..200] : text;
}

using System;
using System.Threading.Tasks;
using RxNeL.Accounts;
using RxNeL.Social;

namespace RxNeL.Backend;

/// <summary>
/// 注册：邮箱验证码（Supabase OTP）+ 独立启动器密码 + Roblox 账号绑定。
/// 唯一性仍由 rx_profiles 的三条唯一约束兜底（email / roblox_id / machine_id）。
/// </summary>
public static class Registration
{

    public const string SchemaSql = """
create table if not exists public.rx_profiles (
    id              uuid primary key references auth.users (id) on delete cascade,
    email           text not null,
    roblox_username text not null,
    roblox_id       bigint not null,
    display_name    text,
    avatar_url      text,
    machine_id      text not null,
    created_at      timestamptz not null default now(),
    constraint rx_profiles_email_key unique (email),
    constraint rx_profiles_roblox_key unique (roblox_id),
    constraint rx_profiles_machine_key unique (machine_id)
);

alter table public.rx_profiles enable row level security;

drop policy if exists "rx_profiles_select_own" on public.rx_profiles;
drop policy if exists "rx_profiles_insert_own" on public.rx_profiles;
drop policy if exists "rx_profiles_update_own" on public.rx_profiles;

create policy "rx_profiles_select_own" on public.rx_profiles
    for select using (auth.uid() = id);

create policy "rx_profiles_insert_own" on public.rx_profiles
    for insert with check (auth.uid() = id);

create policy "rx_profiles_update_own" on public.rx_profiles
    for update using (auth.uid() = id) with check (auth.uid() = id);
""";

    public sealed class Result
    {
        public bool Ok { get; init; }
        public string Message { get; init; } = "";
    }

    private static string PendingEmail = "";
    private static string PendingUserId = "";
    private static string PendingPassword = "";
    private static string PendingRoblox = "";

    public static bool AppwriteMode => AppwriteClient.Configured;
    public static bool BackendConfigured => AppwriteClient.Configured || SupabaseClient.Configured;

    /// <summary>Appwrite：创建账号 + 发送 6 位验证码。</summary>
    public static async Task<Result> AppwriteSendCodeAsync(string email, string password, string robloxUsername)
    {
        if (!AppwriteClient.Configured) return new Result { Message = "未配置 Appwrite" };

        email = email.Trim().ToLowerInvariant();

        if (!email.Contains('@')) return new Result { Message = "邮箱格式不对" };
        if (password.Length < 6) return new Result { Message = "启动器密码至少 6 位" };
        if (string.IsNullOrWhiteSpace(robloxUsername)) return new Result { Message = "先填 Roblox 用户名（用于创建账号名）" };

        var client = AppwriteClient.Current;

        try
        {
            var body = await client.CreateAccountAsync(email, password, robloxUsername);
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            PendingUserId = doc.RootElement.TryGetProperty("$id", out var id) ? id.GetString() ?? "" : "";

            if (string.IsNullOrEmpty(PendingUserId))
                return new Result { Message = "创建账号失败：没拿到用户 ID" };

            await client.SendEmailTokenAsync(PendingUserId, email);

            PendingEmail = email;
            PendingPassword = password;
            PendingRoblox = robloxUsername;

            return new Result { Ok = true, Message = $"验证码已发送到 {email}（6 位数字）" };
        }
        catch (Exception ex)
        {
            var message = ex.Message;

            if (message.Contains("409") || message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                return new Result { Message = "这个邮箱已经注册过了（Appwrite）" };

            return new Result { Message = $"发送失败：{message}" };
        }
    }

    /// <summary>Appwrite：用验证码换会话 + 写资料文档。</summary>
    public static async Task<Result> AppwriteCompleteAsync(string code)
    {
        if (string.IsNullOrEmpty(PendingUserId))
            return new Result { Message = "先点「发送验证码」" };

        var client = AppwriteClient.Current;

        try
        {
            await client.CreateSessionWithTokenAsync(PendingUserId, code.Trim());
        }
        catch (Exception ex)
        {
            return new Result { Message = $"验证码校验失败：{ex.Message}" };
        }

        var lookup = await RobloxAuth.LookupAsync(PendingRoblox);

        if (!lookup.Exists)
            return new Result { Message = lookup.Message + "（Roblox 用户名不通过）" };

        var avatarUrl = await AvatarClient.GetAvatarUrlAsync(lookup.UserId, 180) ?? "";

        try
        {
            await client.CreateDocumentAsync(
                AppSettings.Current.AppwriteDatabaseId,
                AppSettings.Current.AppwriteCollectionId,
                new
                {
                    auth_user_id = PendingUserId,
                    email = PendingEmail,
                    roblox_username = lookup.LoginName ?? PendingRoblox,
                    roblox_id = lookup.UserId,
                    display_name = lookup.DisplayName ?? lookup.LoginName,
                    avatar_url = avatarUrl,
                    machine_id = MachineId()
                },
                PendingUserId);
        }
        catch (Exception ex)
        {
            return new Result { Message = $"写资料失败：{ex.Message}" };
        }

        var profile = Profile.Current;
        profile.Email = PendingEmail;
        profile.RobloxUsername = lookup.LoginName ?? PendingRoblox;
        profile.DisplayName = lookup.DisplayName ?? profile.RobloxUsername;
        profile.UserId = lookup.UserId;
        profile.AvatarUrl = avatarUrl;
        profile.BackendUserId = PendingUserId;
        profile.EmailConfirmed = true;
        profile.MachineTag = MachineId();
        profile.SetPassword(PendingPassword);

        return new Result { Ok = true, Message = $"注册完成：{profile.DisplayName}（{profile.Email}）" };
    }

    /// <summary>第一步：发 6 位验证码到邮箱（账号不存在会自动创建）。</summary>
    public static async Task<Result> SendCodeAsync(string email)
    {
        if (!SupabaseClient.Configured)
            return new Result { Message = "未配置后端" };

        email = email.Trim().ToLowerInvariant();

        if (!email.Contains('@') || email.Length < 6)
            return new Result { Message = "邮箱格式不对" };

        var client = SupabaseClient.Current;

        if (!client.LooksValid())
            return new Result { Message = "后端地址或令牌不合法" };

        try
        {
            await client.SendEmailOtpAsync(email);
            PendingEmail = email;

            return new Result { Ok = true, Message = $"验证码已发送到 {email}（6 位数字，10 分钟内有效）" };
        }
        catch (Exception ex)
        {
            var message = ex.Message;

            if (message.Contains("429") || message.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
                return new Result
                {
                    Message = "Supabase 免费发信每小时只有 2 封，现在额度用完了。约 1 小时后重试，" +
                              "或配一个自定义 SMTP（Mailtrap 免费 4000 封/月）"
                };

            if (message.Contains("Unable to validate email", StringComparison.OrdinalIgnoreCase))
                return new Result { Message = "邮箱地址被 Supabase 拒绝（example.com 这类域名不能用，换 QQ/Gmail）" };

            return new Result { Message = $"发送失败：{message}" };
        }
    }

    /// <summary>第二步：验证码换 session → 设置启动器密码 → 绑定 Roblox → 写库。</summary>
    public static async Task<Result> CompleteAsync(string email, string code, string robloxUsername, string password)
    {
        if (!SupabaseClient.Configured)
            return new Result { Message = "未配置后端" };

        email = email.Trim().ToLowerInvariant();
        code = code.Trim();

        // 兼容两种输入：6 位数字验证码，或整段确认链接（从中取 token）
        var isLink = code.Contains("token=", StringComparison.OrdinalIgnoreCase);

        if (!isLink && code.Length is < 4 or > 8)
            return new Result { Message = "验证码看起来不对（应为 6 位数字，或整段确认链接）" };

        if (password.Length < 6)
            return new Result { Message = "启动器密码至少 6 位" };

        var client = SupabaseClient.Current;
        Session session;

        try
        {
            if (isLink)
            {
                var token = ExtractToken(code);
                var type = ExtractType(code);

                if (string.IsNullOrEmpty(token))
                    return new Result { Message = "链接里没找到 token，复制完整一点" };

                session = await client.VerifyEmailOtpAsync(email, token, type);
            }
            else
            {
                session = await client.VerifyEmailOtpAsync(email, code);
            }
        }
        catch (Exception ex)
        {
            return new Result { Message = $"验证码校验失败：{ex.Message}" };
        }

        if (string.IsNullOrEmpty(session.AccessToken))
            return new Result { Message = "验证通过但没拿到会话，稍后再试" };

        try
        {
            await client.SetPasswordAsync(session, password);
        }
        catch (Exception ex)
        {
            return new Result { Message = $"设置密码失败：{ex.Message}" };
        }

        var lookup = await RobloxAuth.LookupAsync(robloxUsername);

        if (!lookup.Exists)
            return new Result { Message = lookup.Message + "（Roblox 用户名不通过，注册未完成）" };

        var avatarUrl = await AvatarClient.GetAvatarUrlAsync(lookup.UserId, 180) ?? "";
        var machineId = MachineId();

        var row = new
        {
            id = session.UserId,
            email,
            roblox_username = lookup.LoginName ?? robloxUsername,
            roblox_id = lookup.UserId,
            display_name = lookup.DisplayName ?? lookup.LoginName,
            avatar_url = avatarUrl,
            machine_id = machineId,
            launcher_id = CloudSocial.NewLauncherId()
        };

        try
        {
            await client.InsertProfileAsync(session, row);
        }
        catch (Exception ex)
        {
            var message = ex.Message;

            if (message.Contains("23505") || message.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
            {
                if (message.Contains("roblox_id")) return new Result { Message = "这个 Roblox 账号已经注册过了" };
                if (message.Contains("machine_id")) return new Result { Message = "这台机器已经注册过账号了" };
                return new Result { Message = "这个邮箱已经注册过了" };
            }

            return new Result { Message = $"写资料失败：{message}" };
        }

        var profile = Profile.Current;
        profile.Email = email;
        profile.RobloxUsername = row.roblox_username;
        profile.DisplayName = row.display_name;
        profile.UserId = row.roblox_id;
        profile.AvatarUrl = avatarUrl;
        profile.BackendUserId = session.UserId;
        profile.EmailConfirmed = true;
        profile.MachineTag = machineId;
        profile.SetPassword(password);

        PendingEmail = "";

        return new Result { Ok = true, Message = $"注册完成：{profile.DisplayName}（{profile.Email}）" };
    }

    private static string ExtractToken(string link)
    {
        var marker = "token=";
        var start = link.IndexOf(marker, StringComparison.OrdinalIgnoreCase);

        if (start < 0) return "";

        start += marker.Length;

        var end = link.IndexOf('&', start);
        var token = end < 0 ? link[start..] : link[start..end];

        // 链接里常把 token 做 URL 编码（%3D 之类），这里还原
        return Uri.UnescapeDataString(token).Trim();
    }

    private static string ExtractType(string link)
    {
        var marker = "type=";
        var start = link.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        var end = start < 0 ? -1 : link.IndexOf('&', start);

        if (start < 0) return "signup";

        var type = link[(start + marker.Length)..(end < 0 ? link.Length : end)];

        return string.IsNullOrWhiteSpace(type) ? "signup" : type.Trim();
    }

    public static string MachineId()
    {
        var id = Profile.Current.MachineTag;

        if (!string.IsNullOrWhiteSpace(id)) return id;

        return $"{Environment.MachineName}-{Environment.UserName}";
    }
}

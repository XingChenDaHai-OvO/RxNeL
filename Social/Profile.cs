using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace RxNeL.Social;

/// <summary>
/// 本机注册资料：绑定邮箱 + Roblox 账号。用户名/昵称/头像一律以 Roblox 为准。
/// 没有后端，所以「邮箱验证」只能做到格式 + 域名可解析这一层，不做真实收信验证。
/// </summary>
public sealed class Profile
{
    public string Email { get; set; } = "";
    public string RobloxUsername { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public long UserId { get; set; }
    public string AvatarUrl { get; set; } = "";
    public DateTimeOffset RegisteredAt { get; set; } = DateTimeOffset.Now;
    public string MachineTag { get; set; } = "";

    /// <summary>后端（Supabase）用户 ID；为空表示只在本机注册。</summary>
    public string BackendUserId { get; set; } = "";

    /// <summary>邮箱是否已通过后端验证。</summary>
    public bool EmailConfirmed { get; set; }

    /// <summary>云端 refresh token（DPAPI 加密），用于免登录恢复会话。</summary>
    public string RefreshTokenProtected { get; set; } = "";

    /// <summary>启动器密码（DPAPI 加密），用于云端自动登录。</summary>
    public string PasswordProtected { get; set; } = "";

    public void SetPassword(string password)
    {
        PasswordProtected = string.IsNullOrEmpty(password) ? "" : Accounts.Dpapi.Protect(password);
        Save();
    }

    public void SetRefreshToken(string token)
    {
        RefreshTokenProtected = string.IsNullOrEmpty(token) ? "" : Accounts.Dpapi.Protect(token);
        Save();
    }

    public static string? GetRefreshToken()
    {
        var value = Current.RefreshTokenProtected;

        if (string.IsNullOrEmpty(value)) return null;

        try { return Accounts.Dpapi.Unprotect(value); }
        catch { return null; }
    }

    public static string? GetPassword()
    {
        var value = Current.PasswordProtected;

        if (string.IsNullOrEmpty(value)) return null;

        try
        {
            return Accounts.Dpapi.Unprotect(value);
        }
        catch
        {
            return null;
        }
    }

    public static string FilePath => Path.Combine(Paths.AppDir, "profile.json");

    private static Profile? _current;
    private static bool _loaded;

    public static Profile Current
    {
        get
        {
            Load();
            return _current ??= new Profile();
        }
    }

    public static bool IsRegistered => Current.HasAccount;

    public bool HasAccount => UserId > 0 && !string.IsNullOrWhiteSpace(RobloxUsername);

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            if (!File.Exists(FilePath)) return;

            _current = JsonSerializer.Deserialize<Profile>(File.ReadAllText(FilePath));
        }
        catch (Exception ex)
        {
            Logger.Error("读取注册资料失败", ex);
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Paths.AppDir);

            File.WriteAllText(FilePath, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true }));

            using var key = Registry.CurrentUser.CreateSubKey(@"Software\RxNeL");
            key?.SetValue("Registered", HasAccount ? 1 : 0);
            key?.SetValue("RegisteredUser", RobloxUsername);

            _current = this;
        }
        catch (Exception ex)
        {
            Logger.Error("保存注册资料失败", ex);
        }
    }

    /// <summary>清空注册（用于换绑；需要调用方二次确认）。</summary>
    public static void Reset()
    {
        try
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);

            using var key = Registry.CurrentUser.OpenSubKey(@"Software\RxNeL", writable: true);
            key?.SetValue("Registered", 0);
            key?.DeleteValue("RegisteredUser", throwOnMissingValue: false);

            _current = new Profile();
        }
        catch (Exception ex)
        {
            Logger.Error("重置注册资料失败", ex);
        }
    }

    /// <summary>本机是否已经注册过（防止重复注册）。</summary>
    public static bool MachineAlreadyRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\RxNeL");
            return key?.GetValue("Registered") is int value && value == 1;
        }
        catch
        {
            return false;
        }
    }

    // ─────────────────────────── 校验 ───────────────────────────

    private static readonly Regex EmailRegex = new(
        @"^[A-Za-z0-9._%+\-]+@([A-Za-z0-9\-]+\.)+[A-Za-z]{2,}$",
        RegexOptions.Compiled);

    public static (bool Ok, string Message) ValidateEmail(string email)
    {
        email = email.Trim();

        if (!EmailRegex.IsMatch(email))
            return (false, "邮箱格式不对");

        var domain = email[(email.IndexOf('@') + 1)..];

        try
        {
            var addresses = System.Net.Dns.GetHostAddresses(domain);

            return addresses.Length > 0
                ? (true, $"{domain} 可解析（未做真实收信验证，本机无后端）")
                : (false, $"{domain} 解析不到地址");
        }
        catch
        {
            return (false, $"{domain} 无法解析，检查是否有拼写错误");
        }
    }
}

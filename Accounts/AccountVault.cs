using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RxNeL.Accounts;

public sealed class AccountRecord
{
    public string Username { get; set; } = "";
    public string? DisplayName { get; set; }
    public long UserId { get; set; }

    /// <summary>DPAPI 加密后的密码（Base64）。</summary>
    public string PasswordProtected { get; set; } = "";

    public string Status { get; set; } = "未验证";
    public string? Note { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    [JsonIgnore]
    public bool HasPassword => !string.IsNullOrEmpty(PasswordProtected);

    [JsonIgnore]
    public string UserIdText => UserId > 0 ? UserId.ToString() : "—";

    [JsonIgnore]
    public string UpdatedText => UpdatedAt.LocalDateTime.ToString("MM-dd HH:mm");

    /// <summary>全局开关：是否把密码明文显示在列表里。</summary>
    [JsonIgnore]
    public static bool ShowPlain { get; set; }

    /// <summary>解密后的密码，仅在 ShowPlain 打开时由界面填充，关闭后立即清空。</summary>
    [JsonIgnore]
    public string? PlainPassword { get; set; }

    [JsonIgnore]
    public string PasswordDisplay
    {
        get
        {
            if (!HasPassword) return "—";
            if (!ShowPlain) return "••••••••";

            if (PlainPassword is null)
            {
                try
                {
                    PlainPassword = Dpapi.Unprotect(PasswordProtected);
                }
                catch
                {
                    return "（解不开）";
                }
            }

            return string.IsNullOrEmpty(PlainPassword) ? "（解不开）" : PlainPassword!;
        }
    }
}

/// <summary>
/// 账号库：%LOCALAPPDATA%\RbxLite\accounts.json，密码用 DPAPI 加密，只存本机。
/// </summary>
public static class AccountVault
{
    private static readonly object Sync = new();

    public static ObservableCollection<AccountRecord> Accounts { get; } = new();

    public static string FilePath => Path.Combine(Paths.AppDir, "accounts.json");

    public static bool Loaded { get; private set; }

    public static void Load()
    {
        lock (Sync)
        {
            if (Loaded) return;
            Loaded = true;

            try
            {
                if (!File.Exists(FilePath)) return;

                var json = File.ReadAllText(FilePath);
                var list = JsonSerializer.Deserialize<List<AccountRecord>>(json) ?? new List<AccountRecord>();

                Accounts.Clear();

                foreach (var record in list)
                {
                    // 旧版本遗留的状态（Cookie / 网络不可用）统一归位
                    if (record.Status is "待补 Cookie" or "网络不可用，仅记录" or "Cookie 已验证" or "Cookie 失效")
                        record.Status = record.HasPassword ? "用户名已验证" : "未验证";

                    Accounts.Add(record);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("读取账号库失败", ex);
            }
        }
    }

    public static void Save()
    {
        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(Paths.AppDir);

                var json = JsonSerializer.Serialize(Accounts.ToList(),
                    new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                Logger.Error("保存账号库失败", ex);
            }
        }
    }

    public static AccountRecord? Find(string username) =>
        Accounts.FirstOrDefault(a => a.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

    public static AccountRecord AddOrUpdate(string username, string? password, string status,
        long userId = 0, string? displayName = null)
    {
        var record = Find(username);

        if (record is null)
        {
            record = new AccountRecord { Username = username };
            Accounts.Add(record);
        }

        if (!string.IsNullOrEmpty(password))
            record.PasswordProtected = Dpapi.Protect(password);

        if (userId > 0) record.UserId = userId;
        if (!string.IsNullOrWhiteSpace(displayName)) record.DisplayName = displayName;

        record.Status = status;
        record.UpdatedAt = DateTimeOffset.Now;

        Save();
        return record;
    }

    public static void Remove(AccountRecord record)
    {
        Accounts.Remove(record);
        Save();
    }

    public static string? GetPassword(AccountRecord record)
    {
        if (!record.HasPassword) return null;

        try
        {
            return Dpapi.Unprotect(record.PasswordProtected);
        }
        catch (Exception ex)
        {
            Logger.Warn($"解密账号密码失败：{ex.Message}");
            return null;
        }
    }
}

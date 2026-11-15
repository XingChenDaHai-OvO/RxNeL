using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace RxNeL.Accounts;

/// <summary>用 Roblox 公开缩略图接口取头像（该接口不受地区限制）。</summary>
public static class AvatarClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static async Task<string?> GetAvatarUrlAsync(long userId, int size = 180)
    {
        if (userId <= 0) return null;

        try
        {
            var url = $"https://thumbnails.roblox.com/v1/users/avatar?userIds={userId}" +
                      $"&size={size}x{size}&format=Png&isCircular=false";

            var body = await Http.GetStringAsync(url);

            using var doc = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
                return null;

            var first = data[0];

            return first.TryGetProperty("imageUrl", out var imageUrl) ? imageUrl.GetString() : null;
        }
        catch (Exception ex)
        {
            Logger.Warn($"获取头像失败：{ex.Message}");
            return null;
        }
    }

    public static async Task<byte[]?> GetAvatarBytesAsync(long userId, int size = 180)
    {
        var url = await GetAvatarUrlAsync(userId, size);

        if (string.IsNullOrEmpty(url)) return null;

        try
        {
            return await Http.GetByteArrayAsync(url);
        }
        catch (Exception ex)
        {
            Logger.Warn($"下载头像失败：{ex.Message}");
            return null;
        }
    }
}

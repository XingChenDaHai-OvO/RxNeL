using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RxNeL.Social;

/// <summary>局域网里发现的一个启动器实例（好友）。</summary>
public sealed class PeerInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";

    /// <summary>对方聊天的 TCP 端口（每个实例随机分配，避免同机多开冲突）。</summary>
    public int ChatPort { get; set; }
    public bool RobloxRunning { get; set; }
    public string Version { get; set; } = "";

    /// <summary>对方注册的 Roblox 用户名 / ID / 头像。</summary>
    public string RobloxName { get; set; } = "";
    public long RobloxUserId { get; set; }
    public string AvatarUrl { get; set; } = "";
    public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.Now;

    [JsonIgnore]
    public bool Online => (DateTimeOffset.Now - LastSeen).TotalSeconds < 20;

    [JsonIgnore]
    public string PresenceText => Online
        ? (RobloxRunning ? "在线 · Roblox 运行中" : "在线 · 未开游戏")
        : "离线";
}

public enum ChatKind
{
    Text,
    Image
}

public sealed class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ConversationId { get; set; } = "";
    public string FromId { get; set; } = "";
    public string FromName { get; set; } = "";
    public ChatKind Kind { get; set; } = ChatKind.Text;
    public string Text { get; set; } = "";
    public string? ImageName { get; set; }
    public string? ImageBase64 { get; set; }
    public DateTimeOffset SentAt { get; set; } = DateTimeOffset.Now;

    [JsonIgnore]
    public bool IsImage => Kind == ChatKind.Image && !string.IsNullOrEmpty(ImageBase64);
}

public sealed class Conversation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public bool IsGroup { get; set; }
    public List<string> MemberIds { get; set; } = new();
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

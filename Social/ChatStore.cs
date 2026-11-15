using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RxNeL.Social;

/// <summary>会话与消息的本地存储（%LOCALAPPDATA%\RbxLite\social.json）。</summary>
public static class ChatStore
{
    private static readonly object Sync = new();

    public static List<Conversation> Conversations { get; } = new();
    public static List<ChatMessage> Messages { get; } = new();

    public static string FilePath => Path.Combine(Paths.AppDir, "social.json");

    private static bool _loaded;

    public static void Load()
    {
        lock (Sync)
        {
            if (_loaded) return;
            _loaded = true;

            try
            {
                if (!File.Exists(FilePath)) return;

                var doc = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(FilePath));

                if (doc is null) return;

                Conversations.AddRange(doc.Conversations);
                Messages.AddRange(doc.Messages);
            }
            catch (Exception ex)
            {
                Logger.Error("读取聊天记录失败", ex);
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

                var doc = new Snapshot { Conversations = Conversations, Messages = Messages };

                File.WriteAllText(FilePath, JsonSerializer.Serialize(doc,
                    new JsonSerializerOptions { WriteIndented = false }));
            }
            catch (Exception ex)
            {
                Logger.Error("保存聊天记录失败", ex);
            }
        }
    }

    public static Conversation GetOrCreateDirect(string peerId, string peerName)
    {
        Load();

        var conversation = Conversations.FirstOrDefault(c => !c.IsGroup && c.MemberIds.Contains(peerId));

        if (conversation is not null) return conversation;

        conversation = new Conversation
        {
            Id = "dm-" + peerId,
            Name = peerName,
            IsGroup = false,
            MemberIds = { peerId }
        };

        Conversations.Add(conversation);
        Save();

        return conversation;
    }

    public static Conversation CreateGroup(string name, IEnumerable<string> memberIds)
    {
        Load();

        var conversation = new Conversation
        {
            Id = "grp-" + Guid.NewGuid().ToString("N")[..8],
            Name = name,
            IsGroup = true,
            MemberIds = memberIds.ToList()
        };

        Conversations.Add(conversation);
        Save();

        return conversation;
    }

    public static void AddMessage(ChatMessage message)
    {
        Load();
        Messages.Add(message);

        var conversation = Conversations.FirstOrDefault(c => c.Id == message.ConversationId);

        if (conversation is not null)
            conversation.UpdatedAt = message.SentAt;

        Save();
    }

    public static List<ChatMessage> MessagesOf(string conversationId)
    {
        Load();
        return Messages.Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.SentAt)
            .ToList();
    }

    private sealed class Snapshot
    {
        public List<Conversation> Conversations { get; set; } = new();
        public List<ChatMessage> Messages { get; set; } = new();
    }
}

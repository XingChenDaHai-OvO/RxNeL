using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RxNeL.Social;

/// <summary>
/// 局域网在线状态：UDP 27100 广播心跳（每 3 秒），同时监听别人的心跳。
/// 心跳里带上「Roblox 是否在运行」，所以好友能看到你在启动器在线、以及在不在游戏里。
/// </summary>
public sealed class LanPresence : IDisposable
{
    public const int Port = 27100;

    private readonly object _sync = new();
    private readonly Dictionary<string, PeerInfo> _peers = new();
    private readonly CancellationTokenSource _cts = new();
    private UdpClient? _listener;
    private UdpClient? _broadcaster;

    public string SelfId { get; } = Guid.NewGuid().ToString("N");
    public string SelfName { get; set; } = Environment.MachineName;

    /// <summary>本机聊天端口，随心跳广播出去。</summary>
    public int ChatPort { get; set; }

    /// <summary>已注册时把自己的 Roblox 资料一起广播，好友列表直接显示官方昵称/头像。</summary>
    public string RobloxName { get; set; } = "";
    public long RobloxUserId { get; set; }
    public string AvatarUrl { get; set; } = "";

    public event Action? PeersChanged;

    public IReadOnlyList<PeerInfo> Peers
    {
        get
        {
            lock (_sync)
                return _peers.Values.OrderByDescending(p => p.Online).ThenBy(p => p.Name).ToList();
        }
    }

    public static bool RobloxRunning()
    {
        try
        {
            return System.Diagnostics.Process.GetProcessesByName("RobloxPlayerBeta").Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public void Start()
    {
        try
        {
            _listener = new UdpClient { EnableBroadcast = true };
            _listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Client.Bind(new IPEndPoint(IPAddress.Any, Port));

            _broadcaster = new UdpClient { EnableBroadcast = true };

            _ = Task.Run(ListenLoopAsync);
            _ = Task.Run(BroadcastLoopAsync);

            Logger.Info($"[社交] 在线状态已启动（端口 {Port}，本机 {SelfName}）");
        }
        catch (Exception ex)
        {
            Logger.Warn($"[社交] 启动在线状态失败：{ex.Message}");
        }
    }

    private async Task ListenLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var result = await _listener!.ReceiveAsync(_cts.Token);
                var json = Encoding.UTF8.GetString(result.Buffer);
                var beat = JsonSerializer.Deserialize<Heartbeat>(json);

                if (beat is null || beat.Id == SelfId) continue;

                var changed = false;

                lock (_sync)
                {
                    if (!_peers.TryGetValue(beat.Id, out var peer))
                    {
                        peer = new PeerInfo { Id = beat.Id };
                        _peers[beat.Id] = peer;
                        changed = true;
                    }

                    if (peer.Name != beat.Name || peer.RobloxRunning != beat.Roblox || !peer.Online)
                        changed = true;

                    peer.Name = beat.Name;
                    peer.Address = result.RemoteEndPoint.Address.ToString();
                    peer.RobloxRunning = beat.Roblox;
                    peer.Version = beat.Version ?? "";
                    peer.ChatPort = beat.ChatPort;
                    peer.RobloxName = beat.RobloxName ?? "";
                    peer.RobloxUserId = beat.RobloxUserId;
                    peer.AvatarUrl = beat.AvatarUrl ?? "";
                    peer.LastSeen = DateTimeOffset.Now;
                }

                if (changed) Logger.Info($"[社交] 发现/更新好友：{beat.Name}（{result.RemoteEndPoint.Address}）");

                if (changed) PeersChanged?.Invoke();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[社交] 接收心跳失败：{ex.Message}");
                await Task.Delay(1000);
            }
        }
    }

    private async Task BroadcastLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var beat = new Heartbeat
                {
                    Id = SelfId,
                    Name = SelfName,
                    Roblox = RobloxRunning(),
                    Version = AppInfo.VersionLabel,
                    ChatPort = ChatPort,
                    RobloxName = RobloxName,
                    RobloxUserId = RobloxUserId,
                    AvatarUrl = AvatarUrl
                };

                var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(beat));

                // 同时发广播和回环：同机多开也能互相发现，跨机靠广播
                try { await _broadcaster!.SendAsync(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, Port)); }
                catch { /* 某些网络禁广播 */ }

                try { await _broadcaster!.SendAsync(bytes, bytes.Length, new IPEndPoint(IPAddress.Loopback, Port)); }
                catch { /* 忽略 */ }

                Prune();
            }
            catch (Exception ex)
            {
                Logger.Warn($"[社交] 发送心跳失败：{ex.Message}");
            }

            await Task.Delay(3000);
        }
    }

    private void Prune()
    {
        var changed = false;

        lock (_sync)
        {
            foreach (var peer in _peers.Values.Where(p => !p.Online).ToList())
            {
                if ((DateTimeOffset.Now - peer.LastSeen).TotalSeconds > 120)
                {
                    _peers.Remove(peer.Id);
                    changed = true;
                }
            }
        }

        if (changed) PeersChanged?.Invoke();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener?.Dispose();
        _broadcaster?.Dispose();
    }

    private sealed class Heartbeat
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public bool Roblox { get; set; }
        public string? Version { get; set; }
        public int ChatPort { get; set; }
        public string? RobloxName { get; set; }
        public long RobloxUserId { get; set; }
        public string? AvatarUrl { get; set; }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RxNeL.Social;

/// <summary>
/// 局域网聊天：TCP 27101，长度前缀 + JSON 信封。
/// 群聊没有服务器，用的是「群成员点对点扇出」：消息发给每个在线成员，各自落本地库。
/// </summary>
public sealed class ChatService : IDisposable
{
    private const int MaxPayload = 8 * 1024 * 1024;

    /// <summary>实际监听端口（启动后由系统分配）。</summary>
    public int Port { get; private set; }

    private readonly CancellationTokenSource _cts = new();
    private TcpListener? _listener;

    public string SelfId { get; set; } = "";
    public string SelfName { get; set; } = Environment.MachineName;

    public event Action<ChatMessage>? MessageReceived;

    public void Start()
    {
        try
        {
            _listener = new TcpListener(IPAddress.Any, 0);
            _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Start();

            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

            _ = Task.Run(AcceptLoopAsync);

            Logger.Info($"[社交] 聊天服务已监听 TCP {Port}");
        }
        catch (Exception ex)
        {
            Logger.Warn($"[社交] 聊天服务启动失败：{ex.Message}");
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(_cts.Token);

                _ = Task.Run(() => HandleClientAsync(client));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[社交] 接收连接失败：{ex.Message}");
                await Task.Delay(500);
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        try
        {
            using (client)
            using (var stream = client.GetStream())
            {
                var header = new byte[4];

                if (!await ReadExactAsync(stream, header)) return;

                var length = BitConverter.ToInt32(header, 0);

                if (length <= 0 || length > MaxPayload) return;

                var payload = new byte[length];

                if (!await ReadExactAsync(stream, payload)) return;

                var message = JsonSerializer.Deserialize<ChatMessage>(Encoding.UTF8.GetString(payload));

                if (message is null) return;

                MessageReceived?.Invoke(message);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"[社交] 处理消息失败：{ex.Message}");
        }
    }

    private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer)
    {
        var offset = 0;

        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset));

            if (read <= 0) return false;

            offset += read;
        }

        return true;
    }

    public async Task<int> SendAsync(IEnumerable<(string Address, int Port)> targets, ChatMessage message)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        var sent = 0;

        foreach (var (address, port) in targets.Distinct())
        {
            try
            {
                using var client = new TcpClient();

                var connect = client.ConnectAsync(IPAddress.Parse(address), port <= 0 ? 27101 : port);

                if (await Task.WhenAny(connect, Task.Delay(2500)) != connect)
                    continue;

                using var stream = client.GetStream();

                var header = BitConverter.GetBytes(bytes.Length);
                await stream.WriteAsync(header);
                await stream.WriteAsync(bytes);

                sent++;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[社交] 发送到 {address} 失败：{ex.Message}");
            }
        }

        return sent;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener?.Stop();
    }
}

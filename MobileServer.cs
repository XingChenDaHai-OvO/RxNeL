using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RxNeL.Accounts;
using RxNeL.Social;

namespace RxNeL;

/// <summary>
/// 手机遥控：局域网内用手机浏览器（或"添加到主屏幕"）看状态、启动 Roblox。
/// 用 TcpListener 手写极简 HTTP，避免 HttpListener 需要 URL ACL / 管理员权限。
/// </summary>
public sealed class MobileServer : IDisposable
{
    public const int Port = 27150;

    private readonly CancellationTokenSource _cts = new();
    private TcpListener? _listener;

    public string PairCode { get; } = Random.Shared.Next(100000, 999999).ToString();

    public static string Url => $"http://{LocalAddress()}:{Port}/";

    public void Start()
    {
        try
        {
            _listener = new TcpListener(IPAddress.Any, Port);
            _listener.Start();

            _ = Task.Run(LoopAsync);

            Logger.Info($"[手机] 遥控已启动：{Url}（配对码 {PairCode}）");
        }
        catch (Exception ex)
        {
            Logger.Warn($"[手机] 启动失败（端口被占用？）：{ex.Message}");
        }
    }

    private async Task LoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;

            try
            {
                client = await _listener!.AcceptTcpClientAsync(_cts.Token);
            }
            catch
            {
                return;
            }

            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var (method, path, headers) = await ReadAsync(stream);

                if (string.IsNullOrEmpty(path)) return;

                switch (path)
                {
                    case "/":
                    case "/index.html":
                        await WriteAsync(stream, 200, "text/html; charset=utf-8", Page);
                        return;

                    case "/manifest.webmanifest":
                        await WriteAsync(stream, 200, "application/manifest+json", JsonSerializer.Serialize(new
                        {
                            name = "Rx | NeL",
                            short_name = "RxNeL",
                            start_url = "/",
                            display = "standalone",
                            background_color = "#070b0c",
                            theme_color = "#35e0a1"
                        }));
                        return;

                    case "/sw.js":
                        await WriteAsync(stream, 200, "application/javascript",
                            "self.addEventListener('install',e=>self.skipWaiting());self.addEventListener('fetch',()=>{});");
                        return;

                    case "/api/status":
                    {
                        var version = Runtime.InstalledVersion ?? Deployment.GetNewestInstalledVersion();
                        AccountVault.Load();

                        await WriteAsync(stream, 200, "application/json; charset=utf-8", JsonSerializer.Serialize(new
                        {
                            ok = true,
                            version,
                            robloxRunning = LanPresence.RobloxRunning(),
                            multiInstance = AppSettings.Current.MultiInstance,
                            accounts = AccountVault.Accounts.Select(a => new
                            {
                                username = a.Username,
                                display = a.DisplayName,
                                id = a.UserId
                            })
                        }));
                        return;
                    }

                    case "/api/launch" when method == "POST":
                    {
                        headers.TryGetValue("x-pair-code", out var code);

                        if (!string.Equals(code, PairCode, StringComparison.Ordinal))
                        {
                            await WriteAsync(stream, 401, "application/json; charset=utf-8",
                                "{\"ok\":false,\"error\":\"配对码不对\"}");
                            return;
                        }

                        var message = await Task.Run(() =>
                        {
                            try
                            {
                                var version = Runtime.InstalledVersion ?? Deployment.GetNewestInstalledVersion();

                                if (version is null) return "没有安装客户端";

                                if (AppSettings.Current.MultiInstance)
                                    SingletonBypass.ReleaseForRunningClients();

                                ClientPrep.Prepare(version, AppSettings.Current);
                                LaunchHandler.Start(version, LaunchHandler.CreateAppTarget(AppSettings.Current), AppSettings.Current);

                                return "已启动";
                            }
                            catch (Exception ex)
                            {
                                return $"启动失败：{ex.Message}";
                            }
                        });

                        await WriteAsync(stream, 200, "application/json; charset=utf-8",
                            JsonSerializer.Serialize(new { ok = message == "已启动", message }));
                        return;
                    }
                }

                await WriteAsync(stream, 404, "application/json; charset=utf-8", "{\"ok\":false,\"error\":\"not found\"}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"[手机] 请求处理失败：{ex.Message}");
            }
        }
    }

    private static async Task<(string Method, string Path, Dictionary<string, string> Headers)> ReadAsync(Stream stream)
    {
        var buffer = new byte[8192];
        var read = await stream.ReadAsync(buffer);

        if (read <= 0) return ("", "", new Dictionary<string, string>());

        var text = Encoding.UTF8.GetString(buffer, 0, read);
        var lines = text.Split("\r\n");
        var first = lines[0].Split(' ');

        var method = first.Length > 0 ? first[0] : "GET";
        var path = (first.Length > 1 ? first[1] : "/").Split('?')[0];

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) break;

            var idx = lines[i].IndexOf(':');

            if (idx > 0)
                headers[lines[i][..idx].Trim()] = lines[i][(idx + 1)..].Trim();
        }

        return (method, path, headers);
    }

    private static Task WriteAsync(Stream stream, int status, string contentType, string body)
        => WriteAsync(stream, status, contentType, Encoding.UTF8.GetBytes(body));

    private static async Task WriteAsync(Stream stream, int status, string contentType, byte[] body)
    {
        var reason = status switch { 200 => "OK", 401 => "Unauthorized", 404 => "Not Found", _ => "OK" };

        var header = $"HTTP/1.1 {status} {reason}\r\n" +
                     $"Content-Type: {contentType}\r\n" +
                     $"Content-Length: {body.Length}\r\n" +
                     "Access-Control-Allow-Origin: *\r\n" +
                     "Connection: close\r\n\r\n";

        await stream.WriteAsync(Encoding.UTF8.GetBytes(header));

        if (body.Length > 0) await stream.WriteAsync(body);

        await stream.FlushAsync();
    }

    public static string LocalAddress()
    {
        try
        {
            return Dns.GetHostAddresses(Dns.GetHostName())
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                ?.ToString() ?? "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }

    public void Dispose()
    {
        _cts.Cancel();

        try { _listener?.Stop(); } catch { }
    }

    private const string Page = """
<!doctype html>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
<meta name="theme-color" content="#35e0a1">
<meta name="mobile-web-app-capable" content="yes">
<link rel="manifest" href="/manifest.webmanifest">
<title>Rx | NeL</title>
<style>
  :root{--bg:#070b0c;--card:#0c1214;--alt:#111a1c;--line:#1c2a2a;--text:#e4f2ec;--muted:#6e8880;--accent:#35e0a1}
  *{box-sizing:border-box}
  body{margin:0 auto;background:var(--bg);color:var(--text);max-width:560px;padding:20px 16px 40px;
       font:15px/1.6 -apple-system,"Segoe UI",system-ui,"Microsoft YaHei",sans-serif;display:flex;flex-direction:column;gap:14px}
  h1{font-size:18px;margin:0}
  .muted{color:var(--muted);font-size:13px}
  .card{background:var(--card);border:1px solid var(--line);border-radius:14px;padding:16px}
  .row{display:flex;justify-content:space-between;padding:6px 0}
  .dot{display:inline-block;width:8px;height:8px;border-radius:50%;background:var(--muted);margin-right:8px}
  .dot.on{background:var(--accent)}
  button{width:100%;border:0;border-radius:12px;padding:14px;font-size:16px;font-weight:600;color:#04150f;background:var(--accent)}
  button:disabled{opacity:.5}
  input{width:100%;background:var(--alt);border:1px solid var(--line);border-radius:10px;color:var(--text);padding:12px;font-size:15px}
  .acc{display:flex;justify-content:space-between;padding:10px 0;border-top:1px solid var(--line);font-size:14px}
  .acc:first-child{border-top:0}
</style>
<h1>Rx | NeL</h1>
<div class="muted" id="host">连接中…</div>

<div class="card">
  <div class="row"><span><span class="dot" id="dot"></span>客户端</span><span id="ver">—</span></div>
  <div class="row"><span>Roblox</span><span id="rbx">—</span></div>
  <div class="row"><span>多开</span><span id="multi">—</span></div>
</div>

<div class="card">
  <div class="muted" style="margin-bottom:8px">配对码（电脑：设置 → 通用 → 手机遥控）</div>
  <input id="code" inputmode="numeric" placeholder="6 位数字">
</div>

<button id="go">启动 Roblox</button>

<div class="card">
  <div class="muted" style="margin-bottom:6px">账号</div>
  <div id="accs"><div class="muted">—</div></div>
</div>

<script>
const codeEl = document.getElementById('code');
codeEl.value = localStorage.getItem('pair') || '';
codeEl.oninput = () => localStorage.setItem('pair', codeEl.value);

async function refresh() {
  try {
    const d = await (await fetch('/api/status')).json();
    document.getElementById('host').textContent = location.host;
    document.getElementById('ver').textContent = d.version || '未安装';
    document.getElementById('rbx').textContent = d.robloxRunning ? '运行中' : '未运行';
    document.getElementById('multi').textContent = d.multiInstance ? '开' : '关';
    document.getElementById('dot').className = 'dot' + (d.version ? ' on' : '');
    document.getElementById('accs').innerHTML = (d.accounts || []).map(a =>
      '<div class="acc"><span>' + a.username + '</span><span class="muted">' + (a.display || '') + '</span></div>'
    ).join('') || '<div class="muted">暂无账号</div>';
  } catch (e) {
    document.getElementById('host').textContent = '连不上电脑（同一 WiFi？）';
  }
}

document.getElementById('go').onclick = async () => {
  const btn = document.getElementById('go');
  btn.disabled = true; btn.textContent = '启动中…';
  try {
    const d = await (await fetch('/api/launch', { method: 'POST', headers: { 'X-Pair-Code': codeEl.value } })).json();
    btn.textContent = d.message || (d.ok ? '已启动' : '失败');
  } catch { btn.textContent = '请求失败'; }
  setTimeout(() => { btn.disabled = false; btn.textContent = '启动 Roblox'; refresh(); }, 2500);
};

refresh();
setInterval(refresh, 5000);
if ('serviceWorker' in navigator) navigator.serviceWorker.register('/sw.js').catch(() => {});
</script>
""";
}

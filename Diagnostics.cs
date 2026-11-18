using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RxNeL;

/// <summary>
/// 网络 / 启动诊断：解析最近的 Roblox 日志（数据中心、服务器 IP），实测到该 IP 的延迟，
/// 并列出客户端里实际生效的配置，用来判断「延迟变高」到底出在哪一层。
/// </summary>
public static class Diagnostics
{
    public static async Task<string> NetDiagAsync()
    {
        var report = new StringBuilder();

        report.AppendLine("== 客户端 ==");
        report.AppendLine($"版本目录：{Runtime.InstalledVersion ?? Deployment.GetNewestInstalledVersion() ?? "(未安装)"}");

        var flagsPath = DescribeFlags(out var flagsText);
        report.AppendLine($"FastFlags：{(flagsPath is null ? "(没有写入文件)" : flagsText)}");

        report.AppendLine();
        report.AppendLine("== 最近一次会话 ==");

        var log = NewestPlayerLog();

        if (log is null)
        {
            report.AppendLine("没有找到 Roblox 日志");
            return report.ToString();
        }

        report.AppendLine($"日志：{Path.GetFileName(log)}");

        string text;

        try { text = await File.ReadAllTextAsync(log); }
        catch (Exception ex) { return report.AppendLine($"读取日志失败：{ex.Message}").ToString(); }

        var datacenter = Regex.Match(text, @"DatacenterId=(\d+)");
        var server = Regex.Match(text, @"serverId:\s*([\d\.]+)\|(\d+)");
        var udmux = Regex.Match(text, @"UDMUX Address = ([\d\.]+)");
        var transport = Regex.Match(text, @"selectedTransport=(\w+)");

        if (datacenter.Success) report.AppendLine($"数据中心：{datacenter.Groups[1].Value}");
        if (transport.Success) report.AppendLine($"传输方式：{transport.Groups[1].Value}");

        var ip = server.Success ? server.Groups[1].Value : (udmux.Success ? udmux.Groups[1].Value : null);

        if (ip is null)
        {
            report.AppendLine("日志里没有服务器 IP（可能没进过游戏）");
            return report.ToString();
        }

        report.AppendLine($"服务器：{ip}{(server.Success ? ":" + server.Groups[2].Value : "")}");

        report.AppendLine();
        report.AppendLine("== 实测延迟（到该服务器）==");

        var port = server.Success && int.TryParse(server.Groups[2].Value, out var p) ? p : 443;

        for (var i = 0; i < 3; i++)
        {
            var icmp = await IcmpAsync(ip);

            if (icmp is not null)
            {
                report.AppendLine($"第 {i + 1} 次：ICMP {icmp} ms");
                continue;
            }

            // Roblox 的 UDMUX 通常不回 ICMP，退回 TCP 握手测 RTT
            var tcp = await TcpRttAsync(ip, port);

            report.AppendLine(tcp is null
                ? $"第 {i + 1} 次：ICMP 与 TCP:{port} 都超时（该 IP 可能只收 UDP）"
                : $"第 {i + 1} 次：TCP:{port} {tcp} ms（ICMP 不可用）");
        }

        report.AppendLine();
        report.AppendLine("== 启动链路 ==");
        report.AppendLine($"参数模板：{(AppSettings.Current.UseLegacyArgs ? "兼容模式（-app -t --placelauncherurl）" : AppSettings.Current.LaunchArgsTemplate)}");
        report.AppendLine($"多开：{(AppSettings.Current.MultiInstance ? "开" : "关")}　代理：{(string.IsNullOrWhiteSpace(AppSettings.Current.ProxyUrl) ? "无" : AppSettings.Current.ProxyUrl)}");
        report.AppendLine($"干净启动：{(AppSettings.Current.CleanLaunch ? "开（不写任何客户端配置）" : "关")}");

        return report.ToString();
    }


    private static async Task<long?> IcmpAsync(string ip)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ip, 2000);

            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<long?> TcpRttAsync(string ip, int port)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var task = client.ConnectAsync(ip, port);

            var completed = await Task.WhenAny(task, Task.Delay(2500));

            if (completed != task || !client.Connected) return null;

            return watch.ElapsedMilliseconds;
        }
        catch
        {
            return null;
        }
    }

    private static string? DescribeFlags(out string text)
    {
        text = "";

        var version = Runtime.InstalledVersion ?? Deployment.GetNewestInstalledVersion();

        if (version is null) return null;

        var path = Path.Combine(Paths.VersionDir(version), "ClientSettings", "ClientAppSettings.json");

        if (!File.Exists(path)) return null;

        try
        {
            text = File.ReadAllText(path).Trim();

            if (text.Length > 200) text = text[..200] + "…";

            return path;
        }
        catch
        {
            return null;
        }
    }

    private static string? NewestPlayerLog()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "logs");

            if (!Directory.Exists(dir)) return null;

            return Directory.GetFiles(dir, "*Player*_last.log")
                .Where(f => !f.Contains("Crash", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }
}

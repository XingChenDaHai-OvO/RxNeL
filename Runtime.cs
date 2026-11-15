using System;
using System.Threading.Tasks;

namespace RxNeL;

/// <summary>界面间共享的运行时状态。</summary>
public static class Runtime
{
    public static string Channel { get; set; } = "production";
    public static string? InstalledVersion { get; set; }
    public static string? LatestVersionId { get; set; }
    public static string? LatestVersion { get; set; }

    public static event Action<string>? StatusChanged;

    public static void Status(string text) => StatusChanged?.Invoke(text);
}

/// <summary>把「检查/安装/启动」这套流程从界面里抽出来，供各个视图复用。</summary>
public static class ClientManager
{
    public static async Task<string?> EnsureClientAsync(bool force, IProgress<double>? progress = null)
    {
        Runtime.Status("正在检查版本…");

        var info = await Deployment.GetPlayerVersionAsync(Runtime.Channel);
        Runtime.LatestVersionId = info.VersionId;
        Runtime.LatestVersion = info.Version ?? info.VersionId;

        var version = info.VersionId;

        if (!force && Deployment.IsComplete(version))
        {
            Runtime.InstalledVersion = version;
            Runtime.Status($"客户端就绪：{version}");
            return version;
        }

        Runtime.Status($"正在安装 {version}…");
        await Deployment.InstallAsync(version, progress);

        Runtime.InstalledVersion = version;
        Runtime.Status($"客户端就绪：{version}");
        return version;
    }

    public static int Start(LaunchTarget target)
    {
        var version = Runtime.InstalledVersion ?? Deployment.GetNewestInstalledVersion()
            ?? throw new InvalidOperationException("尚未安装客户端");

        if (AppSettings.Current.MultiInstance)
        {
            var bypass = SingletonBypass.ReleaseForRunningClients();
            Logger.Info($"多开：{bypass.Message}");
        }

        ClientPrep.Prepare(version, AppSettings.Current);

        var process = LaunchHandler.Start(version, target, AppSettings.Current);
        Runtime.Status("Roblox 已启动");
        return process.Id;
    }
}

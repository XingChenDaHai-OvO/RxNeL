using System;
using System.Threading.Tasks;
using System.Windows;

namespace RxNeL;

public enum LaunchOutcome
{
    Ok,
    Failed
}

/// <summary>
/// 启动流程：连接 → 检查版本 →（必要时）下载 → 应用配置 → 启动客户端。
/// 全程由一个类似 Bloxstrap bootstrapper 的小进度窗驱动。
/// </summary>
public static class LaunchFlow
{
    public static async Task<LaunchOutcome> RunAsync(
        Window? owner,
        LaunchTarget? target,
        bool forceInstall = false)
    {
        var dialog = new LaunchWindow();

        if (owner is not null)
            dialog.Owner = owner;

        dialog.Show();

        try
        {
            dialog.SetIndeterminate("正在连接 Roblox…", Runtime.Channel);

            var info = await Deployment.GetPlayerVersionAsync(Runtime.Channel);

            Runtime.LatestVersionId = info.VersionId;
            Runtime.LatestVersion = info.Version ?? info.VersionId;

            var version = info.VersionId;

            dialog.SetIndeterminate($"检查客户端 {version}…", "读取部署信息");

            if (forceInstall || !Deployment.IsComplete(version))
            {
                dialog.StopIndeterminate();
                dialog.SetStatus("正在下载客户端… 0%", 0.12, version);

                var progress = new Progress<double>(p =>
                {
                    var percent = p * 100;
                    dialog.SetStatus($"正在下载客户端… {percent:0}%", 0.12 + p * 0.68, version);
                });

                await Deployment.InstallAsync(version, progress);
            }

            Runtime.InstalledVersion = version;

            dialog.StopIndeterminate();
            dialog.SetStatus("应用配置（FastFlags / 模组 / 字体）…", 0.84, version);

            await Task.Run(() => ClientPrep.Prepare(version, AppSettings.Current));

            if (target is not null)
            {
                if (AppSettings.Current.MultiInstance)
                {
                    var bypass = SingletonBypass.ReleaseForRunningClients();
                    Logger.Info($"多开：{bypass.Message}");
                }

                dialog.SetStatus("启动客户端…", 0.95, version);

                var process = LaunchHandler.Start(version, target, AppSettings.Current);
                Logger.Info($"Roblox 已启动，PID={process.Id}");
            }

            dialog.SetStatus(target is null ? "客户端已就绪" : "Roblox 已启动", 1, version);

            await Task.Delay(400);

            Runtime.Status(target is null ? $"客户端就绪：{version}" : "Roblox 已启动");
            return LaunchOutcome.Ok;
        }
        catch (Exception ex)
        {
            Logger.Error("启动流程失败", ex);
            dialog.Fail($"失败：{ex.Message}");

            await Task.Delay(1500);
            return LaunchOutcome.Failed;
        }
        finally
        {
            dialog.Close();
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace RxNeL.Views;

public partial class ModsView : UserControl
{
    public ModsView()
    {
        InitializeComponent();
        Refresh();
    }

    private void Refresh()
    {
        var mods = ModManager.ListMods();
        var lines = new System.Text.StringBuilder();

        lines.AppendLine($"模组目录：{Paths.ModsDir}");
        lines.AppendLine();

        if (mods.Count == 0)
        {
            lines.AppendLine("（空）点「打开模组目录」会建立 content\\textures、content\\sounds、content\\fonts 骨架并附说明文件。");
        }
        else
        {
            lines.AppendLine($"共 {mods.Count} 个文件：");

            foreach (var mod in mods)
                lines.AppendLine("  " + mod);
        }

        if (Runtime.InstalledVersion is { } version)
        {
            lines.AppendLine();
            lines.AppendLine($"目标客户端：{version}");
        }

        ListBox.Text = lines.ToString();
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        ModManager.CreateSampleStructure();
        OpenPath(Paths.ModsDir);
        Refresh();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var version = Runtime.InstalledVersion ?? Deployment.GetNewestInstalledVersion();

        if (version is null)
        {
            AppDialog.Info(Window.GetWindow(this), "内容", "尚未安装客户端，无法应用模组。");
            return;
        }

        var result = ModManager.Apply(version);
        Runtime.Status(result.Message);
        Refresh();

        if (result.Applied == 0)
            AppDialog.Info(Window.GetWindow(this), "应用模组", result.Message);
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        var version = Runtime.InstalledVersion ?? Deployment.GetNewestInstalledVersion();

        if (version is null) return;

        var result = ModManager.Restore(version);
        Runtime.Status(result.Message);
        Refresh();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (!AppDialog.Confirm(Window.GetWindow(this), "清空模组目录",
                "删除模组目录里的所有文件？已覆盖到客户端的文件请先点「还原原始文件」。"))

        try
        {
            if (Directory.Exists(Paths.ModsDir))
                Directory.Delete(Paths.ModsDir, recursive: true);

            Directory.CreateDirectory(Paths.ModsDir);
            Refresh();
            Runtime.Status("模组目录已清空");
        }
        catch (Exception ex)
        {
            Logger.Error("清空模组目录失败", ex);
        }
    }

    private static void OpenPath(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Error($"打开路径失败：{path}", ex);
        }
    }
}

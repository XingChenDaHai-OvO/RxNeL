using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace RxNeL.Views;

public partial class AppearanceView : UserControl
{
    private bool _loading;

    public AppearanceView()
    {
        InitializeComponent();

        _loading = true;
        AllFamiliesCheck.IsChecked = AppSettings.Current.FontModAllFamilies;
        GpuCheck.IsChecked = AppSettings.Current.PreferDedicatedGpu;
        _loading = false;

        GpuCheck.Checked += (_, _) => ApplyGpu(true);
        GpuCheck.Unchecked += (_, _) => ApplyGpu(false);

        RefreshFonts();
    }

    private string? Version()
    {
        var version = Runtime.InstalledVersion ?? Deployment.GetNewestInstalledVersion();
        Runtime.InstalledVersion ??= version;
        return version;
    }

    private void RefreshFonts()
    {
        var version = Version();

        FontBox.Items.Clear();

        if (version is null)
        {
            FontStatus.Text = "尚未安装客户端：先到「概览」页检查更新";
            return;
        }

        var fonts = FontModManager.ListFontFiles(version);

        _loading = true;

        foreach (var font in fonts)
            FontBox.Items.Add(font);

        if (FontBox.Items.Count > 0)
            FontBox.SelectedIndex = 0;

        _loading = false;

        FontStatus.Text = FontModManager.IsModified(version)
            ? $"当前客户端字体已被替换（{fonts.Count} 个可选字体，可随时还原）"
            : $"共 {fonts.Count} 个可选字体";
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshFonts();

    private void FontBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;

        var version = Version();
        if (version is null || FontBox.SelectedItem is not string font) return;

        var path = Path.Combine(FontModManager.FontsDir(version), font);

        try
        {
            if (!File.Exists(path)) return;

            var glyph = new GlyphTypeface(new Uri(path));
            var familyName = glyph.FamilyNames.Values.FirstOrDefault();

            PreviewText.FontFamily = familyName is null
                ? new FontFamily("Segoe UI")
                : new FontFamily(new Uri(path), $"./#{familyName}");
        }
        catch (Exception ex)
        {
            Logger.Warn($"字体预览失败：{ex.Message}");
            PreviewText.FontFamily = new FontFamily("Segoe UI");
        }
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var version = Version();

        if (version is null || FontBox.SelectedItem is not string font)
        {
            AppDialog.Info(Window.GetWindow(this), "美化", "先安装客户端并选一个字体。");
            return;
        }

        var result = FontModManager.ApplyExistingFont(version, font, AllFamiliesCheck.IsChecked == true);

        var settings = AppSettings.Current;
        settings.FontModEnabled = result.Success;
        settings.FontModFile = font;
        settings.FontModCustom = false;
        settings.FontModAllFamilies = AllFamiliesCheck.IsChecked == true;
        settings.Save();

        FontStatus.Text = result.Message;
        Runtime.Status(result.Message);
    }

    private void Pick_Click(object sender, RoutedEventArgs e)
    {
        var version = Version();

        if (version is null) return;

        var dialog = new OpenFileDialog { Title = "选择字体文件", Filter = "字体 (*.ttf;*.otf)|*.ttf;*.otf|所有文件|*.*" };

        if (dialog.ShowDialog() != true) return;

        try
        {
            Directory.CreateDirectory(Paths.AppDir);
            File.Copy(dialog.FileName, Paths.CustomFontFile, overwrite: true);
        }
        catch (Exception ex)
        {
            Logger.Error("复制字体文件失败", ex);
            AppDialog.Error(Window.GetWindow(this), "美化", ex.Message);
            return;
        }

        var result = FontModManager.ApplyCustomFont(version, Paths.CustomFontFile, AllFamiliesCheck.IsChecked == true);

        var settings = AppSettings.Current;
        settings.FontModEnabled = result.Success;
        settings.FontModFile = FontModManager.CustomFontFile;
        settings.FontModCustom = true;
        settings.FontModAllFamilies = AllFamiliesCheck.IsChecked == true;
        settings.Save();

        FontStatus.Text = result.Message;
        Runtime.Status(result.Message);
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        var version = Version();
        if (version is null) return;

        var result = FontModManager.Restore(version);

        var settings = AppSettings.Current;
        settings.FontModEnabled = false;
        settings.FontModCustom = false;
        settings.Save();

        RefreshFonts();
        FontStatus.Text = result.Message;
        Runtime.Status(result.Message);
    }

    private void ApplyGpu(bool enabled)
    {
        if (_loading) return;

        var applied = GpuPreference.ApplyToInstalledVersions(enabled);

        AppSettings.Current.PreferDedicatedGpu = enabled;
        AppSettings.Current.Save();

        GpuStatus.Text = applied > 0
            ? $"已为 {applied} 个客户端写入显卡偏好，下次启动生效"
            : "尚未安装客户端；安装后启动时会自动写入";
    }
}

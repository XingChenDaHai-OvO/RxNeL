using System;
using System.Windows;
using Microsoft.Win32;

namespace RxNeL;

public enum ThemeMode
{
    System,
    Light,
    Dark
}

/// <summary>WPF 版主题切换：整体替换第一套合并字典（颜色），样式引用 DynamicResource 自动跟随。</summary>
public static class ThemeManager
{
    public static ThemeMode Mode { get; private set; } = ThemeMode.System;
    public static bool IsDark { get; private set; }

    public static event Action? Changed;

    public static ThemeMode ParseMode(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "dark" => ThemeMode.Dark,
        "light" => ThemeMode.Light,
        _ => ThemeMode.System
    };

    public static string ModeToSetting(ThemeMode mode) => mode switch
    {
        ThemeMode.Dark => "dark",
        ThemeMode.Light => "light",
        _ => "system"
    };

    public static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    public static void Initialize(ThemeMode mode) => SetMode(mode);

    public static void SetMode(ThemeMode mode)
    {
        Mode = mode;
        IsDark = mode switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => SystemPrefersDark()
        };

        var app = Application.Current;
        if (app is not null)
        {
            var dictionary = new ResourceDictionary
            {
                Source = new Uri(IsDark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative)
            };

            var merged = app.Resources.MergedDictionaries;

            if (merged.Count > 0)
                merged[0] = dictionary;
            else
                merged.Add(dictionary);
        }

        Changed?.Invoke();
    }
}

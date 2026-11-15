using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace RxNeL.Views;

/// <summary>
/// 「设置」总入口：把引擎（FastFlags）、内容（模组）、美化（字体/显卡）、通用、日志
/// 全部收进来，用左侧二级导航切换。主界面只保留「概览」和「设置」两项。
/// </summary>
public partial class SettingsHubView : UserControl
{
    private static readonly (string Key, string Title)[] Sections =
    {
        ("general", "通用"),
        ("engine", "引擎"),
        ("content", "内容"),
        ("appearance", "美化"),
        ("log", "日志")
    };

    private readonly Dictionary<int, UserControl> _views = new();
    private readonly List<RadioButton> _buttons = new();

    private int _active = -1;

    public SettingsHubView()
    {
        InitializeComponent();
        BuildNav();
        SelectSection(0);
    }

    public int SectionCount => Sections.Length;

    public string SectionTitleAt(int index) =>
        index >= 0 && index < Sections.Length ? Sections[index].Title : "?";

    private void BuildNav()
    {
        for (var i = 0; i < Sections.Length; i++)
        {
            var button = new RadioButton
            {
                Content = Sections[i].Title,
                Style = (Style)FindResource("NavButton"),
                GroupName = "settings-nav",
                Height = 34,
                Margin = new Thickness(2, 1, 2, 1)
            };

            var index = i;
            button.Checked += (_, _) => SelectSection(index);

            SectionNav.Children.Add(button);
            _buttons.Add(button);
        }
    }

    private UserControl EnsureSection(int index)
    {
        if (_views.TryGetValue(index, out var existing)) return existing;

        UserControl view = index switch
        {
            0 => new SettingsView(),
            1 => new FlagsView(),
            2 => new ModsView(),
            3 => new AppearanceView(),
            _ => new LogView()
        };

        _views[index] = view;
        return view;
    }

    public void SelectSection(int index)
    {
        if (index < 0 || index >= Sections.Length) return;
        if (index == _active) return;

        SectionHost.Content = EnsureSection(index);

        if (index < _buttons.Count)
            _buttons[index].IsChecked = true;

        _active = index;
    }

    public void SelectSectionForTest(string key)
    {
        for (var i = 0; i < Sections.Length; i++)
        {
            if (Sections[i].Key != key) continue;
            SelectSection(i);
            return;
        }
    }
}

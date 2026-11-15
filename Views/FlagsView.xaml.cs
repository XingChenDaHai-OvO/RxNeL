using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace RxNeL.Views;

public sealed class FlagRow
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

public partial class FlagsView : UserControl
{
    private readonly ObservableCollection<FlagRow> _rows = new();

    public FlagsView()
    {
        InitializeComponent();

        Grid.ItemsSource = _rows;
        Grid.RowHeight = 30;
        Grid.ColumnHeaderHeight = 32;
        Grid.ColumnWidth = new DataGridLength(1, DataGridLengthUnitType.Star);

        Grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Flag 名称",
            Binding = new System.Windows.Data.Binding("Key"),
            Width = new DataGridLength(340)
        });

        Grid.Columns.Add(new DataGridTextColumn
        {
            Header = "值（true / false / 数字 / 字符串）",
            Binding = new System.Windows.Data.Binding("Value"),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });

        foreach (var preset in FastFlagPresets.All)
            PresetBox.Items.Add(preset.Name);

        if (PresetBox.Items.Count > 0) PresetBox.SelectedIndex = 0;

        LoadFromManager();
    }

    private void LoadFromManager()
    {
        _rows.Clear();

        foreach (var (key, value) in FastFlagManager.Flags)
            _rows.Add(new FlagRow { Key = key, Value = FastFlagManager.FormatNode(value) });
    }

    private void ApplyPreset_Click(object sender, RoutedEventArgs e)
    {
        if (PresetBox.SelectedIndex < 0) return;

        var preset = FastFlagPresets.All[PresetBox.SelectedIndex];

        foreach (var (key, value) in preset.Values)
            _rows.Add(new FlagRow { Key = key, Value = Format(value) });

        Runtime.Status($"已添加预设：{preset.Name}（记得保存）");
    }

    private static string Format(object? value) => value switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        string s => s,
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    private void AddRow_Click(object sender, RoutedEventArgs e) =>
        _rows.Add(new FlagRow { Key = "FFlagNewFlag", Value = "false" });

    private void RemoveRow_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is FlagRow row)
            _rows.Remove(row);
    }

    private void Save_Click(object sender, RoutedEventArgs e) => SaveFlags(apply: false);

    private void SaveApply_Click(object sender, RoutedEventArgs e) => SaveFlags(apply: true);

    private void SaveFlags(bool apply)
    {
        var flags = new System.Text.Json.Nodes.JsonObject();

        foreach (var row in _rows)
        {
            if (string.IsNullOrWhiteSpace(row.Key)) continue;
            flags[row.Key.Trim()] = FastFlagManager.ParseValue(row.Value);
        }

        FastFlagManager.Replace(flags);
        FastFlagManager.Save();

        if (!apply)
        {
            Runtime.Status("FastFlags 已保存");
            return;
        }

        var version = Runtime.InstalledVersion ?? Deployment.GetNewestInstalledVersion();

        if (version is null)
        {
            AppDialog.Info(Window.GetWindow(this), "引擎", "尚未安装客户端，无法写入 FastFlags。");
            return;
        }

        FastFlagManager.Apply(version);
        Runtime.Status($"FastFlags 已写入 {version}");
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "导入 FastFlags JSON", Filter = "JSON (*.json)|*.json|所有文件|*.*" };

        if (dialog.ShowDialog() != true) return;

        FastFlagManager.Import(dialog.FileName);
        LoadFromManager();
        Runtime.Status("FastFlags 已导入");
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "导出 FastFlags JSON", Filter = "JSON (*.json)|*.json", FileName = "FastFlags.json" };

        if (dialog.ShowDialog() != true) return;

        SaveFlags(apply: false);
        FastFlagManager.Export(dialog.FileName);
        Runtime.Status("FastFlags 已导出");
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        var version = Runtime.InstalledVersion ?? Deployment.GetNewestInstalledVersion();

        if (version is null) return;

        FastFlagManager.Restore(version);
        Runtime.Status("已还原客户端原始设置");
    }
}

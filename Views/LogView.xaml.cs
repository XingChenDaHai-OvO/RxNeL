using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace RxNeL.Views;

public partial class LogView : UserControl
{
    private const int MaxLines = 500;

    private readonly ObservableCollection<string> _lines = new();
    private readonly DispatcherTimer _flushTimer;
    private readonly System.Collections.Generic.Queue<string> _pending = new();
    private readonly object _sync = new();

    private bool _loaded;
    private long _lastScroll;

    public LogView()
    {
        InitializeComponent();

        LogList.ItemsSource = _lines;

        Logger.Line += OnLine;

        _flushTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(220)
        };

        _flushTimer.Tick += (_, _) => Flush();
        _flushTimer.Start();

        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) Flush();
        };

        Loaded += (_, _) => EnsureLoaded();
    }

    private void OnLine(string line)
    {
        lock (_sync)
        {
            _pending.Enqueue(line);

            while (_pending.Count > 1500)
                _pending.Dequeue();
        }
    }

    private void Flush()
    {
        if (!IsVisible) return;

        // 每次最多 120 行：一次塞 1500 行会让这一帧直接掉到 500ms
        var batch = new System.Collections.Generic.List<string>(120);

        lock (_sync)
        {
            if (_pending.Count == 0) return;

            while (_pending.Count > 0 && batch.Count < 120)
                batch.Add(_pending.Dequeue());
        }

        foreach (var line in batch)
            _lines.Add(line);

        while (_lines.Count > MaxLines)
            _lines.RemoveAt(0);

        // 不自动滚动：ListBox 的 ScrollIntoView 在虚拟化下会强制重算全部偏移，
        // 一次 100~400ms。日志这类内容让用户自己滚更划算（也可点「滚到底部」）。
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        var lines = Logger.ReadAll().Split('\n');
        var start = Math.Max(0, lines.Length - MaxLines);

        _lines.Clear();

        for (var i = start; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Length > 0) _lines.Add(line);
        }
    }

    /// <summary>只读日志文件尾部：整个文件可能有上万行，全读会拖慢首帧。</summary>
    private static string ReadLogTail()
    {
        try
        {
            var file = System.IO.Directory.GetFiles(Paths.LogsDir, "*.log")
                .OrderByDescending(f => f)
                .FirstOrDefault();

            if (file is null) return string.Empty;

            using var stream = System.IO.File.OpenRead(file);
            var length = (int)Math.Min(stream.Length, 24_000);

            stream.Seek(-length, System.IO.SeekOrigin.End);

            using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch
        {
            return string.Empty;
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = Paths.LogsDir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Error("打开日志目录失败", ex);
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (_lines.Count > 0)
            Clipboard.SetText(string.Join(Environment.NewLine, _lines));
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => _lines.Clear();

    private void ScrollEnd_Click(object sender, RoutedEventArgs e)
    {
        if (_lines.Count > 0)
            LogList.ScrollIntoView(_lines[^1]);
    }

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        _loaded = false;
        EnsureLoaded();
    }
}

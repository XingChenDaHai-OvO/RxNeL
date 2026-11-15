using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Threading;

namespace RxNeL;

/// <summary>界面流畅度回归：测首帧、切页、控件树构建耗时。</summary>
public static class PerfTest
{
    public static void Run()
    {
        var watch = Stopwatch.StartNew();

        var window = new MainWindow();

        var buildMs = watch.ElapsedMilliseconds;
        watch.Restart();

        window.Show();
        window.UpdateLayout();
        Pump();

        var firstFrameMs = watch.ElapsedMilliseconds;
        watch.Restart();

        window.PrewarmAllPages();
        var prewarmMs = watch.ElapsedMilliseconds;

        var results = new List<(string Name, long Ms)>();

        for (var round = 0; round < 3; round++)
        {
            for (var i = 0; i < window.PageCount; i++)
            {
                var label = window.PageNameAt(i);
                var sw = Stopwatch.StartNew();

                window.SelectPage(i);
                Pump();

                results.Add(($"{label}#{round + 1}", sw.ElapsedMilliseconds));
            }
        }

        var report = new List<string>
        {
            $"render tier = {Perf.Tier} ({(Perf.IsSoftware ? "software" : "hardware")})",
            $"window build = {buildMs} ms",
            $"first frame  = {firstFrameMs} ms",
            $"prewarm all  = {prewarmMs} ms",
        };

        Console.WriteLine($"render tier = {Perf.Tier} ({(Perf.IsSoftware ? "software" : "hardware")})");
        Console.WriteLine($"window build = {buildMs} ms");
        Console.WriteLine($"first frame  = {firstFrameMs} ms");
        Console.WriteLine($"prewarm all  = {prewarmMs} ms（遮罩内）");

        long max = 0;
        long total = 0;

        foreach (var (name, ms) in results)
        {
            Console.WriteLine($"switch {name} = {ms} ms");
            report.Add($"switch {name} = {ms} ms");
            max = Math.Max(max, ms);
            total += ms;
        }

        Console.WriteLine($"switch avg = {total / results.Count} ms, max = {max} ms");
        report.Add($"switch avg = {total / results.Count} ms, max = {max} ms");

        try { System.IO.File.WriteAllLines("perf.txt", report); } catch { }


        window.Close();
    }

    private static void Pump() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
}

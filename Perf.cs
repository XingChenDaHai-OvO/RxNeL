using System;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace RxNeL;

/// <summary>
/// 渲染能力检测 + 全局流畅度默认值。
/// VMware / 远程桌面这类没有 GPU 的环境，WPF 会退到软件光栅：
/// 这时候持续动画、像素着色器（DropShadowEffect）、透明分层窗口都特别贵。
/// </summary>
public static class Perf
{
    public static int Tier { get; private set; } = 2;

    /// <summary>软件渲染（Tier 0）时为 true。</summary>
    public static bool IsSoftware { get; private set; }

    public static long DetectMs { get; private set; }

    public static void Detect()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            Tier = (RenderCapability.Tier >> 16) & 0xFFFF;
            IsSoftware = Tier <= 0;
        }
        catch
        {
            Tier = 0;
            IsSoftware = true;
        }

        DetectMs = watch.ElapsedMilliseconds;

        Logger.Info($"渲染层级: Tier {Tier}（{(IsSoftware ? "软件渲染" : "硬件加速")}）");
    }

    /// <summary>把全局动画帧率降到 30fps：软件渲染下 60fps 会明显吃掉 UI 线程。</summary>
    public static void ApplyAnimationBudget()
    {
        try
        {
            Timeline.DesiredFrameRateProperty.OverrideMetadata(
                typeof(Timeline),
                new System.Windows.FrameworkPropertyMetadata(IsSoftware ? 24 : 60));
        }
        catch (Exception ex)
        {
            Logger.Warn($"设置动画帧率失败：{ex.Message}");
        }
    }
}

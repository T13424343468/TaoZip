using System;
using Microsoft.UI.Xaml;

namespace TaoZip.PC.App;

/// <summary>
/// 主题服务（Tao 系列三象，水墨默认自带）：
/// 0=水墨（宣纸底/浓墨字/青灰蓝点缀，浅色象），1=天青（中钢灰底/深墨青字/深藏蓝点缀，中调象），2=墨夜（近黑底/宣纸白/黛青，深色象）。
/// 应用与切换都作用于当前窗口，字典挂应用级资源，新开窗口自动继承。
/// v61 治本：三套字典一律扁平（不分 Light/Dark 段，永远生效），三象互斥只挂当象一个，
/// 与系统基调彻底脱钩——深色系统的机器水墨不再黑底。
/// </summary>
public static class ThemeService
{
    private static readonly Uri InkSource = new("ms-appx:///Themes/InkTheme.xaml");
    private static readonly Uri SkySource = new("ms-appx:///Themes/SkyTheme.xaml");
    private static readonly Uri NightSource = new("ms-appx:///Themes/TaoDark.xaml");

    /// <summary>
    /// 快切并档定时器（2026-11-02 TaoDown 真机同型教训：6 秒连切 8 次主题，
    /// 部分控件吃第一拍漏第二拍出混合态）。150ms 内连击只落定最后一次，
    /// 中间态一次都不执行；字段持有防 GC 回收（刷新时钟被回收的同型教训）。
    /// </summary>
    private static Microsoft.UI.Dispatching.DispatcherQueueTimer? _coalesceTimer;

    /// <summary>并档期间的待落定目标（窗口 + 主题），由最后一次 Apply 覆盖。</summary>
    private static Window? _pendingWindow;
    private static int _pendingThemeId;

    /// <summary>对窗口应用主题：三象字典互斥只挂当象，并钉基调与标题栏（万流不漏）。</summary>
    public static void Apply(Window? window, int themeId)
    {
        if (window is null)
        {
            return;
        }

        // 快切并档：150ms 内的连击只落定最后一次，杜绝中间混合态（无论多快都不能串）。
        var queue = window.DispatcherQueue;
        if (queue is null)
        {
            ApplyNow(window, themeId);
            return;
        }

        if (_coalesceTimer is null)
        {
            _coalesceTimer = queue.CreateTimer();
            _coalesceTimer.Interval = TimeSpan.FromMilliseconds(150);
            _coalesceTimer.IsRepeating = false;
            _coalesceTimer.Tick += (_, _) =>
            {
                _coalesceTimer.Stop();
                var w = _pendingWindow;
                var id = _pendingThemeId;
                _pendingWindow = null;
                ApplyNow(w, id);
            };
        }

        _pendingWindow = window;
        _pendingThemeId = themeId;
        _coalesceTimer.Stop();
        _coalesceTimer.Start();
    }

    /// <summary>真正的落定舞步：摘净三套字典 + 挂当象 + 基调两拍 + 标题栏钉色。由并档定时器唯一调用。</summary>
    private static void ApplyNow(Window? window, int themeId)
    {
        if (window is null)
        {
            return;
        }

        try
        {
            var resources = Application.Current.Resources;
            // 病根(v61): 分段字典 (ThemeDictionaries) 跟随系统基调——深色系统的机器
            // 浅色段整段不生效, 水墨照样黑底 (真机截图坐实: 按钮浅/底黑/标题栏黑三色三来路)。
            // 治本: 扁平字典 + 三象互斥, 摘净三套再挂当象一个, 谁也不跟系统基调走。
            RemoveTheme(resources, "InkTheme");
            RemoveTheme(resources, "SkyTheme");
            RemoveTheme(resources, "TaoDark");
            var source = themeId switch { 1 => SkySource, 2 => NightSource, _ => InkSource };
            resources.MergedDictionaries.Add(new ResourceDictionary { Source = source });
            var requested = BaseFor(themeId);
            if (window.Content is FrameworkElement root)
            {
                // 病根(v58): WinUI 只在实际生效基调(ActualTheme)变化时重估 ThemeResource。
                // v73 假设"字典摘挂自己会重估刷色"——v76 真机证伪: 水墨↔天青互切(都是浅底)
                // 时窗口停在旧配色。定案: WinUI 重估 ThemeResource 只认基调变化。
                // 修法(v77): 基调在变→一次赋值即可; 基调不变(纯字典切换)→先反再落强制重估,
                // 两次赋值在同一调用栈内完成, 中间值不经历渲染帧。
                // v74: 不额外清空根板过渡集合——相关类型在 WinUI3 的命名空间与文档不符,
                // 猜不起; 子窗白屏根因另有其因(界面线程卡死), 与过渡动画无关。
                var before = root.ActualTheme;
                if (root.ActualTheme != requested)
                {
                    root.RequestedTheme = requested;
                }
                else
                {
                    root.RequestedTheme = requested == ElementTheme.Light ? ElementTheme.Dark : ElementTheme.Light;
                    root.RequestedTheme = requested;
                }
                LogApply(themeId, before, root.ActualTheme);
                PinTitleBar(window, root);
            }
        }
        catch (Exception)
        {
            // 主题属于外观增强，任何异常都不影响主功能
        }
    }

    /// <summary>基调映射（唯一真相，可测）：水墨/天青浅色，墨夜深色。</summary>
    internal static ElementTheme BaseFor(int themeId) =>
        themeId == 2 ? ElementTheme.Dark : ElementTheme.Light;

    private static void RemoveTheme(Microsoft.UI.Xaml.ResourceDictionary resources, string marker)
    {
        for (var i = resources.MergedDictionaries.Count - 1; i >= 0; i--)
        {
            var src = resources.MergedDictionaries[i].Source?.OriginalString ?? "";
            if (src.Contains(marker)) resources.MergedDictionaries.RemoveAt(i);
        }
    }

    /// <summary>
    /// 标题栏随象而染（v60）: 非客户区的标题栏与三颗小按钮（最小化/最大化/关闭）
    /// 不吃 XAML 资源，颜色跟系统走——墨夜近黑底上深色按钮即隐身。
    /// 按当前主题解析出的笔刷给系统标题栏钉色，真人肉眼必可辨。
    /// </summary>
    private static void PinTitleBar(Window window, FrameworkElement root)
    {
        try
        {
            // v79 真机定案: 主题字典挂在 Application.Current.Resources, 而 root.Resources 的
            // 直接索引不做应用级回退 → 永远取不到 → 标题栏从未钉色, 墨夜下窗控按钮隐身。
            // 改从应用级资源取, 每次 Apply 都按当象字典钉色。
            var res = Application.Current.Resources;
            if (res["WindowCaptionForegroundBrush"] is not Microsoft.UI.Xaml.Media.SolidColorBrush glyph) return;
            if (res["WindowCaptionBackgroundBrush"] is not Microsoft.UI.Xaml.Media.SolidColorBrush capBg) return;
            PinCaption(window, glyph.Color, capBg.Color);
        }
        catch (Exception)
        {
            // 标题栏着色属于外观增强, 失败静默
        }
    }

    /// <summary>
    /// 子窗标题栏随象钉色（v63）: 解压/压缩子窗不另 Apply（换肤入口唯一），
    /// 但扁平字典下内容随主题而染，标题栏若仍跟系统走会"内容浅/标题栏黑"拼色。
    /// 按当前挂载主题解析标题栏色钉上，与主窗一致。
    /// </summary>
    internal static void PinSubTitleBar(Window? window)
    {
        if (window is null) return;
        try
        {
            var res = Application.Current.Resources;
            if (res["WindowCaptionForegroundBrush"] is not Microsoft.UI.Xaml.Media.SolidColorBrush glyph) return;
            if (res["WindowCaptionBackgroundBrush"] is not Microsoft.UI.Xaml.Media.SolidColorBrush capBg) return;
            PinCaption(window, glyph.Color, capBg.Color);
        }
        catch (Exception)
        {
            // 外观增强, 静默
        }
    }

    /// <summary>
    /// 窗口自检（v68）: 激活后清点可视树实际渲染内容（每个子层的类型/名字/实际尺寸），
    /// 落盘 theme.log。真机"小窗空白"在沙箱无法复现, 用地面实况定位: 是树是空的,
    /// 还是元素在但尺寸为零/越界。
    /// </summary>
    internal static void DiagnoseWindow(Window window, string tag)
    {
        try
        {
            if (window.Content is not FrameworkElement root)
            {
                WriteDiag($"{tag}: Content 为空");
                return;
            }
            var sb = new System.Text.StringBuilder();
            sb.Append($"{tag}: root={root.GetType().Name} {root.ActualWidth:F0}x{root.ActualHeight:F0} theme={root.ActualTheme}");
            Walk(root, 0, sb);
            // 进程身份: 右键菜单启动的是装好的那份程序, 记下路径与文件时间, 防止"新包没装、老程序背锅"
            try
            {
                var exe = System.IO.Path.Combine(AppContext.BaseDirectory, "TaoZip.exe");
                var stamp = System.IO.File.Exists(exe) ? System.IO.File.GetLastWriteTime(exe).ToString("yyyy-MM-dd HH:mm:ss") : "缺失";
                sb.Append($" | 程序={AppContext.BaseDirectory} 构建时间={stamp}");
            }
            catch { /* 身份信息仅诊断用 */ }
            WriteDiag(sb.ToString());
        }
        catch (Exception ex)
        {
            WriteDiag($"{tag}: 自检异常 {ex.GetType().Name} {ex.Message}");
        }
    }

    private static void Walk(Microsoft.UI.Xaml.DependencyObject node, int depth, System.Text.StringBuilder sb)
    {
        const int max = 12;
        if (depth > max) return;
        foreach (var child in Enumerable(node))
        {
            var indent = new string('>', depth + 1);
            if (child is FrameworkElement fe)
                sb.Append($" {indent}{fe.GetType().Name}[{fe.Name}] {fe.ActualWidth:F0}x{fe.ActualHeight:F0} vis={fe.Visibility}");
            else
                sb.Append($" {indent}{child.GetType().Name}");
            Walk(child, depth + 1, sb);
        }
    }

    private static System.Collections.Generic.IEnumerable<Microsoft.UI.Xaml.DependencyObject> Enumerable(Microsoft.UI.Xaml.DependencyObject node)
    {
        var n = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < n; i++) yield return Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i);
    }

    /// <summary>外部事件落盘（v68）: 子窗启动期异常等写进同一份 theme.log 便于对时序。</summary>
    internal static void LogEvent(string line) => WriteDiag(line);

    private static void WriteDiag(string line)
    {
        try
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(TaoZip.Core.TaoPaths.DataDir("TaoZip"), "theme.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}\r\n");
        }
        catch { /* 诊断落盘失败静默 */ }
    }

    /// <summary>
    /// 小窗按屏幕工作区居中 (v82 用户反馈: 小窗默认应落在屏幕中间, 系统默认位随缘)。
    /// 传窗口像素尺寸, 取主显示器工作区 (避开任务栏); 失败退回系统默认位, 静默。
    /// </summary>
    public static void CenterOnScreen(Microsoft.UI.Windowing.AppWindow appWindow, int width, int height)
    {
        try
        {
            var wa = Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea;
            // AppWindow.Position 是只读属性, 挪位须走 Move
            appWindow.Move(new Windows.Graphics.PointInt32(
                wa.X + (wa.Width - width) / 2,
                wa.Y + (wa.Height - height) / 2));
        }
        catch { /* 居中失败退回系统默认位 */ }
    }

    private static void PinCaption(Window window, Windows.UI.Color glyph, Windows.UI.Color capBg)
    {
        var appWindow = window.AppWindow;
        if (appWindow?.TitleBar is not { } titleBar) return;
        titleBar.BackgroundColor = capBg;
        titleBar.ForegroundColor = glyph;
        titleBar.ButtonBackgroundColor = capBg;
        titleBar.ButtonForegroundColor = glyph;
        // v80 真机反馈: 悬停/按下底色原样钉 capBg, 墨夜下与常态毫无差别, 肉眼看不出悬停反馈。
        // 改为向前景色调和: 悬停 12% 轻提亮, 按下 20% 更明显, 三象自适应。
        titleBar.ButtonHoverBackgroundColor = Blend(capBg, glyph, 0.12f);
        titleBar.ButtonHoverForegroundColor = glyph;
        titleBar.ButtonPressedBackgroundColor = Blend(capBg, glyph, 0.20f);
        titleBar.ButtonPressedForegroundColor = glyph;
    }

    /// <summary>颜色按比例向目标调和 (t=0 取 a, t=1 取 b), 供窗控悬停/按下态生成可辨底色。</summary>
    private static Windows.UI.Color Blend(Windows.UI.Color a, Windows.UI.Color b, float t) =>
        Windows.UI.Color.FromArgb(a.A,
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));

    /// <summary>
    /// 换肤自证日志: 每次 Apply 追加一行 (目标主题/切换前后实际基调/挂载字典)。
    /// 沙箱无法验证 XAML 运行时表现, 真机若再现"切换没效果", 日志可直接定位
    /// 是重估没触发还是字典没挂上。外观项, 写失败静默。
    /// </summary>
    private static void LogApply(int themeId, ElementTheme before, ElementTheme after)
    {
        try
        {
            var dicts = "";
            foreach (var d in Application.Current.Resources.MergedDictionaries)
            {
                var src = d.Source?.OriginalString ?? "";
                if (src.Contains("Theme")) dicts += System.IO.Path.GetFileName(src) + " ";
            }
            var name = themeId == 0 ? "水墨" : themeId == 1 ? "天青" : "墨夜";
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} 换肤→{name} 基调 {before}→{after} 字典=[{dicts}]\r\n";
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(TaoZip.Core.TaoPaths.DataDir("TaoZip"), "theme.log"), line);
        }
        catch { /* 外观诊断, 静默 */ }
    }
}

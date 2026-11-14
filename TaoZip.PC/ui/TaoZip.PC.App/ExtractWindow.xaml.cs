using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using TaoZip.Core;

namespace TaoZip.PC.App;

/// <summary>右键解压小窗: 进度条 + 百分比 + 预估剩余时间; 可转后台 (托盘) 继续解压, 点托盘唤起查看进度。</summary>
public sealed partial class ExtractWindow : Window
{
    private readonly ArchiveService _svc = new();
    private readonly HistoryStore _hist = new(TaoPaths.DataDir("TaoZip")); // v89: 右键解压同样落历史
    private readonly List<(DateTime t, double p)> _samples = new();
    private TrayIcon? _tray;
    private bool _inTray;
    private bool _done;
    private double _lastPct = -1;
    private readonly DispatcherTimer _etaTimer;
    private volatile bool _uiAlive; // v78 心电图: 界面线程每次动一下就置真, 后台心跳读它判死活

    public ExtractWindow(string archive)
    {
        InitializeComponent();
        AppWindow.Title = "TaoZip 解压中";
        AppIcon.Apply(AppWindow);
        ThemeService.PinSubTitleBar(this);
        var winSize = new Windows.Graphics.SizeInt32();
        winSize.Width = 500;
        winSize.Height = 210;
        AppWindow.Resize(winSize);
        // v82 用户反馈: 小窗默认落屏幕中间, 不再由系统随缘摆放
        ThemeService.CenterOnScreen(AppWindow, winSize.Width, winSize.Height);
        // 每秒刷新一次估算：单个大文件久无进度回调时，剩余时间不再冻结在旧值上
        _etaTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _etaTimer.Tick += (_, _) =>
        {
            _uiAlive = true;
            if (!_done && _lastPct >= 0)
                TxtPercent.Text = $"解压中 {_lastPct:F1}% · {ComputeEta()}";
        };
        _etaTimer.Start();
        Closed += (_, _) => { _etaTimer.Stop(); _tray?.Dispose(); };
        // v68 自检: 首次激活即清点可视树, 空白窗真相落盘 theme.log
        Activated += OnFirstActivated;
        _ = StartSafe(archive); // (2026-11-14 审查黄1) fire-and-forget 安全包装: 异常落盘收口, 不再穿 async void 杀进程
    }

    private async Task StartSafe(string archive)
    {
        try { await Start(archive); }
        catch (Exception ex)
        {
            ThemeService.LogEvent($"解压窗启动异常: {ex.GetType().Name} {ex.Message}\r\n栈首: {App.TrimStack(ex.StackTrace)}");
            try { TxtPercent.Text = $"解压失败: {ex.Message}"; } catch { /* 界面未就绪时静默, 已落盘 */ }
        }
    }

    private bool _diagDone;
    private void OnFirstActivated(object sender, WindowActivatedEventArgs e)
    {
        if (_diagDone) return;
        _diagDone = true;
        // v85 真机截图定案: 构造期按物理像素定尺寸, 高缩放屏 (125%/150%) 窗口比内容窄,
        // 内容左缘被裁。激活后按界面实际缩放比例重定尺寸并居中。
        ApplyWindowSize();
        ThemeService.DiagnoseWindow(this, "解压窗");
        SelfHealCheck();
    }

    /// <summary>按 XAML 逻辑尺寸 × 屏幕缩放比例重定窗口物理尺寸并居中 (v85)。</summary>
    private void ApplyWindowSize()
    {
        try
        {
            var scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
            if (scale <= 0) scale = 1.0;
            var w = (int)Math.Round(500 * scale);
            var h = (int)Math.Round(210 * scale);
            AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = w, Height = h });
            ThemeService.CenterOnScreen(AppWindow, w, h);
        }
        catch (Exception ex) { ThemeService.LogEvent($"解压窗尺寸适配失败: {ex.GetType().Name} {ex.Message}"); }
    }

    // ---- v71 自愈: 激活后内容若未实际渲染(高为零), 用纯代码重建界面, 不再依赖可疑加载链路 ----
    private void SelfHealCheck()
    {
        var t = DispatcherQueue.CreateTimer();
        t.Interval = TimeSpan.FromMilliseconds(600);
        t.IsRepeating = false;
        t.Tick += (_, _) =>
        {
            _uiAlive = true;
            // v73: 体检结论无论好坏都落盘, 真机日志一次说清"排版到底活没活"
            ThemeService.LogEvent($"解压窗: 600ms 体检 内容高={BodyPanel.ActualHeight:F0}px 宽={BodyPanel.ActualWidth:F0}px" +
                (BodyPanel.ActualHeight > 1 ? "（排版正常, 自愈跳过）" : "（排版为零 → 触发自愈重建）"));
            if (BodyPanel.ActualHeight > 1) return;
            ThemeService.LogEvent("解压窗: 启用纯代码自愈重建");
            try { RebuildInCode(); }
            catch (Exception ex) { ThemeService.LogEvent($"解压窗: 自愈失败 {ex.GetType().Name} {ex.Message}"); }
        };
        t.Start();
    }

    private Microsoft.UI.Xaml.Media.Brush? PickBrush(string key, string fallbackHex)
    {
        if (Application.Current.Resources[key] is Microsoft.UI.Xaml.Media.Brush b) return b;
        return new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Microsoft.UI.ColorHelper.FromArgb(255,
                Convert.ToByte(fallbackHex.Substring(1, 2), 16),
                Convert.ToByte(fallbackHex.Substring(3, 2), 16),
                Convert.ToByte(fallbackHex.Substring(5, 2), 16)));
    }

    private void RebuildInCode()
    {
        BodyPanel.Children.Clear();
        // v82: 与 XAML 同构——文件名两行上限省略号, 百分比与按钮双栏互不顶开
        var file = new Microsoft.UI.Xaml.Controls.TextBlock
        {
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 15,
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap, MaxLines = 2,
            TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis,
            Foreground = PickBrush("TextFillColorPrimaryBrush", "#1C242C")
        };
        var bar = new Microsoft.UI.Xaml.Controls.ProgressBar { Maximum = 100, Foreground = PickBrush("AccentFillColorDefaultBrush", "#0F4C9E") };
        var pct = new Microsoft.UI.Xaml.Controls.TextBlock
        {
            Text = "准备中…", TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            Foreground = PickBrush("TextFillColorSecondaryBrush", "#6F6A5E")
        };
        var btn = new Microsoft.UI.Xaml.Controls.Button { Content = "后台解压", Padding = new Thickness(14, 4, 14, 4), VerticalAlignment = VerticalAlignment.Center };
        btn.Click += BtnBackground_Click;
        var row = new Microsoft.UI.Xaml.Controls.Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new Microsoft.UI.Xaml.Controls.ColumnDefinition { Width = new Microsoft.UI.Xaml.GridLength(1, Microsoft.UI.Xaml.GridUnitType.Star) });
        row.ColumnDefinitions.Add(new Microsoft.UI.Xaml.Controls.ColumnDefinition { Width = new Microsoft.UI.Xaml.GridLength(1, Microsoft.UI.Xaml.GridUnitType.Auto) });
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(pct, 0);
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(btn, 1);
        row.Children.Add(pct);
        row.Children.Add(btn);
        BodyPanel.Children.Add(file);
        BodyPanel.Children.Add(bar);
        BodyPanel.Children.Add(row);
        TxtFile = file;
        BarProgress = bar;
        TxtPercent = pct;
    }

    private async Task Start(string archive)
    {
        try
        {
            TxtFile.Text = Path.GetFileName(archive);
            if (!File.Exists(archive))
            {
                TxtPercent.Text = $"找不到文件: {archive}";
                _hist.Add("解压", archive, "", false, "找不到文件");
                return;
            }
        }
        catch (Exception ex)
        {
            // (2026-11-14 审查黄1) 去 rethrow: 就地落盘并给界面提示, 不再把异常改道送进 UnhandledException
            ThemeService.LogEvent($"解压窗 Start 前段异常: {ex.GetType().Name} {ex.Message}");
            TxtPercent.Text = $"解压失败: {ex.Message}";
            return;
        }
        var dest = ArchiveService.DefaultDestFor(archive);
        var began = DateTime.Now;
        ThemeService.LogEvent($"解压开始: {archive} → {dest}");
        // v78 心电图: 后台心跳 6 秒, 每拍记录界面线程死活——白屏时是"线程卡死"还是"进程崩溃"一次看清
        _ = Task.Run(async () =>
        {
            ThemeService.LogEvent("解压窗: 心跳启动");
            for (var i = 1; i <= 12; i++)
            {
                await Task.Delay(500);
                ThemeService.LogEvent($"解压窗: 心跳{i}/12 界面线程活={_uiAlive}");
            }
        });
        // v80 真机定案: 崩点栈指进度回调 ui.TryEnqueue 一线。Progress 本身已按构造时捕获的
        // 界面同步上下文派发(崩点栈第三帧即 DispatcherQueueSynchronizationContext.Post),
        // 再手动 TryEnqueue 属画蛇添足且正是 NRE 源, 整层拆除, 回调直摸控件。
        var progress = new Progress<double>(p =>
        {
            try
            {
                // 防御: 非有限值与越界百分比一律拦下, 不进进度条也不进估算样本
                if (!_done && double.IsFinite(p))
                {
                    var c = Math.Clamp(p, 0, 100);
                    BarProgress.Value = c;
                    _lastPct = c;
                    _samples.Add((DateTime.Now, c));
                    TrimSamples();
                    TxtPercent.Text = $"解压中 {c:F1}% · {ComputeEta()}";
                }
            }
            catch (Exception ex)
            {
                // 带栈落盘, 不再无声崩进程
                ThemeService.LogEvent($"解压进度回调异常: {ex.GetType().Name} {ex.Message}\r\n栈首: {App.TrimStack(ex.StackTrace)}");
            }
        });
        try
        {
            var task = Task.Run(() => _svc.SmartExtract(archive, dest, progress));
            // v78 后台观察员: 解压任务的结局由后台线程直接落盘, 不再依赖界面线程活着
            _ = task.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    var ex = t.Exception?.InnerException;
                    ThemeService.LogEvent($"解压后台观察: 解压任务异常退出 {ex?.GetType().Name} {ex?.Message}");
                }
                else
                {
                    ThemeService.LogEvent($"解压后台观察: 解压任务正常结束, 文件数={t.Result.Files}, 目录数={t.Result.Directories}");
                }
            }, TaskScheduler.Default);
            var r = await task;
            _done = true;
            BarProgress.Value = 100;
            TxtPercent.Text = $"解压完成 —— {r.Files} 个文件 → {dest}";
            _hist.Add("解压", archive, dest, true);
            ThemeService.LogEvent($"解压完成: {r.Files} 个文件, 耗时 {(DateTime.Now - began).TotalSeconds:F1} 秒");
            if (_inTray)
            {
                _tray?.Balloon("TaoZip 解压完成", $"{r.Files} 个文件已解压到 {dest}，点击托盘图标查看详情。");
            }
            else
            {
                await Task.Delay(1500);
                Close();
            }
        }
        catch (Exception ex)
        {
            _done = true; // 失败同样收口: 停掉估算定时器, 不再误报剩余时间
            TxtPercent.Text = $"解压失败: {ex.Message}";
            _hist.Add("解压", archive, dest, false, ex.Message);
            ThemeService.LogEvent($"解压失败: {ex.GetType().Name} {ex.Message}, 耗时 {(DateTime.Now - began).TotalSeconds:F1} 秒");
            if (_inTray) _tray?.Balloon("TaoZip 解压失败", ex.Message);
        }
    }

    /// <summary>用最近 10 秒的实际速度滚动估算剩余时间; 样本不足或久无新进度时提示估算中, 不拿旧速度硬猜。</summary>
    private void TrimSamples()
    {
        var now = DateTime.Now;
        while (_samples.Count > 2 && (now - _samples[0].t).TotalSeconds > 10) _samples.RemoveAt(0);
        // 高频回调防堆积: 10 秒窗口内样本超量时丢最旧的
        if (_samples.Count > 256) _samples.RemoveRange(0, _samples.Count - 256);
    }

    private string ComputeEta()
    {
        if (_samples.Count < 2) return "剩余时间估算中…";
        var (t0, p0) = _samples[0];
        var (t1, p1) = _samples[^1];
        var dt = (t1 - t0).TotalSeconds;
        var dp = p1 - p0;
        if (dt < 0.5 || dp < 0.01) return "剩余时间估算中…";
        if ((DateTime.Now - t1).TotalSeconds > 10) return "剩余时间估算中…"; // 久无新进度(如单个大文件), 不硬猜
        if (p1 >= 99.9) return "即将完成…";
        var remain = Math.Max(0, (100 - p1) / (dp / dt));
        if (remain < 60) return $"预计剩余 {remain:F0} 秒";
        if (remain < 3600) return $"预计剩余 {(int)(remain / 60)} 分 {remain % 60:F0} 秒";
        return $"预计剩余约 {(int)(remain / 3600)} 小时 {(int)(remain % 3600 / 60)} 分";
    }

    /// <summary>转入后台: 隐藏窗口, 托盘图标接管, 点图标唤起查看进度。</summary>
    private void BtnBackground_Click(object sender, RoutedEventArgs e)
    {
        if (_inTray) return; // 防连点重复触发托盘气泡
        if (_tray is null) _tray = new TrayIcon(WinRT.Interop.WindowNative.GetWindowHandle(this), "TaoZip 后台解压中 — 点击查看进度", ShowFromTray);
        _inTray = true;
        AppWindow.Hide();
        _tray.Balloon("TaoZip 后台解压中", "已最小到托盘，点击图标随时查看进度。");
    }

    private void ShowFromTray()
    {
        _inTray = false;
        AppWindow.Show();
        Activate();
    }
}

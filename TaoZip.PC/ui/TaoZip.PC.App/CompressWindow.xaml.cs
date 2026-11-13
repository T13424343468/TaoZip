using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using TaoZip.Core;

namespace TaoZip.PC.App;

/// <summary>
/// 右键压缩小窗: 默认压缩到当前文件夹同名 zip, 可浏览自定义; 进度条 + 百分比 + 预估剩余时间, 可转后台 (托盘)。
/// 资源管理器多选时会为每个选中项各启动一个进程, 这里用临时标记 + 命名互斥体把同批选中项合并成一个压缩包。
/// </summary>
public sealed partial class CompressWindow : Window
{
    private readonly ArchiveService _svc = new();
    private readonly HistoryStore _hist = new(TaoPaths.DataDir("TaoZip")); // v89: 右键压缩同样落历史; 记录指向产物 zip, 误删防护见条目语义
    private readonly List<string> _paths;
    private readonly List<(DateTime t, double p)> _samples = new();
    private TrayIcon? _tray;
    private bool _inTray;
    private bool _done;
    private bool _started;
    private double _lastPct = -1;
    private readonly DispatcherTimer _etaTimer;
    private volatile bool _uiAlive; // v78 心电图: 界面线程每次动一下就置真, 后台心跳读它判死活

    public CompressWindow(string path)
    {
        InitializeComponent();
        AppWindow.Title = "TaoZip 压缩";
        AppIcon.Apply(AppWindow);
        ThemeService.PinSubTitleBar(this);
        var winSize = new Windows.Graphics.SizeInt32();
        winSize.Width = 530;
        winSize.Height = 280;
        AppWindow.Resize(winSize);
        // v82 用户反馈: 小窗默认落屏幕中间, 不再由系统随缘摆放
        ThemeService.CenterOnScreen(AppWindow, winSize.Width, winSize.Height);
        // 每秒刷新一次估算: 单个大文件久无进度回调时, 剩余时间不再冻结在旧值上
        _etaTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _etaTimer.Tick += (_, _) =>
        {
            _uiAlive = true;
            if (!_done && _lastPct >= 0)
                TxtPercent.Text = $"压缩中 {_lastPct:F1}% · {ComputeEta()}";
        };
        Closed += (_, _) => { _etaTimer.Stop(); _tray?.Dispose(); };
        // v68 自检: 首次激活即清点可视树, 空白窗真相落盘 theme.log
        Activated += OnFirstActivated;
        _paths = CollectMultiSelect(path);
        TxtSummary.Text = Describe();
        TxtDest.Text = DefaultDest();
        // v78 心电图: 后台心跳 20 秒, 真机白屏时"线程卡死"还是"进程崩溃"一次看清
        _ = Task.Run(async () =>
        {
            ThemeService.LogEvent("压缩窗: 心跳启动");
            for (var i = 1; i <= 20; i++)
            {
                await Task.Delay(1000);
                ThemeService.LogEvent($"压缩窗: 心跳{i}/20 界面线程活={_uiAlive}");
            }
        });
    }

    private bool _diagDone;
    private void OnFirstActivated(object? sender, WindowActivatedEventArgs e)
    {
        if (_diagDone) return;
        _diagDone = true;
        // v85 真机截图定案: 构造期按物理像素定尺寸, 高缩放屏窗口比内容窄, 激活后按缩放比例重定
        ApplyWindowSize();
        ThemeService.DiagnoseWindow(this, "压缩窗");
        SelfHealCheck();
    }

    /// <summary>按 XAML 逻辑尺寸 × 屏幕缩放比例重定窗口物理尺寸并居中 (v85)。</summary>
    private void ApplyWindowSize()
    {
        try
        {
            var scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
            if (scale <= 0) scale = 1.0;
            var w = (int)Math.Round(530 * scale);
            var h = (int)Math.Round(280 * scale);
            AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = w, Height = h });
            ThemeService.CenterOnScreen(AppWindow, w, h);
        }
        catch (Exception ex) { ThemeService.LogEvent($"压缩窗尺寸适配失败: {ex.GetType().Name} {ex.Message}"); }
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
            ThemeService.LogEvent($"压缩窗: 600ms 体检 内容高={BodyPanel.ActualHeight:F0}px 宽={BodyPanel.ActualWidth:F0}px" +
                (BodyPanel.ActualHeight > 1 ? "（排版正常, 自愈跳过）" : "（排版为零 → 触发自愈重建）"));
            if (BodyPanel.ActualHeight > 1) return;
            ThemeService.LogEvent("压缩窗: 启用纯代码自愈重建");
            try { RebuildInCode(); }
            catch (Exception ex) { ThemeService.LogEvent($"压缩窗: 自愈失败 {ex.GetType().Name} {ex.Message}"); }
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
        var summary = new Microsoft.UI.Xaml.Controls.TextBlock
        {
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 15,
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            Foreground = PickBrush("TextFillColorPrimaryBrush", "#1C242C")
        };
        var destLabel = new Microsoft.UI.Xaml.Controls.TextBlock
        {
            Text = "压缩包位置（默认当前文件夹，可修改）",
            Foreground = PickBrush("TextFillColorSecondaryBrush", "#6F6A5E")
        };
        var dest = new Microsoft.UI.Xaml.Controls.TextBox();
        var browse = new Microsoft.UI.Xaml.Controls.Button { Content = "浏览…", Padding = new Thickness(14, 5, 14, 5) };
        browse.Click += OnBrowse;
        var start = new Microsoft.UI.Xaml.Controls.Button { Content = "开始压缩", Padding = new Thickness(20, 5, 20, 5) };
        start.Click += OnStart;
        var bar = new Microsoft.UI.Xaml.Controls.ProgressBar { Maximum = 100, Foreground = PickBrush("AccentFillColorDefaultBrush", "#0F4C9E"), Visibility = Visibility.Collapsed };
        var pct = new Microsoft.UI.Xaml.Controls.TextBlock
        {
            Text = "准备中…", TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
            Foreground = PickBrush("TextFillColorSecondaryBrush", "#6F6A5E")
        };
        var bg = new Microsoft.UI.Xaml.Controls.Button { Content = "后台压缩", Padding = new Thickness(14, 5, 14, 5), VerticalAlignment = VerticalAlignment.Center };
        bg.Click += OnBackground;
        var row = new Microsoft.UI.Xaml.Controls.StackPanel { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(browse);
        row.Children.Add(start);
        var prow = new Microsoft.UI.Xaml.Controls.StackPanel { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 10, Visibility = Visibility.Collapsed };
        prow.Children.Add(pct);
        prow.Children.Add(bg);
        BodyPanel.Children.Add(summary);
        BodyPanel.Children.Add(destLabel);
        BodyPanel.Children.Add(dest);
        BodyPanel.Children.Add(row);
        BodyPanel.Children.Add(bar);
        BodyPanel.Children.Add(prow);
        TxtSummary = summary;
        TxtDest = dest;
        BarProgress = bar;
        TxtPercent = pct;
        PanelProgress = prow;
        // v76 补漏: 三颗按钮也要重赋, 否则自愈后 OnStart 摸到的还是已拆除的旧按钮
        BtnBrowse = browse;
        BtnStart = start;
        BtnBackground = bg;
    }

    // ---- 多选合并: 写标记 → 抢收集权 → 等同批进程写齐 → 汇总 ----
    private static List<string> CollectMultiSelect(string path)
    {
        try
        {
            var dir = Path.Combine(TaoPaths.TempDir("TaoZip"), "TaoZipCompress");
            Directory.CreateDirectory(dir);
            // 清理异常退出留下的超过 1 小时的旧标记, 防止混入不相关文件
            foreach (var f in Directory.GetFiles(dir, "*.txt"))
                if (DateTime.Now - File.GetLastWriteTime(f) > TimeSpan.FromHours(1))
                { try { File.Delete(f); } catch { } }

            var marker = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(marker, path);
            using var mux = new Mutex(initiallyOwned: false, "Local\\TaoZip.Compress.Collect");
            bool got = false;
            try
            {
                try { got = mux.WaitOne(TimeSpan.FromSeconds(5)); }
                catch (AbandonedMutexException) { got = true; } // 上个持有者异常退出, 标记按现状接管
                if (!got) return new List<string> { path }; // 等不到收集权: 按单文件处理, 不让用户干等
                Thread.Sleep(900); // 等资源管理器同批启动的进程把选中项写齐
                var list = Directory.GetFiles(dir, "*.txt")
                    .Select(ReadMarker)
                    .Where(p => !string.IsNullOrWhiteSpace(p) && (File.Exists(p) || Directory.Exists(p)))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                foreach (var f in Directory.GetFiles(dir, "*.txt")) { try { File.Delete(f); } catch { } }
                return list.Count > 0 ? list : new List<string> { path };
            }
            finally { if (got) mux.ReleaseMutex(); }
        }
        catch { return new List<string> { path }; } // 任何异常都退回单文件, 保证还能压缩
    }

    private static string ReadMarker(string f) { try { return File.ReadAllText(f).Trim(); } catch { return ""; } }

    // ---- 界面文案与默认目标 ----
    private string Describe()
    {
        if (_paths.Count == 1)
        {
            var p = _paths[0];
            return (Directory.Exists(p) ? "文件夹: " : "文件: ") + Path.GetFileName(p.TrimEnd('\\', '/'));
        }
        var names = _paths.Select(p => Path.GetFileName(p.TrimEnd('\\', '/'))).ToList();
        var head = string.Join("、", names.Take(3));
        return $"{_paths.Count} 项: {head}{(names.Count > 3 ? " 等" : "")}";
    }

    /// <summary>默认目标: 单项用其同名 zip, 多项用当前文件夹名 zip; 已存在自动加序号。</summary>
    private string DefaultDest()
    {
        string dir, name;
        if (_paths.Count == 1)
        {
            var full = Path.GetFullPath(_paths[0]).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            dir = Path.GetDirectoryName(full) ?? ".";
            name = Path.GetFileName(full);
        }
        else
        {
            var dir0 = Path.GetDirectoryName(Path.GetFullPath(_paths[0])) ?? ".";
            dir = dir0;
            name = Path.GetFileName(dir0); // 多选必然同目录, 用当前文件夹名命名
        }
        return NextFree(Path.Combine(dir, name + ".zip"));
    }

    private static string NextFree(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path) ?? ".";
        var stem = Path.GetFileNameWithoutExtension(path);
        var n = 2;
        while (File.Exists(Path.Combine(dir, $"{stem}({n}).zip"))) n++;
        return Path.Combine(dir, $"{stem}({n}).zip");
    }

    // ---- 浏览: 选压缩包保存位置与名称 (v86: 失败不再无声, 提示可直接手输路径) ----
    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        Windows.Storage.Pickers.FileSavePicker picker;
        try
        {
            picker = new Windows.Storage.Pickers.FileSavePicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            picker.SuggestedFileName = Path.GetFileName(TxtDest.Text);
            picker.FileTypeChoices.Add("zip 压缩包", new List<string> { ".zip" });
        }
        catch (Exception ex)
        {
            ThemeService.LogEvent($"压缩小窗: 保存对话框初始化失败 {ex.GetType().Name} {ex.Message}");
            TxtPercent.Text = "保存对话框打不开, 可直接在路径框输入保存位置。";
            return;
        }
        Windows.Storage.StorageFile? file;
        try { file = await picker.PickSaveFileAsync(); }
        catch (Exception ex)
        {
            ThemeService.LogEvent($"压缩小窗: 保存对话框打开失败 {ex.GetType().Name} {ex.Message}\r\n栈首: {App.TrimStack(ex.StackTrace)}");
            TxtPercent.Text = "保存对话框打不开, 可直接在路径框输入保存位置。";
            return;
        }
        if (file != null && !string.IsNullOrEmpty(file.Path)) TxtDest.Text = file.Path;
    }

    // ---- 开始压缩 ----
    private async void OnStart(object? sender, RoutedEventArgs e)
    {
        if (_started) return;
        var dest = TxtDest.Text.Trim().Trim('"');
        if (dest.Length == 0) { TxtDest.Text = DefaultDest(); return; }
        if (!dest.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) dest += ".zip";

        PanelProgress.Visibility = Visibility.Visible;
        string destFull;
        try { destFull = Path.GetFullPath(dest); }
        catch
        {
            TxtPercent.Text = "压缩包位置不是合法路径, 请修改后重试。";
            return;
        }
        // 校验: 目标不能与所选内容重合, 所在文件夹必须存在
        if (_paths.Any(p => string.Equals(Path.GetFullPath(p).TrimEnd('\\', '/'), destFull.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)))
        {
            TxtPercent.Text = "压缩包不能就是所选文件本身, 请换个位置或名称。";
            return;
        }
        var destDir = Path.GetDirectoryName(destFull);
        if (string.IsNullOrEmpty(destDir) || !Directory.Exists(destDir))
        {
            TxtPercent.Text = $"目标文件夹不存在: {destDir}";
            return;
        }

        _started = true;
        TxtDest.IsEnabled = false;
        BtnBrowse.IsEnabled = false;
        BtnStart.IsEnabled = false;
        TxtDest.Text = dest;
        BarProgress.Visibility = Visibility.Visible;

        var items = _paths.ToList();
        var sources = string.Join(";", items); // v89: 来源进历史, 可搜索可追溯
        // v80 真机定案: 与解压窗同款——Progress 已自带界面派发, 手动 TryEnqueue 属多余且是崩点, 拆除
        var progress = new Progress<double>(p =>
        {
            try
            {
                if (!_done && double.IsFinite(p))
                {
                    var c = Math.Clamp(p, 0, 100);
                    BarProgress.Value = c;
                    _lastPct = c;
                    _samples.Add((DateTime.Now, c));
                    TrimSamples();
                    TxtPercent.Text = $"压缩中 {c:F1}% · {ComputeEta()}";
                }
            }
            catch (Exception ex)
            {
                // 带栈落盘, 不再无声崩进程
                ThemeService.LogEvent($"压缩进度回调异常: {ex.GetType().Name} {ex.Message}\r\n栈首: {App.TrimStack(ex.StackTrace)}");
            }
        });
        try
        {
            var task = Task.Run(() => _svc.CreateZip(items, dest, progress));
            // v78 后台观察员: 压缩任务的结局由后台线程直接落盘, 不再依赖界面线程活着
            _ = task.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    var ex = t.Exception?.InnerException;
                    ThemeService.LogEvent($"压缩后台观察: 压缩任务异常退出 {ex?.GetType().Name} {ex?.Message}");
                }
                else
                {
                    ThemeService.LogEvent("压缩后台观察: 压缩任务正常结束");
                }
            }, TaskScheduler.Default);
            await task;
            _done = true;
            _etaTimer.Stop();
            BarProgress.Value = 100;
            TxtPercent.Text = $"压缩完成 —— {items.Count} 项 → {dest}";
            _hist.Add("压缩", dest, "", true, sources: sources); // ArchivePath=产物 zip, "全部删除"删的也是产物, 绝不碰源
            if (_inTray)
                _tray?.Balloon("TaoZip 压缩完成", $"{items.Count} 项已压缩到 {dest}，点击托盘图标查看详情。");
            else
            {
                await Task.Delay(1500);
                Close();
            }
        }
        catch (Exception ex)
        {
            _done = true;
            _etaTimer.Stop();
            TxtPercent.Text = $"压缩失败: {ex.Message}";
            _hist.Add("压缩", dest, "", false, ex.Message, sources);
            // 失败允许改目标重试, 不锁死界面
            _started = false;
            TxtDest.IsEnabled = true;
            BtnBrowse.IsEnabled = true;
            BtnStart.IsEnabled = true;
            BtnStart.Content = "重试";
            try { File.Delete(dest); } catch { } // 清掉半截压缩包, 不留垃圾
            if (_inTray) _tray?.Balloon("TaoZip 压缩失败", ex.Message);
        }
    }

    // ---- 剩余时间估算: 与解压窗同口径 (最近 10 秒滚动速度) ----
    private void TrimSamples()
    {
        var now = DateTime.Now;
        while (_samples.Count > 2 && (now - _samples[0].t).TotalSeconds > 10) _samples.RemoveAt(0);
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
        if ((DateTime.Now - t1).TotalSeconds > 10) return "剩余时间估算中…";
        if (p1 >= 99.9) return "即将完成…";
        var remain = Math.Max(0, (100 - p1) / (dp / dt));
        if (remain < 60) return $"预计剩余 {remain:F0} 秒";
        if (remain < 3600) return $"预计剩余 {(int)(remain / 60)} 分 {remain % 60:F0} 秒";
        return $"预计剩余约 {(int)(remain / 3600)} 小时 {(int)(remain % 3600 / 60)} 分";
    }

    // ---- 转后台: 隐藏窗口, 托盘接管 ----
    private void OnBackground(object? sender, RoutedEventArgs e)
    {
        if (_inTray || !_started || _done) return; // 未开始/已结束不进托盘
        if (_tray is null) _tray = new TrayIcon(WinRT.Interop.WindowNative.GetWindowHandle(this), "TaoZip 后台压缩中 — 点击查看进度", ShowFromTray);
        _inTray = true;
        AppWindow.Hide();
        _tray.Balloon("TaoZip 后台压缩中", "已最小到托盘，点击图标随时查看进度。");
    }

    private void ShowFromTray()
    {
        _inTray = false;
        AppWindow.Show();
        Activate();
    }
}

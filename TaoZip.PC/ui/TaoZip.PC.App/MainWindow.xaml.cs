using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using TaoZip.Core;

namespace TaoZip.PC.App;

/// <summary>主界面: 打开/解压/压缩/测试/历史, 支持拖入压缩包或待压缩文件。</summary>
public sealed partial class MainWindow : Window
{
    private readonly ArchiveService _svc = new();
    private readonly HistoryStore _hist = new(GetAppDataDir());
    private string? _currentArchive;
    // v86: 历史记录整体重做 —— _historyAll 为落盘全量, _historyView 为搜索过滤后的当前视图;
    // 每行自带删除按钮, 搜索框即输即筛。
    private List<TaoZip.Core.HistoryEntry> _historyAll = new();
    private List<TaoZip.Core.HistoryEntry> _historyView = new();
    private bool _historyMode;
    // v89: 记录文件监视 —— 右键小窗 (独立进程) 写入时, 主窗历史页自动重载刷新
    private System.IO.FileSystemWatcher? _histWatcher;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _histReloadDeb;

    private bool _suppressThemeChange = true;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        // WinUI3 窗口尺寸须用 AppWindow 设置
        var winSize = new Windows.Graphics.SizeInt32();
        winSize.Width = 960;
        winSize.Height = 640;
        AppWindow.Resize(winSize);
        AppWindow.Title = "TaoZip";
        AppIcon.Apply(AppWindow);
        // 主题下拉初始选中上次选择 (水墨为默认), 初始化期间不触发切换;
        // 病根(v60): 启动只选下拉不落主题, 深色系统的机器开局三套全黑底——
        // Light 键字典在系统深色基调下不生效, 启动必须显式钉基调。
        var themeId = new ThemeStore(App.AppDir()).Load();
        CmbTheme.SelectedIndex = themeId;
        ThemeService.Apply(this, themeId);
        _suppressThemeChange = false;
        // 旧版本残留的右键注册表键若指向已删除的旧程序, 会遮蔽新键导致菜单不生效, 启动时静默修复
        FileAssoc.RepairStaleCommands();
        EnsureHistoryWatcher(); // v89: 记录文件一有变化 (含右键小窗写入) 即防抖刷新
        HandleCommandLine();
        // v71 诊断基线: 主窗内容正常渲染, 自检结果作为小窗空白问题的对照组
        Activated += OnFirstActivated;
    }

    private bool _diagDone;
    private void OnFirstActivated(object sender, WindowActivatedEventArgs e)
    {
        if (_diagDone) return;
        _diagDone = true;
        ThemeService.DiagnoseWindow(this, "主窗");
    }

    /// <summary>切换主题：即时生效并记住（水墨为系列默认）。</summary>
    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressThemeChange || CmbTheme.SelectedIndex < 0) return;
        var themeId = CmbTheme.SelectedIndex;
        ThemeService.Apply(this, themeId);
        new ThemeStore(App.AppDir()).Save(themeId);
        Status(themeId == 0 ? "已切换到水墨主题" : themeId == 1 ? "已切换到天青主题" : "已切换到墨夜主题");
    }

    // ---- 启动参数: 双击压缩包打开 ----
    private void HandleCommandLine()
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (args.Length == 0) return;
        var target = args.FirstOrDefault(IsArchivePath);
        if (target != null && File.Exists(target)) OpenArchive(target);
    }

    // ---- 文件关联设置 ----
    private async void OnFileAssoc(object sender, RoutedEventArgs e)
    {
        var panel = new StackPanel { Spacing = 8 };
        var boxes = new List<(string Ext, CheckBox Box)>();
        foreach (var ext in FileAssoc.Extensions)
        {
            var cb = new CheckBox
            {
                Content = new TextBlock { Text = $"{ext} —— 设为默认打开方式" },
                IsChecked = FileAssoc.IsDefault(ext)
            };
            boxes.Add((ext, cb));
            panel.Children.Add(cb);
        }
        var ctxExtract = new CheckBox
        {
            Content = new TextBlock { Text = "右键菜单 —— 使用TaoZip解压到此处 (全部格式)" },
            IsChecked = FileAssoc.AllContextMenu()
        };
        panel.Children.Add(ctxExtract);
        var ctxCompress = new CheckBox
        {
            Content = new TextBlock { Text = "右键菜单 —— 使用TaoZip压缩 (任意文件与文件夹, 支持多选)" },
            IsChecked = FileAssoc.HasCompressMenu()
        };
        panel.Children.Add(ctxCompress);

        var dialog = new ContentDialog
        {
            Title = "文件关联与右键菜单",
            Content = panel,
            PrimaryButtonText = "应用",
            CloseButtonText = "取消",
            XamlRoot = (Content as FrameworkElement)?.XamlRoot!
        };
        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        foreach (var (ext, cb) in boxes)
        {
            if (cb.IsChecked == true && !FileAssoc.IsDefault(ext)) FileAssoc.SetDefault(ext);
            else if (cb.IsChecked == false && FileAssoc.IsDefault(ext)) FileAssoc.RemoveDefault(ext);
        }
        // 勾选即重写 (SetXxx 幂等): 不再因"键已存在"跳过, 否则指向旧路径的残留键永远刷不过来
        if (ctxExtract.IsChecked == true) FileAssoc.SetContextMenuAll();
        else if (ctxExtract.IsChecked == false && FileAssoc.AllContextMenu()) FileAssoc.RemoveContextMenuAll();
        if (ctxCompress.IsChecked == true) FileAssoc.SetCompressMenuAll();
        else if (ctxCompress.IsChecked == false && FileAssoc.HasCompressMenu()) FileAssoc.RemoveCompressMenuAll();
        FileAssoc.RefreshExplorer();
        Status("文件关联已更新 (默认打开方式 / 右键解压 / 右键压缩)");
    }

    private static string GetAppDataDir() => TaoPaths.DataDir("TaoZip");

    // 格式识别的唯一事实来源在 ArchiveKinds (知常曰明), 界面只问不背
    private static bool IsArchivePath(string path) => ArchiveKinds.IsArchive(path);

    private void Status(string text) => StatusText.Text = text;

    /// <summary>空状态提示随列表虚实显隐 (负阴抱阳: 安顿好"无")。</summary>
    private void UpdateEmptyHint()
        => EmptyHint.Visibility = ListMain.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    // ---- 选择器: 统一初始化, 大道至简 ----
    private Windows.Storage.Pickers.FileOpenPicker NewOpenPicker()
    {
        var p = new Windows.Storage.Pickers.FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(p, WinRT.Interop.WindowNative.GetWindowHandle(this));
        p.FileTypeFilter.Add("*");
        return p;
    }

    private Windows.Storage.Pickers.FolderPicker NewFolderPicker()
    {
        var p = new Windows.Storage.Pickers.FolderPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(p, WinRT.Interop.WindowNative.GetWindowHandle(this));
        p.FileTypeFilter.Add("*");
        return p;
    }

    private Windows.Storage.Pickers.FileSavePicker NewSavePicker(string suggested)
    {
        var p = new Windows.Storage.Pickers.FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(p, WinRT.Interop.WindowNative.GetWindowHandle(this));
        p.SuggestedFileName = suggested;
        return p;
    }

    // ---- 打开压缩包: 列出条目 ----
    private async void OnOpen(object sender, RoutedEventArgs e)
    {
        var file = await NewOpenPicker().PickSingleFileAsync();
        if (file == null) return;
        OpenArchive(file.Path);
    }

    private void OpenArchive(string path)
    {
        try
        {
            var entries = _svc.ListEntries(path);
            _currentArchive = path;
            ExitHistoryMode();
            SetArchiveButtons(true);
            ListMain.Items.Clear();
            foreach (var (name, isDir, size) in entries)
            {
                var sizeText = isDir ? "<目录>" : $"{size / 1024.0:F1} KB";
                ListMain.Items.Add(new TextBlock { Text = $"  {name}    {sizeText}" });
            }
            UpdateEmptyHint();
            Status($"已打开 {Path.GetFileName(path)} —— 共 {ListMain.Items.Count} 项");
            // v90: "所有操作都有记录"——打开浏览成功也记一条 (此前只有打开失败才记)
            _hist.Add("浏览", path, "", true, $"共 {ListMain.Items.Count} 项");
        }
        catch (Exception ex)
        {
            Status($"打开失败: {ex.Message}");
            _hist.Add("打开", path, "", false, ex.Message);
        }
    }

    /// <summary>解压/测试类按钮随「是否已打开压缩包」联动, 未打开时置灰防误点。</summary>
    private void SetArchiveButtons(bool enabled)
    {
        BtnExtract.IsEnabled = enabled;
        BtnSmart.IsEnabled = enabled;
        BtnTest.IsEnabled = enabled;
    }

    // ---- 解压: 先选目标文件夹, 再执行 ----
    private async void OnExtract(object sender, RoutedEventArgs e)
    {
        if (_currentArchive == null) { Status("请先打开一个压缩包"); return; }
        var folder = await NewFolderPicker().PickSingleFolderAsync();
        if (folder == null) return;
        string extractSrc = _currentArchive;
        string extractDest = Path.Combine(folder.Path, Path.GetFileNameWithoutExtension(extractSrc));
        var n = 2;
        while (Directory.Exists(extractDest)) extractDest = Path.Combine(folder.Path, $"{Path.GetFileNameWithoutExtension(extractSrc)}({n++})");
        Status($"解压到 {extractDest} …");
        await RunJob("解压", p => { var r = _svc.Extract(extractSrc, extractDest, p); return $"解压完成: {r.Files} 个文件 → {extractDest}"; }, extractDest);
    }

    private void OnSmartExtract(object sender, RoutedEventArgs e)
    {
        if (_currentArchive == null) { Status("请先打开一个压缩包"); return; }
        string smartSrc = _currentArchive;
        string smartDest = ArchiveService.DefaultDestFor(smartSrc);
        Status($"智能解压到 {smartDest} …");
        await RunJob("智能解压", p => { var r = _svc.SmartExtract(smartSrc, smartDest, p); return $"智能解压完成: {r.Files} 个文件 → {smartDest}"; }, smartDest);
    }

    // ---- 完整性测试 ----
    private async void OnTest(object sender, RoutedEventArgs e)
    {
        if (_currentArchive == null) { Status("请先打开一个压缩包"); return; }
        string archive = _currentArchive;
        Status($"正在测试 {Path.GetFileName(archive)} …");
        try
        {
            var (n, bytes) = await System.Threading.Tasks.Task.Run(() => _svc.Test(archive));
            Status($"测试通过: {n} 个条目, 解压后约 {bytes / 1024.0 / 1024.0:F1} MB");
            _hist.Add("测试", archive, "", true);
        }
        catch (Exception ex)
        {
            Status($"测试失败: {Path.GetFileName(archive)} 可能已损坏 —— {ex.Message}");
            _hist.Add("测试", archive, "", false, ex.Message);
        }
    }

    /// <summary>选择器任一步失败: 界面明示 + 落盘 (带栈首), 杜绝"点了没反应"式静默失败。</summary>
    private void PickFail(string step, Exception ex)
    {
        ThemeService.LogEvent($"{step}失败: {ex.GetType().Name} {ex.Message}\r\n栈首: {App.TrimStack(ex.StackTrace)}");
        Status($"{step}失败: {ex.Message} —— 已记录日志, 请把 {TaoZip.Core.TaoPaths.DataDir("TaoZip")}\\theme.log 尾部发给开发助手");
    }

    // ---- 历史: 显式文字色 + 行内删除 + 搜索过滤 (v86 重做) ----
    private void OnHistory(object sender, RoutedEventArgs e)
    {
        _historyMode = true;
        HistoryBar.Visibility = Visibility.Visible;
        TxtHistSearch.Text = "";
        _historyAll = _hist.Load();
        RebuildHistory();
        Status($"历史记录 —— 共 {_historyAll.Count} 条 (双击条目定位压缩包所在文件夹; 行尾可删除)");
    }

    /// <summary>退出历史模式: 工具条收起, 搜索清零。</summary>
    private void ExitHistoryMode()
    {
        _historyMode = false;
        HistoryBar.Visibility = Visibility.Collapsed;
        TxtHistSearch.Text = "";
        _historyAll.Clear();
        _historyView.Clear();
    }

    /// <summary>按搜索词重建历史列表; 文字显式钉主色, 不依赖列表项默认前景。</summary>
    private void RebuildHistory()
    {
        var filter = TxtHistSearch.Text.Trim();
        bool Match(TaoZip.Core.HistoryEntry h) => filter.Length == 0
            || h.ArchivePath.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || h.DestPath.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || h.Action.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || h.Message.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || h.Sources.Contains(filter, StringComparison.OrdinalIgnoreCase);
        _historyView = _historyAll.Where(Match).ToList();

        var primary = Application.Current.Resources["TextFillColorPrimaryBrush"] as Microsoft.UI.Xaml.Media.Brush;
        ListMain.Items.Clear();
        for (var i = 0; i < _historyView.Count; i++)
        {
            var h = _historyView[i];
            var text = $"  [{h.Time:MM-dd HH:mm}] {h.Action}  {h.ArchivePath}"
                       + (string.IsNullOrEmpty(h.DestPath) ? "" : $"  →  {h.DestPath}")
                       + (h.Ok ? "  ✓" : $"  ✗ {h.Message}");
            var line = new TextBlock
            {
                Text = text,
                TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis,
                MaxLines = 2,
                VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
                Foreground = h.Ok ? primary : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.IndianRed)
            };
            var del = new Button
            {
                Content = "删除",
                Tag = i,
                Padding = new Microsoft.UI.Xaml.Thickness(10, 2, 10, 2),
                VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center
            };
            del.Click += OnHistDeleteRow;
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(line, 0);
            Grid.SetColumn(del, 1);
            row.Children.Add(line);
            row.Children.Add(del);
            ListMain.Items.Add(row);
        }
        UpdateEmptyHint();
        // 渲染自证: 行数 + 首行字数落盘, 真机若再现"有行无字", 日志可判是数据空还是渲染空
        try
        {
            var firstLen = _historyView.Count > 0
                ? (ListMain.Items[0] is Grid g && g.Children[0] is TextBlock tb ? tb.Text.Length : -1)
                : 0;
            ThemeService.LogEvent($"历史渲染: 过滤\"{filter}\" → {ListMain.Items.Count} 行, 首行字数={firstLen}");
        }
        catch { /* 诊断落盘失败静默 */ }
    }

    private void OnHistSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (_historyMode) RebuildHistory();
    }

    /// <summary>删除单条记录; 可勾选连同删除本地压缩包文件。</summary>
    private async void OnHistDeleteRow(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not int idx) return;
        if (idx < 0 || idx >= _historyView.Count) return;
        var entry = _historyView[idx];

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(entry.ArchivePath) ? "(无路径)" : entry.ArchivePath,
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush
        });
        var alsoFile = new CheckBox { Content = new TextBlock { Text = "同时删除本地压缩包文件 (若存在)" }, IsChecked = false };
        panel.Children.Add(alsoFile);
        var dialog = new ContentDialog
        {
            Title = "删除这条历史记录?",
            Content = panel,
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            XamlRoot = (Content as FrameworkElement)?.XamlRoot!
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var note = "";
        if (alsoFile.IsChecked == true && !string.IsNullOrEmpty(entry.ArchivePath))
        {
            try
            {
                if (File.Exists(entry.ArchivePath)) { File.Delete(entry.ArchivePath); note = " 本地文件已一并删除。"; }
                else note = " (本地文件本就不存在)";
            }
            catch (Exception ex) { note = $" 本地文件删除失败: {ex.Message}"; }
        }
        // v90: 删除走锁内按内容匹配 (ReferenceEquals 只能匹配本地实例, 锁内重读盘上表对不上),
        // 右键小窗并发写入的记录不会被旧表回存覆盖
        _hist.RemoveEntry(entry);
        _historyAll = _hist.Load();
        RebuildHistory();
        Status($"已删除 1 条记录。{note}");
    }

    /// <summary>清理全部: 只清空记录, 不动本地文件。</summary>
    private async void OnHistClear(object sender, RoutedEventArgs e)
    {
        if (_historyAll.Count == 0) { Status("历史记录已是空的"); return; }
        var dialog = new ContentDialog
        {
            Title = "清理全部",
            Content = new TextBlock
            {
                Text = $"确定清空全部 {_historyAll.Count} 条记录?\r\n只清记录, 不动本地文件。",
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
            },
            PrimaryButtonText = "清理",
            CloseButtonText = "取消",
            XamlRoot = (Content as FrameworkElement)?.XamlRoot!
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var n = _historyAll.Count;
        // v90: 锁内清空, 右键小窗并发写入的记录不会被旧空表覆盖
        _hist.RemoveAll(_ => true);
        _historyAll = _hist.Load();
        RebuildHistory();
        Status($"已清空 {n} 条记录 (本地文件未动)。");
    }

    /// <summary>全部删除: 清空记录, 勾选后连同删除记录指向的本地压缩包文件。
    /// 删除在后台逐个进行: 文件不在/被占用/无权限都就地记因, 绝不卡住界面;
    /// 完成后弹统计报告, 点「关闭」立即关, 不点 3 秒自动关。</summary>
    private async void OnHistPurge(object sender, RoutedEventArgs e)
    {
        if (_historyAll.Count == 0) { Status("历史记录已是空的"); return; }
        var panel = new StackPanel { Spacing = 8 };
        var alsoFiles = new CheckBox
        {
            Content = new TextBlock { Text = "同时删除记录指向的本地压缩包文件 (不可恢复, 谨慎勾选)" },
            IsChecked = false
        };
        panel.Children.Add(new TextBlock
        {
            Text = $"共 {_historyAll.Count} 条记录。\"全部删除\"会清空全部记录;\r\n勾选后还会把记录指向的本地压缩包一并删除。",
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
        });
        panel.Children.Add(alsoFiles);
        var dialog = new ContentDialog
        {
            Title = "全部删除",
            Content = panel,
            PrimaryButtonText = "全部删除",
            CloseButtonText = "取消",
            XamlRoot = (Content as FrameworkElement)?.XamlRoot!
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var total = _historyAll.Count;
        var targets = _historyAll.Select(h => h.ArchivePath)
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        List<(string Path, bool Deleted, string Reason)> results = new();
        if (alsoFiles.IsChecked == true)
        {
            // 后台线程逐个删: 任何一种失败都记原因收场, 不让一个坏文件卡住整批
            results = await System.Threading.Tasks.Task.Run(() =>
            {
                var list = new List<(string Path, bool Deleted, string Reason)>();
                foreach (var p in targets)
                {
                    try
                    {
                        if (!File.Exists(p)) { list.Add((p, false, "文件已不存在")); continue; }
                        File.Delete(p);
                        list.Add((p, true, ""));
                    }
                    catch (UnauthorizedAccessException) { list.Add((p, false, "没有删除权限")); }
                    catch (System.IO.IOException) { list.Add((p, false, "文件被占用 (可能正被打开)")); }
                    catch (Exception ex) { list.Add((p, false, $"删除失败: {ex.Message}")); }
                }
                return list;
            });
            ThemeService.LogEvent($"历史-全部删除: 记录 {total} 条, 本地文件 {results.Count(r => r.Deleted)} 删 / {results.Count(r => !r.Deleted)} 未删");
        }

        // v90: 锁内清空, 右键小窗并发写入的记录不会被旧空表覆盖
        _hist.RemoveAll(_ => true);
        _historyAll = _hist.Load();
        RebuildHistory();

        if (alsoFiles.IsChecked == true)
            _ = ShowPurgeReport(total, results); // fire-and-forget (2026-11-14 审查黄2): 方法内已收口, 不再走 async void
        else
            Status($"已删除 {total} 条记录 (本地文件未动)。");
    }

    /// <summary>删除完成统计: 共选多少、删了多少、未删的各是什么原因;
    /// 点「关闭」立即关, 3 秒无操作自动关 (v88)。
    /// (2026-11-14 审查黄2) 非 async void: ShowAsync 异常就地落盘, 不穿出到 UnhandledException。</summary>
    private async Task ShowPurgeReport(int totalRecords, List<(string Path, bool Deleted, string Reason)> results)
    {
        var deleted = results.Count(r => r.Deleted);
        var failed = results.Where(r => !r.Deleted).ToList();
        var lines = new List<string>
        {
            $"共选择 {totalRecords} 条记录, 已全部删除。",
            results.Count == 0
                ? "未勾选删除本地文件, 文件全部保留。"
                : $"本地文件共 {results.Count} 个: 成功删除 {deleted} 个" + (failed.Count > 0 ? $" , 未删除 {failed.Count} 个:" : " 。"),
        };
        foreach (var f in failed.Take(12))
        {
            lines.Add($"· {Path.GetFileName(f.Path)} —— {f.Reason}");
        }
        if (failed.Count > 12)
        {
            lines.Add($"…其余 {failed.Count - 12} 项未删除, 详情见 theme.log。");
        }

        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = string.Join("\r\n", lines),
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
        });
        var dialog = new ContentDialog
        {
            Title = "删除完成",
            Content = panel,
            CloseButtonText = "关闭",
            XamlRoot = (Content as FrameworkElement)?.XamlRoot!
        };
        _ = AutoCloseDialog(dialog);
        try { await dialog.ShowAsync(); }
        catch (Exception ex) { ThemeService.LogEvent($"删除完成弹窗异常: {ex.GetType().Name} {ex.Message}\r\n栈首: {App.TrimStack(ex.StackTrace)}"); }
        Status($"已删除 {totalRecords} 条记录; 本地文件删除 {deleted} 个"
               + (failed.Count > 0 ? $", 未删除 {failed.Count} 个" : "") + "。");
    }

    /// <summary>3 秒无操作自动关闭; 用户先手动关闭时 Hide 属空操作, 异常就地消化。</summary>
    private static async System.Threading.Tasks.Task AutoCloseDialog(ContentDialog dialog)
    {
        await System.Threading.Tasks.Task.Delay(3000);
        try { dialog.Hide(); }
        catch { /* 已关闭 */ }
    }

    // ---- v89 记录文件监视: 数据任何变化 (本窗或右键小窗/压缩小窗写入) 都自动刷新 ----
    private void EnsureHistoryWatcher()
    {
        if (_histWatcher != null) return;
        try
        {
            var watcher = new System.IO.FileSystemWatcher
            {
                Path = TaoZip.Core.TaoPaths.DataDir("TaoZip"),
                Filter = "history.json",
                NotifyFilter = System.IO.NotifyFilters.LastWrite | System.IO.NotifyFilters.Size | System.IO.NotifyFilters.FileName
            };
            watcher.Changed += (_, _) => OnHistFileEvent();
            watcher.Created += (_, _) => OnHistFileEvent();
            watcher.Renamed += (_, _) => OnHistFileEvent();
            watcher.EnableRaisingEvents = true;
            _histWatcher = watcher;
            ThemeService.LogEvent("历史监视: 已启动, 记录文件任何变化将自动刷新");
        }
        catch (Exception ex) { ThemeService.LogEvent($"历史监视启动失败: {ex.GetType().Name} {ex.Message}"); }
    }

    // 事件在后台线程触发: 切回界面线程做防抖 (系统对一次写入常连发多个事件)
    private void OnHistFileEvent()
        => DispatcherQueue.TryEnqueue(() =>
        {
            if (_histReloadDeb == null)
            {
                _histReloadDeb = DispatcherQueue.CreateTimer();
                _histReloadDeb.Interval = TimeSpan.FromMilliseconds(300);
                _histReloadDeb.IsRepeating = false;
                _histReloadDeb.Tick += (_, _) => ReloadHistoryFromDisk();
            }
            _histReloadDeb.Stop();
            _histReloadDeb.Start();
        });

    private void ReloadHistoryFromDisk()
    {
        try
        {
            _historyAll = _hist.Load();
            if (_historyMode)
            {
                RebuildHistory();
                Status($"历史记录已刷新 —— 共 {_historyAll.Count} 条");
            }
        }
        catch (Exception ex) { ThemeService.LogEvent($"历史自动刷新失败: {ex.GetType().Name} {ex.Message}"); }
    }

    // ---- 历史双击: 在资源管理器中定位压缩包 ----
    private void OnListDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (!_historyMode) return;
        var idx = ListMain.SelectedIndex;
        if (idx < 0 || idx >= _historyView.Count) return;
        var path = _historyView[idx].ArchivePath;
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (File.Exists(path))
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            else
                Process.Start("explorer.exe", Path.GetDirectoryName(Path.GetFullPath(path)) ?? path);
        }
        catch (Exception ex) { Status($"打开文件夹失败: {ex.Message}"); }
    }

    // ---- 拖放: 压缩包直接打开, 普通文件/文件夹直接压缩 ----
    private void OnDragOver(object sender, DragEventArgs e) => e.AcceptedOperation = DataPackageOperation.Copy;

    private async void OnDrop(object sender, DragEventArgs e)
    {
        var items = await e.DataView.GetStorageItemsAsync();
        if (items.Count == 0) return;
        var first = items[0].Path;
        // 单个压缩包直接打开 (含 .tar.gz 等复合扩展名); 其余整体作为压缩来源
        if (items.Count == 1 && File.Exists(first) && IsArchivePath(first))
        {
            OpenArchive(first);
            return;
        }
        Windows.Storage.Pickers.FileSavePicker savePicker;
        try
        {
            savePicker = NewSavePicker("拖入内容.zip");
            savePicker.FileTypeChoices.Add("zip 压缩包", new List<string> { ".zip" });
        }
        catch (Exception ex) { PickFail("保存对话框初始化", ex); return; }
        Windows.Storage.StorageFile? file;
        try { file = await savePicker.PickSaveFileAsync(); }
        catch (Exception ex) { PickFail("保存对话框打开", ex); return; }
        if (file == null) return;
        var dropDest = file.Path;
        var dropItems = items.Select(i => i.Path).ToList();
        // 线程源头闸 (2026-11-14 审查红2): 目标与拖入内容重合时, File.Create 会先把源截断为零——在进任务前就拒绝, 与右键压缩小窗同口径
        var dropDestFull = Path.GetFullPath(dropDest);
        if (dropItems.Any(p => string.Equals(Path.GetFullPath(p), dropDestFull, StringComparison.OrdinalIgnoreCase)))
        {
            Status("压缩包不能就是拖入的文件本身, 请换个位置或名称。");
            return;
        }
        await RunJob("压缩", p => { _svc.CreateZip(dropItems, dropDest, p); return $"压缩完成 → {dropDest}"; }, archiveOverride: dropDest); // v89: 历史记产物 zip, 可定位可删
    }

    // ---- 通用任务执行 + 历史记录 ----
    private Task RunJob(string action, Func<string> job, string? dest = null)
        => RunJob(action, _ => job(), dest);

    // 非事件处理器 (2026-11-14 审查黄2): async Task 走 await 链, 异常经调用方收口, 不再走 async void 骨架
    private async Task RunJob(string action, Func<IProgress<double>, string> job, string? dest = null, string? archiveOverride = null)
    {
        string archive = archiveOverride ?? _currentArchive ?? ""; // v89: 压缩类条目可显式指定产物路径, 落在 ArchivePath 位
        Status($"{action}中…");
        try
        {
            // v80: 与子窗同款——Progress 已按界面同步上下文派发, 手动 TryEnqueue 属多余且是 NRE 崩点, 拆除
            var progress = new Progress<double>(v =>
            {
                try { Status($"{action}中 {v:F0}%…"); }
                catch (Exception ex) { ThemeService.LogEvent($"主窗进度回调异常: {ex.GetType().Name} {ex.Message}\r\n栈首: {App.TrimStack(ex.StackTrace)}"); }
            });
            var msg = await System.Threading.Tasks.Task.Run(() => job(progress));
            Status(msg + (dest != null ? $" → {dest}" : ""));
            _hist.Add(action, archive, dest ?? "", true);
        }
        catch (Exception ex)
        {
            Status($"{action}失败: {ex.Message}");
            _hist.Add(action, archive, dest ?? "", false, ex.Message);
        }
    }
}

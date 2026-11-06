using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TaoZip.Core;
using Xunit;
using Xunit.Abstractions;

namespace TaoZipOps;

/// <summary>
/// 操作之道（道生万物 · 维度 N）:
/// N1 界面动作全实现 —— 每个按钮的 Click 处理器都真实存在且非空 (所有按钮);
/// N2 入口全接通 —— 命令行三入口 (打开/解压/压缩) 各有其道 (所有操作);
/// N3 守卫闭环 —— 依赖压缩包的按钮出厂置灰、开门后启用 (防误点的负阴抱阳);
/// N4 换肤入口唯一 —— 三窗皆走 ThemeService.Apply, 不各自漂移;
/// N5 底随主题 —— 三窗根板引用主题底色笔刷 (隐身病根的永久断言);
/// C1-C4 组合操作 (Core 层真实运转): 链式往返 / 重复解压幂等 / 交叉格式 / 压缩再压缩。
/// </summary>
public class OperationTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _root;
    private readonly string _ui;
    private readonly ArchiveService _svc = new();

    public OperationTests(ITestOutputHelper output)
    {
        _out = output;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? root = null;
        for (var d = dir; d != null && root is null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "tools", "msi", "package.ps1")))
                root = d.FullName;
        Assert.False(root is null, "未定位到源码树根");
        Root = root!;
        _ui = Path.Combine(Root, "ui", "TaoZip.PC.App");
        _root = Path.Combine(Path.GetTempPath(), "TaoZipOps_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    private string Root { get; }
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private string ReadUi(string file) => File.ReadAllText(Path.Combine(_ui, file));

    // ---- N1 界面动作全实现: 所有按钮的处理器存在且非空 ----

    [Fact]
    public void N1_Operations_EveryButtonHandlerExistsAndNotEmpty()
    {
        int wired = 0;
        wired += AssertWindowWired("MainWindow.xaml", "MainWindow.xaml.cs");
        wired += AssertWindowWired("ExtractWindow.xaml", "ExtractWindow.xaml.cs");
        wired += AssertWindowWired("CompressWindow.xaml", "CompressWindow.xaml.cs");
        _out.WriteLine($"N1 已接线的界面按钮: {wired} 个");
        Assert.True(wired >= 10, $"接线按钮仅 {wired} 个, 少于预期");
    }

    private int AssertWindowWired(string xamlFile, string codeFile)
    {
        var xaml = ReadUi(xamlFile);
        var code = ReadUi(codeFile);
        var handlers = Regex.Matches(xaml, "Click=\"([^\"]+)\"").Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.True(handlers.Count > 0, $"{xamlFile} 无任何按钮动作");
        foreach (var h in handlers)
        {
            // 处理器存在
            var m = Regex.Match(code, $@"(?:private|public|internal)?\s*(?:async\s+)?void\s+{Regex.Escape(h)}\s*\(");
            Assert.True(m.Success, $"{xamlFile} 按钮 {h} 无对应处理器");
            // 处理器非空 (方法体内至少一条语句)
            var bodyStart = code.IndexOf('{', m.Index);
            Assert.True(bodyStart > 0, $"{xamlFile} 按钮 {h} 处理器无方法体");
            var body = ReadBalanced(code, bodyStart);
            var meaningful = Regex.Matches(body, @"\w\s*\(|;\s*").Count;
            Assert.True(meaningful >= 1, $"{xamlFile} 按钮 {h} 处理器是空壳");
        }
        return handlers.Count;
    }

    private static string ReadBalanced(string text, int openPos)
    {
        int depth = 0;
        for (int i = openPos; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}') { depth--; if (depth == 0) return text[openPos..(i + 1)]; }
        }
        return text[openPos..];
    }

    // ---- N2 入口全接通: 打开 / 解压到此处 / 压缩 三条命令行道 ----

    [Fact]
    public void N2_Operations_AllCommandlineEntriesWired()
    {
        var app = ReadUi("App.xaml.cs");
        Assert.Contains("/extract-here", app);
        Assert.Contains("/compress-here", app);
        Assert.Contains("new ExtractWindow", app);
        Assert.Contains("new CompressWindow", app);
        var main = ReadUi("MainWindow.xaml.cs");
        Assert.Contains("HandleCommandLine", main);
        Assert.Contains("OpenArchive", main);
        // 核心层必须有对应的智能落位与压缩能力供入口调用
        var core = File.ReadAllText(Path.Combine(Root, "src", "TaoZip.Core", "ArchiveService.cs"));
        Assert.Contains("SmartExtract", core);
        Assert.Contains("CreateZip", core);
        Assert.Contains("CreateTarGz", core);
    }

    // ---- N3 守卫闭环: 依赖压缩包的按钮出厂置灰, 开门后启用 ----

    [Fact]
    public void N3_Operations_GuardButtonsDisabledUntilArchiveOpen()
    {
        var xaml = ReadUi("MainWindow.xaml");
        // 出厂即置灰 (XAML IsEnabled=False): 解压到…/智能解压/完整性测试
        Assert.Equal(3, Regex.Matches(xaml, "IsEnabled=\"False\"").Count);
        var code = ReadUi("MainWindow.xaml.cs");
        Assert.Matches(@"SetArchiveButtons\(true\)", code);
    }

    // ---- N4 换肤入口唯一: 启动处统一 Apply, 进度窗不得自带主题 ----

    [Fact]
    public void N4_Operations_SingleThemeEntryForAllWindows()
    {
        var app = ReadUi("App.xaml.cs");
        Assert.Contains("ThemeService.Apply(_window", app);
        // v58 病根封锁 → v73 改判 → v76 真机再证: 单次赋值后水墨↔天青(同浅底)互切
        // 停在旧配色——字典摘挂不重估 ThemeResource, 重估只认基调变化。
        // v77 定案: 基调在变一次赋值; 基调不变先反再落强制重估(同栈完成, 无中间渲染帧)。
        var theme = ReadUi("ThemeService.cs");
        Assert.Contains("if (root.ActualTheme != requested)", theme);
        Assert.Contains("root.RequestedTheme = requested == ElementTheme.Light ? ElementTheme.Dark : ElementTheme.Light;", theme);
        // v74: TransitionCollection 在 WinUI3 命名空间与文档不符, 该行整体移除, 禁止回加
        Assert.DoesNotContain("TransitionCollection", theme);
        Assert.DoesNotContain("root.RequestedTheme = before == ElementTheme.Dark", theme);
        Assert.Contains("LogApply(", theme);
        // v82: 换肤日志必须记"切换前"基调——取证(before)必须在赋值之前,
        // 否则 before/after 永远同值 (真机实证: 浅→墨夜记成 Dark→Dark, 误导诊断)。
        Assert.Contains("var before = root.ActualTheme;", theme);
        Assert.Contains("LogApply(themeId, before, root.ActualTheme);", theme);
        // v90: 历史记录并发安全与全操作覆盖
        var mainWin = ReadUi("MainWindow.xaml.cs");
        Assert.Contains("_hist.Add(\"浏览\", path, \"\", true", mainWin);       // 打开浏览成功也记
        Assert.Contains("_hist.RemoveEntry(entry);", mainWin);                  // 删除走锁内签名删除
        Assert.DoesNotContain("_hist.Save(_historyAll)", mainWin);              // 旧表整体回存禁令 (会覆盖右键并发写入)
        var store = System.IO.File.ReadAllText(Path.Combine(Root, "src", "TaoZip.Core", "HistoryStore.cs"));
        Assert.Contains("WithFileLock", store);
        Assert.Contains("RemoveEntry", store);
        foreach (var f in new[] { "ExtractWindow.xaml.cs", "CompressWindow.xaml.cs" })
        {
            Assert.DoesNotContain("ThemeService.Apply", ReadUi(f));
            // v63: 子窗不另 Apply, 但标题栏须随象钉色 (内容浅/标题栏黑拼色之封锁)
            Assert.Contains("ThemeService.PinSubTitleBar(this)", ReadUi(f));
        }
        // 主窗仅两处合法入口: 启动钉基调 (v60, 深色系统开局全黑底之封锁) + 用户切换主题
        var main = ReadUi("MainWindow.xaml.cs");
        Assert.Equal(2, Regex.Matches(main, "ThemeService\\.Apply").Count);
        Assert.Contains("ThemeService.Apply(this, themeId)", main);
    }

    // ---- N5 底随主题 ----

    [Fact]
    public void N5_Operations_AllWindowsUseThemeBackground()
    {
        foreach (var f in new[] { "MainWindow.xaml", "ExtractWindow.xaml", "CompressWindow.xaml" })
            Assert.Contains("Background=\"{ThemeResource ApplicationPageBackgroundThemeBrush}\"", ReadUi(f));

        // v66 真人视角: 子窗不留白露出系统默认底——根板 Grid 铺满, 文字/进度条钉死主题色
        var ext = ReadUi("ExtractWindow.xaml");
        Assert.Matches(@"x:Name=""TxtFile""[^>]*Foreground=""\{ThemeResource TextFillColorPrimaryBrush\}""", ext);
        Assert.Matches(@"x:Name=""BarProgress""[^>]*Foreground=""\{ThemeResource AccentFillColorDefaultBrush\}""", ext);
        var cmp = ReadUi("CompressWindow.xaml");
        Assert.Matches(@"x:Name=""TxtSummary""[^>]*Foreground=""\{ThemeResource TextFillColorPrimaryBrush\}""", cmp);
        Assert.Matches(@"x:Name=""BarProgress""[^>]*Foreground=""\{ThemeResource AccentFillColorDefaultBrush\}""", cmp);

        // v71 自愈: 子窗必须带纯代码重建兜底, XAML 内容未渲染时自救 (真机空白小窗之最后防线)
        Assert.Contains("RebuildInCode", ReadUi("ExtractWindow.xaml.cs"));
        Assert.Contains("RebuildInCode", ReadUi("CompressWindow.xaml.cs"));
        Assert.Contains("RootGrid", ReadUi("ExtractWindow.xaml"));
        Assert.Contains("RootGrid", ReadUi("CompressWindow.xaml"));
    }

    // ---- N6 三象归一: 水墨/天青/墨夜, 键集同源, 各守其色 ----

    [Fact]
    public void N6_Operations_ThreeThemesOneSource()
    {
        var ink = File.ReadAllText(Path.Combine(_ui, "Themes", "InkTheme.xaml"));
        var sky = File.ReadAllText(Path.Combine(_ui, "Themes", "SkyTheme.xaml"));
        var dark = File.ReadAllText(Path.Combine(_ui, "Themes", "TaoDark.xaml"));

        // 三象各有其字典, v61 起一律扁平 (不分段); 主窗恰好三项, 名实相符
        Assert.Contains("x:Key=\"ApplicationPageBackgroundThemeBrush\"", ink);
        Assert.Contains("x:Key=\"ApplicationPageBackgroundThemeBrush\"", sky);
        Assert.Contains("x:Key=\"ApplicationPageBackgroundThemeBrush\"", dark);
        var main = ReadUi("MainWindow.xaml");
        Assert.Equal(3, Regex.Matches(main, "<ComboBoxItem Content=\"").Count);
        foreach (var name in new[] { "水墨", "天青", "墨夜" })
            Assert.Contains($"Content=\"{name}\"", main);
        Assert.DoesNotContain("浅色", main);
        Assert.DoesNotContain("深色", main);
        Assert.Contains("已切换到天青主题", ReadUi("MainWindow.xaml.cs"));
        Assert.Contains("SkyTheme", ReadUi("ThemeService.cs"));

        // 键集同源: 四根支柱三象皆备
        foreach (var dict in new[] { ink, sky, dark })
            foreach (var key in new[]
            {
                "ApplicationPageBackgroundThemeBrush", "TextFillColorPrimaryBrush",
                "AccentFillColorDefaultBrush", "WindowCaptionBackgroundBrush"
            })
                Assert.Contains($"x:Key=\"{key}\"", dict);

        // 天青: 近白偏青浅调 (底 #EDF2EF 通道序 G>B>R, 点缀青瓷 #34655C)
        Assert.Contains("ApplicationPageBackgroundThemeBrush\" Color=\"#EDF2EF\"", sky);
        Assert.Contains("AccentFillColorDefaultBrush\" Color=\"#34655C\"", sky);

        // 墨夜铁律: 以黑为主 (近黑 #131211, 暖序 R>G>B), 宣纸白, 黛青
        Assert.Contains("ApplicationPageBackgroundThemeBrush\" Color=\"#131211\"", dark);
        Assert.Contains("TextFillColorPrimaryBrush\" Color=\"#E8E3D5\"", dark);
        Assert.Contains("AccentFillColorDefaultBrush\" Color=\"#6FA3BC\"", dark);

        // v61 治本: 三套字典必须扁平 (不分 Light/Dark 段, 与系统基调脱钩, 深色系统水墨不再黑底)
        foreach (var f in new[] { "InkTheme.xaml", "SkyTheme.xaml", "TaoDark.xaml" })
            Assert.DoesNotContain("ThemeDictionaries", File.ReadAllText(Path.Combine(_ui, "Themes", f)));

        // v62 真人视角: 窗口中间不许隐形——列表区自带层色底, 空态提示用次要字 (不用最淡的三级灰);
        // 子窗根板必挂主题底色笔刷 (随象而染)
        var mainXaml = ReadUi("MainWindow.xaml");
        Assert.Contains("x:Name=\"ListMain\"", mainXaml);
        Assert.Matches(@"x:Name=""ListMain""[^>]*Background=""\{ThemeResource LayerFillColorDefaultBrush\}""", mainXaml);
        Assert.Matches(@"x:Name=""EmptyHint""[^>]*Foreground=""\{ThemeResource TextFillColorSecondaryBrush\}""", mainXaml);
        foreach (var f in new[] { "ExtractWindow.xaml", "CompressWindow.xaml" })
            Assert.Contains("Background=\"{ThemeResource ApplicationPageBackgroundThemeBrush}\"", ReadUi(f));

        // 主题字典不再常驻应用级, 三象互斥只挂当象 (ThemeService 摘净三套再挂一个)
        Assert.DoesNotContain("Themes/TaoDark.xaml", ReadUi("App.xaml"));
        var themeSvc = ReadUi("ThemeService.cs");
        Assert.Contains("RemoveTheme(resources, \"InkTheme\")", themeSvc);
        Assert.Contains("RemoveTheme(resources, \"SkyTheme\")", themeSvc);
        Assert.Contains("RemoveTheme(resources, \"TaoDark\")", themeSvc);
        Assert.Contains("NightSource", themeSvc);
    }

    // ---- N7 换肤周游: 启动必须钉基调, 基调映射浅/浅/黑 (真机"三套全黑底"病根封锁) ----

    [Fact]
    public void N7_Themes_StartupPinsBaseAndLadderMappingHolds()
    {
        // 基调映射唯一真相: 水墨/天青浅色, 墨夜深色 (系统深色机器开局全黑底之封锁)
        var svc = ReadUi("ThemeService.cs");
        Assert.Contains("themeId == 2 ? ElementTheme.Dark : ElementTheme.Light", svc);
        // 沙箱不编译 App 工程, Apply 必须先算出 requested 再用 (真机 CS0103 之封锁)
        Assert.Contains("var requested = BaseFor(themeId);", svc);
        Assert.Contains("root.RequestedTheme = requested;", svc);

        // 启动即落主题: 主窗构造必须显式 Apply (只选下拉不 Apply = 深色系统开局黑底)
        var main = ReadUi("MainWindow.xaml.cs");
        Assert.Contains("ThemeService.Apply(this, themeId)", main);

        // 标题栏三颗小按钮随象而染: 换肤落点必须给系统标题栏钉色
        Assert.Contains("PinTitleBar(window, root)", svc);
        Assert.Contains("ButtonForegroundColor = glyph", svc);
        Assert.Contains("PinSubTitleBar", svc);
    }

    // ---- C1 组合: 压缩→解压→再压缩 链式往返, 三代同宗 ----

    [Fact]
    public void C1_Combined_ChainedRoundTripsPreserveLineage()
    {
        var gen1 = Path.Combine(_root, "gen1");
        Directory.CreateDirectory(gen1);
        File.WriteAllBytes(Path.Combine(gen1, "始祖.dat"), new byte[] { 1, 2, 3, 4 });
        File.WriteAllText(Path.Combine(gen1, "家谱.txt"), "道生一");

        var zip1 = Path.Combine(_root, "一代.zip");
        _svc.CreateZip(new[] { gen1 }, zip1);
        var out1 = Path.Combine(_root, "out1");
        Directory.CreateDirectory(out1);
        _svc.Extract(zip1, out1);

        var zip2 = Path.Combine(_root, "二代.zip");
        _svc.CreateZip(new[] { Path.Combine(out1, "gen1") }, zip2);
        var out2 = Path.Combine(_root, "out2");
        Directory.CreateDirectory(out2);
        _svc.Extract(zip2, out2);

        AssertFileEqual(Path.Combine(gen1, "始祖.dat"), Path.Combine(out2, "gen1", "始祖.dat"));
        AssertFileEqual(Path.Combine(gen1, "家谱.txt"), Path.Combine(out2, "gen1", "家谱.txt"));
    }

    // ---- C2 组合: 同包重复解压至同目录, 幂等不增不减 ----

    [Fact]
    public void C2_Combined_RepeatedExtractIsIdempotent()
    {
        var src = Path.Combine(_root, "c2src");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "常.txt"), "repeat");
        var zip = Path.Combine(_root, "c2.zip");
        _svc.CreateZip(new[] { src }, zip);

        var dest = Path.Combine(_root, "c2out");
        Directory.CreateDirectory(dest);
        _svc.Extract(zip, dest);
        _svc.Extract(zip, dest);
        _svc.Extract(zip, dest);

        var files = Directory.GetFiles(dest, "*", SearchOption.AllDirectories);
        Assert.Single(files);
    }

    // ---- C3 组合: 同一批文件 zip 与 tar.gz 交叉解压, 结果同源 ----

    [Fact]
    public void C3_Combined_CrossFormatSameOrigin()
    {
        var src = Path.Combine(_root, "c3src");
        Directory.CreateDirectory(src);
        var payload = new byte[] { 9, 8, 7, 6, 5 };
        File.WriteAllBytes(Path.Combine(src, "真身.bin"), payload);

        var zip = Path.Combine(_root, "c3.zip");
        var tar = Path.Combine(_root, "c3.tar.gz");
        _svc.CreateZip(new[] { src }, zip);
        _svc.CreateTarGz(new[] { src }, tar);

        var outZip = Path.Combine(_root, "c3zip");
        var outTar = Path.Combine(_root, "c3tar");
        Directory.CreateDirectory(outZip);
        Directory.CreateDirectory(outTar);
        _svc.Extract(zip, outZip);
        _svc.Extract(tar, outTar);

        // zip 保顶层名, tar 解包即内容
        AssertFileEqual(Path.Combine(src, "真身.bin"), Path.Combine(outZip, "c3src", "真身.bin"));
        AssertFileEqual(Path.Combine(src, "真身.bin"), Path.Combine(outTar, "真身.bin"));
    }

    // ---- C4 组合: 解出之物再压缩, 防自压缩 (目标不在待压清单内) ----

    [Fact]
    public void C4_Combined_ExtractedOutputRecompressSafely()
    {
        var src = Path.Combine(_root, "c4src");
        Directory.CreateDirectory(src);
        File.WriteAllBytes(Path.Combine(src, "种.bin"), new byte[] { 7 });
        var zip = Path.Combine(_root, "c4.zip");
        _svc.CreateZip(new[] { src }, zip);

        var dest = Path.Combine(_root, "c4out");
        Directory.CreateDirectory(dest);
        _svc.Extract(zip, dest);
        // 把压缩包本身也丢进输出目录, 再压整个目录: 包中包不应出现死循环或自我包含
        File.Copy(zip, Path.Combine(dest, "c4.zip"), overwrite: true);
        var zip2 = Path.Combine(_root, "c4二代.zip");
        _svc.CreateZip(new[] { dest }, zip2);
        Assert.True(_svc.Test(zip2).Entries >= 2);
    }

    private static void AssertFileEqual(string a, string b)
    {
        Assert.True(File.Exists(b), $"缺文件 {b}");
        Assert.True(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(a))
            .SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(b))),
            $"内容漂移: {b}");
    }

    // ---- T1 托盘之道: 消息托管宿主窗句柄, 禁止自建消息窗口 (TaoDBM 真机教训同型排查) ----

    [Fact]
    public void T1_Tray_MessagesHostedOnOwnerWindow()
    {
        // TaoDBM 真机教训 (2026-11-03): 自建消息窗口在真机上可能创建失败, 托盘整条链路失效;
        // 托盘消息必须子类化托管到宿主窗句柄, explorer 重启后按 TaskbarCreated 自动补挂。
        var tray = ReadUi("TrayIcon.cs");
        Assert.DoesNotContain("CreateWindowExW", tray);                          // 不再自建消息窗口
        Assert.Contains("SetWindowLongPtrW(_ownerHwnd", tray);                   // 子类化宿主窗
        Assert.Contains("RegisterWindowMessageW(\"TaskbarCreated\")", tray);     // 重启补挂
        Assert.Contains("SetWindowLongPtrW(_ownerHwnd, GWLP_WNDPROC, _prevProc)", tray); // 释放还原原过程
        // 两个后台窗都以宿主窗句柄创建托盘, 不再走旧签名
        var compress = ReadUi("CompressWindow.xaml.cs");
        Assert.Contains("new TrayIcon(WinRT.Interop.WindowNative.GetWindowHandle(this)", compress);
        var extract = ReadUi("ExtractWindow.xaml.cs");
        Assert.Contains("new TrayIcon(WinRT.Interop.WindowNative.GetWindowHandle(this)", extract);
        Assert.DoesNotContain("new TrayIcon(\"", compress);
        Assert.DoesNotContain("new TrayIcon(\"", extract);
        // 版本推进: 1.0.4 (托盘托管 + 天青主题随包)
        var csproj = File.ReadAllText(Path.Combine(_ui, "TaoZip.PC.App.csproj"));
        Assert.Contains("<Version>1.0.4</Version>", csproj);
    }
}

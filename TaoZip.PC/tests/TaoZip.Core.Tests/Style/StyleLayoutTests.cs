using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace TaoZipStyle;

/// <summary>
/// 样式布局测试层（样式之道, 道生万物）:
/// 各安其位 —— 安装页控件零重叠 (悬停闪烁的病根);
/// 象必有名 —— 每条右键登记必带图标值;
/// 负阴抱阳 —— 界面必有空状态安顿"无"。
/// </summary>
public class StyleLayoutTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 10 && dir != null; i++, dir = dir.Parent!)
        {
            if (File.Exists(Path.Combine(dir.FullName, "tools", "msi", "package.ps1")))
                return dir.FullName;
        }
        throw new InvalidOperationException("未定位到源码树根 (tools/msi/package.ps1)");
    }

    private static string InstallerScript() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "tools", "msi", "package.ps1"));

    // ---- 各安其位: 安装页任意两控件矩形不得重叠 ----

    [Fact]
    public void Style_Placement_InstallerControlsNeverOverlap()
    {
        var src = InstallerScript();
        var dialogs = Regex.Split(src, Regex.Escape("<Dialog"))
            .Where(d => d.Contains("<Control "))
            .ToList();
        Assert.True(dialogs.Count >= 5, $"自绘页不足 5 张, 只识别到 {dialogs.Count}");

        var problems = new List<string>();
        foreach (var dlg in dialogs)
        {
            var name = Regex.Match(dlg, @"Id=""([^""]+)""").Groups[1].Value;
            var rects = new List<(string Id, int X, int Y, int W, int H)>();
            foreach (Match m in Regex.Matches(dlg,
                         "<Control\\s+Id=\"([^\"]+)\"[^>]*?X=\"(\\d+)\"[^>]*?Y=\"(\\d+)\"[^>]*?Width=\"(\\d+)\"[^>]*?Height=\"(\\d+)\""))
                rects.Add((m.Groups[1].Value, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value),
                           int.Parse(m.Groups[4].Value), int.Parse(m.Groups[5].Value)));
            for (int i = 0; i < rects.Count; i++)
                for (int j = i + 1; j < rects.Count; j++)
                {
                    var a = rects[i]; var b = rects[j];
                    bool overlap = a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H;
                    if (overlap) problems.Add($"{name}: {a.Id} 与 {b.Id} 重叠");
                }
        }
        Assert.True(problems.Count == 0, "控件重叠: " + string.Join("; ", problems));
    }

    // ---- 象必有名: 每条右键菜单登记必带 Icon 值 ----

    [Fact]
    public void Style_EveryVerbRegistration_HasIcon()
    {
        var src = InstallerScript();
        var verbBlocks = Regex.Matches(src, "<RegistryKey Root=\"HKLM\"[^>]*Key=\"[^\"]*(TaoZip\\.Extract|TaoZip\\.Compress)\"[^>]*>(.*?)</RegistryKey>",
            RegexOptions.Singleline);
        Assert.True(verbBlocks.Count >= 10, $"右键登记不足 10 条, 只识别到 {verbBlocks.Count}");
        var missing = new List<string>();
        foreach (Match m in verbBlocks)
        {
            var key = Regex.Match(m.Value, "Key=\"([^\"]+)\"").Groups[1].Value;
            if (!m.Value.Contains("Name=\"Icon\"")) missing.Add(key);
        }
        Assert.True(missing.Count == 0, "右键登记缺图标值: " + string.Join("; ", missing));
        Assert.True(Regex.Matches(src, "Name=\"Icon\"").Count >= 10, "图标值总数不足 10 (8 解压 + 2 压缩)");
    }

    // ---- 负阴抱阳: 主界面必有空状态, 且随列表虚实联动 ----

    [Fact]
    public void Style_YinYang_MainWindowHasEmptyState()
    {
        var ui = Path.Combine(RepoRoot(), "ui", "TaoZip.PC.App");
        var xaml = File.ReadAllText(Path.Combine(ui, "MainWindow.xaml"));
        var code = File.ReadAllText(Path.Combine(ui, "MainWindow.xaml.cs"));
        Assert.Contains("EmptyHint", xaml);
        Assert.Contains("EmptyHint.Visibility", code);
        Assert.Contains("UpdateEmptyHint", code);
    }

    // ---- 知常曰明: 打包脚本版本号随包更新 (常可循) ----

    [Fact]
    public void Style_Knowledge_PackScriptVersionPresent()
    {
        var src = InstallerScript();
        var m = Regex.Match(src, @"\$ScriptVersion = ""(v\d+-\d+)""");
        Assert.True(m.Success, "打包脚本缺版本标记");
        Assert.Matches(@"^v\d+-\d{4}$", m.Groups[1].Value);
    }

    // ---- 快切并档 (2026-11-02 TaoDown 同型教训): 连击只落定最后一次, 中间态不执行 ----

    private static string ThemeServiceSrc() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "ui", "TaoZip.PC.App", "ThemeService.cs"));

    [Fact]
    public void Style_ThemeCoalesce_TimerHeldInField_AntiGc()
    {
        var src = ThemeServiceSrc();
        // v84 真机构建定案: CreateTimer 返回 Microsoft.UI.Dispatching.DispatcherQueueTimer,
        // 字段类型必须与之同源 (原 Windows.System 声明编译不过)
        Assert.Matches(@"private static Microsoft\.UI\.Dispatching\.DispatcherQueueTimer\?\s+_coalesceTimer;", src);
        Assert.Matches(@"_coalesceTimer = queue\.CreateTimer\(\);", src);
        // 字段持有, 不得为局部变量 (刷新时钟被回收的同型教训)
        Assert.DoesNotContain("var _coalesceTimer", src);
    }

    [Fact]
    public void Style_ThemeCoalesce_LastOnly_Semantics()
    {
        var src = ThemeServiceSrc();
        // 非重复定时器: 一轮只落定一次
        Assert.Contains("_coalesceTimer.IsRepeating = false;", src);
        // 连击覆盖: 每次 Apply 重写待落定目标, 最后一次赢
        Assert.Contains("_pendingWindow = window;", src);
        Assert.Contains("_pendingThemeId = themeId;", src);
        // 重启定时器: 窗口期内不断顺延, 到点才落定
        Assert.Contains("_coalesceTimer.Stop();", src);
        Assert.Contains("_coalesceTimer.Start();", src);
        // Tick 落定时先取走目标再清空, 落定走 ApplyNow
        Assert.Contains("ApplyNow(w, id);", src);
    }

    [Fact]
    public void Style_ThemeCoalesce_ApplyNow_SingleSettlement()
    {
        var src = ThemeServiceSrc();
        // 落定入口唯一: Apply 只做并档, 舞步只在 ApplyNow
        Assert.Matches(@"private static void ApplyNow\(Window\? window, int themeId\)", src);
        // Apply 本体不再直接执行字典摘挂/基调赋值 (防止绕开并档)
        var applyBody = src[(src.IndexOf("public static void Apply", StringComparison.Ordinal))..
                           (src.IndexOf("private static void ApplyNow", StringComparison.Ordinal))];
        Assert.DoesNotContain("RemoveTheme(", applyBody);
        Assert.DoesNotContain("RequestedTheme =", applyBody);
    }
}

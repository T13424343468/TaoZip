using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace TaoZipOps;

/// <summary>
/// 视觉之道（维度 V · 真人视觉模拟）:
/// 用户真机反馈"部分主题配色导致按钮等元素看不见"。按钮隐身的物理本质是
/// 前景/描边与底色亮度对比不足——不靠人眼逐台机器看, 以 WCAG 相对亮度对比律
/// 对三象色板逐对体检: 文字 vs 底、按钮字 vs 按钮面、描边 vs 按钮面、
/// 描边 vs 底、点缀 vs 底。凡可断言皆成测试, 隐身对永久出局。
/// 阈值: 正文 4.5 / 次要 3.5 / 点缀(UI) 3.0 / 描边 2.0 / 按钮字 4.0 (禁用态豁免)。
/// </summary>
public class VisionTests
{
    private readonly ITestOutputHelper _out;
    public VisionTests(ITestOutputHelper output) => _out = output;

    private static string ThemesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var d = dir; d != null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "ui", "TaoZip.PC.App", "Themes");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new InvalidOperationException("未定位到 Themes 目录");
    }

    private static (double R, double G, double B) HexToRgb(string hex)
    {
        if (hex.StartsWith('#')) hex = hex[1..];   // #RRGGBB → RRGGBB
        if (hex.Length == 8) hex = hex[2..];       // AARRGGBB → RRGGBB
        return ((double)Convert.ToByte(hex[..2], 16) / 255,
                (double)Convert.ToByte(hex.Substring(2, 2), 16) / 255,
                (double)Convert.ToByte(hex.Substring(4, 2), 16) / 255);
    }

    private static double Channel(double c) =>
        c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    private static double Luminance(string hex)
    {
        var (r, g, b) = HexToRgb(hex);
        return 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
    }

    private static double Contrast(string a, string b)
    {
        var la = Luminance(a); var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static string ColorOf(string dict, string key)
    {
        var m = Regex.Match(dict, $"x:Key=\"{key}\" Color=\"(#[0-9A-Fa-f]{{6,8}})\"");
        if (!m.Success)
        {
            // 釉面渐变键 (陶瓷质感): 取 0.55 主体色帧参与对比体检, 真人眼看到的正是这一帧
            var g = Regex.Match(dict, $"x:Key=\"{key}\".*?GradientStop[^>]*Offset=\"0.55\"[^>]*Color=\"(#[0-9A-Fa-f]{{6,8}})\"", RegexOptions.Singleline);
            if (!g.Success)
            {
                g = Regex.Match(dict, $"x:Key=\"{key}\".*?GradientStop[^>]*Color=\"(#[0-9A-Fa-f]{{6,8}})\"[^>]*Offset=\"0.55\"", RegexOptions.Singleline);
            }
            Assert.True(g.Success, $"缺色 {key}");
            return g.Groups[1].Value;
        }
        return m.Groups[1].Value;
    }

    private static void Check(ITestOutputHelper output, string theme, string dict,
        (string Fg, string Bg, double Min, string What)[] pairs)
    {
        foreach (var (fgKey, bgKey, min, what) in pairs)
        {
            var ratio = Contrast(ColorOf(dict, fgKey), ColorOf(dict, bgKey));
            output.WriteLine($"{theme} · {what}: {ratio:F2} (线 {min})");
            Assert.True(ratio >= min, $"{theme} · {what}: 对比 {ratio:F2} 低于 {min}, 真人眼将看不见 ({fgKey} vs {bgKey})");
        }
    }

    [Fact]
    public void V1_Vision_AllThemePairsHumanlyVisible()
    {
        var themesDir = ThemesDir();
        var ink = File.ReadAllText(Path.Combine(themesDir, "InkTheme.xaml"));
        var sky = File.ReadAllText(Path.Combine(themesDir, "SkyTheme.xaml"));
        var night = File.ReadAllText(Path.Combine(themesDir, "TaoDark.xaml"));

        // 三象同构对: 正文/次要/按钮字/描边双界/点缀 (禁用态豁免不查)
        (string, string, double, string)[] inkPairs =
        {
            ("TextFillColorPrimaryBrush", "ApplicationPageBackgroundThemeBrush", 4.5, "正文 vs 底"),
            ("TextFillColorSecondaryBrush", "ApplicationPageBackgroundThemeBrush", 3.5, "次要字 vs 底"),
            ("TextFillColorPrimaryBrush", "ControlFillColorDefaultBrush", 4.0, "按钮字 vs 按钮面"),
            ("ControlStrokeColorDefaultBrush", "ControlFillColorDefaultBrush", 2.0, "描边 vs 按钮面"),
            ("ControlStrokeColorDefaultBrush", "ApplicationPageBackgroundThemeBrush", 2.0, "描边 vs 底"),
            ("AccentFillColorDefaultBrush", "ApplicationPageBackgroundThemeBrush", 3.0, "点缀 vs 底"),
        };
        Check(_out, "水墨", ink, inkPairs);
        Check(_out, "天青", sky, inkPairs);
        Check(_out, "墨夜", night, inkPairs);
    }

    [Fact]
    public void V2_Vision_StrokeKeysPresentInAllThree()
    {
        // 天青曾整段缺描边键 ——白按钮落近白底即隐身, 三象必须齐备
        foreach (var f in new[] { "InkTheme.xaml", "SkyTheme.xaml", "TaoDark.xaml" })
        {
            var dict = File.ReadAllText(Path.Combine(ThemesDir(), f));
            Assert.Contains("x:Key=\"ControlStrokeColorDefaultBrush\"", dict);
            Assert.Contains("x:Key=\"ControlStrokeColorSecondaryBrush\"", dict);
        }
    }

    // ---- V3 三象互异 + 亮度阶梯: 切换必须一眼可辨, 浅/中/黑三段不许串档 ----

    [Fact]
    public void V3_Vision_ThreeThemesLadderAndDistinct()
    {
        var ink = ColorOf(File.ReadAllText(Path.Combine(ThemesDir(), "InkTheme.xaml")), "ApplicationPageBackgroundThemeBrush");
        var sky = ColorOf(File.ReadAllText(Path.Combine(ThemesDir(), "SkyTheme.xaml")), "ApplicationPageBackgroundThemeBrush");
        var night = ColorOf(File.ReadAllText(Path.Combine(ThemesDir(), "TaoDark.xaml")), "ApplicationPageBackgroundThemeBrush");

        // 亮度阶梯: 水墨浅为主 / 天青浅偏青 / 墨夜黑为主, 三档各守其带, 单调递减
        var li = Luminance(ink); var ls = Luminance(sky); var ln = Luminance(night);
        Assert.True(li > 0.8, $"水墨应浅色为主 (L={li:F2})");
        Assert.True(ls > 0.85, $"天青应浅调 (L={ls:F2})");
        Assert.True(ln < 0.03, $"墨夜应以黑为主 (L={ln:F3})");
        Assert.True(li > ls && ls > ln, "亮度阶梯必须单调: 水墨 > 天青 > 墨夜");

        // 气质签名: 水墨暖宣 (R>G>B) / 天青青瓷 (G>B>R) / 墨夜暖暗 (R>G>B)
        var inkBG = ink; var (ir, ig, ib) = HexToRgb(ink);
        var (sr, sg, sb) = HexToRgb(sky);
        Assert.True(ir > ig && ig > ib, "水墨底应暖序 R>G>B");
        Assert.True(sg > sb && sb > sr, "天青底应青瓷序 G>B>R");

        // 点缀两两互异 (2026-11-06 天青定稿): 水墨=深墨青灰蓝 #0F4C9E 系, 天青=青瓷 #34655C, 墨夜=白亮黛青,
        // 三象点缀同键不同色, 换肤一眼可辨
        var accents = new[] {
            ColorOf(File.ReadAllText(Path.Combine(ThemesDir(), "InkTheme.xaml")), "AccentFillColorDefaultBrush"),
            ColorOf(File.ReadAllText(Path.Combine(ThemesDir(), "SkyTheme.xaml")), "AccentFillColorDefaultBrush"),
            ColorOf(File.ReadAllText(Path.Combine(ThemesDir(), "TaoDark.xaml")), "AccentFillColorDefaultBrush"),
        };
        Assert.True(accents[0] != accents[1] && accents[1] != accents[2] && accents[0] != accents[2], "三象点缀必须两两互异");
        Assert.True(Luminance(accents[1]) < Luminance(inkBG) && Luminance(accents[1]) > 0.05, "天青点缀应为深色青瓷 (深底上可见)");
        Assert.True(Contrast(accents[0], accents[2]) >= 1.5, "墨夜点缀与浅底系过近");
    }

    // ---- V4 标题栏可见性: 三颗小按钮随象而染, 墨夜近黑底不许再隐身 ----

    [Fact]
    public void V4_Vision_CaptionGlyphsHumanlyVisible()
    {
        foreach (var (name, f) in new[] { ("水墨", "InkTheme.xaml"), ("天青", "SkyTheme.xaml"), ("墨夜", "TaoDark.xaml") })
        {
            var dict = File.ReadAllText(Path.Combine(ThemesDir(), f));
            var ratio = Contrast(ColorOf(dict, "WindowCaptionForegroundBrush"),
                                 ColorOf(dict, "WindowCaptionBackgroundBrush"));
            _out.WriteLine($"{name} · 标题栏字 vs 标题栏底: {ratio:F2} (线 2)");
            Assert.True(ratio >= 2.0, $"{name} 标题栏字对比 {ratio:F2} 不足 2, 真人眼看不见右上角按钮");
        }
    }

    /// <summary>A2 强调色覆盖（2026-11-01 全系避坑，源自 TaoPad 真机"设置弹窗控件没跟主题"）：
    /// 三象字典必须自带完整 accent 键族，子窗（解压/压缩）与全部弹层的主按钮/开关
    /// 才与三象统一，不随系统强调色漂移。</summary>
    [Fact]
    public void A2_AccentOverride_KeysCompleteInAllThemes()
    {
        string[] dicts = { "InkTheme.xaml", "SkyTheme.xaml", "TaoDark.xaml" };
        string[] keys =
        {
            "SystemAccentColor", "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3",
            "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3",
            "SystemAccentColorLight1Brush", "SystemAccentColorLight2Brush", "SystemAccentColorLight3Brush",
            "SystemAccentColorDark1Brush", "SystemAccentColorDark2Brush", "SystemAccentColorDark3Brush",
            "ToggleSwitchFillOn", "SystemControlHighlightAccentBrush",
            "SystemControlForegroundAccentBrush", "SystemControlBackgroundAccentBrush",
        };
        foreach (var dict in dicts)
        {
            var src = File.ReadAllText(Path.Combine(ThemesDir(), dict));
            foreach (var k in keys)
                Assert.True(src.Contains($"x:Key=\"{k}\""), $"{dict} 缺 accent 键 {k}");
        }
    }

}

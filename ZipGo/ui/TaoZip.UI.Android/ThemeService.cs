using Android.Content;
using Android.Graphics;
using Android.Content.Res;

namespace TaoZip.UI.Android;

/// <summary>
/// 主题三象（水墨 / 天灰 / 墨夜）：安卓版主题唯一事实来源。
/// 色板扁平字典全键消费，禁界面硬编码色；层色底定律：页底 &lt; 卡面 &lt; 悬浮层。
/// 对比度阈值（视觉之道）：正文 4.5 / 次要 3.5 / 辅助 3.0 / 点缀与选中 3.0 / 描边 2.0。
/// </summary>
internal static class ThemeService
{
    public enum Xiang { Shuimo, Tianhui, Moye }

    public enum Key
    {
        // 底面（层色底定律：由浅入深）
        PageBg, CardBg, FloatBg, PressedBg,
        // 主色与按钮
        Primary, OnPrimary, Danger, OnDanger, Accent,
        // 文字（正文/次要/辅助）
        TextMain, TextSub, TextHint,
        // 描边
        Stroke, Divider,
        // 状态栏图标亮度：true=浅图标（深底象），false=深图标（浅底象）
        LightStatusIcons,
    }

    /// <summary>当前象（默认水墨，随应用启动记忆）。</summary>
    public static Xiang Current { get; private set; } = Xiang.Shuimo;

    static readonly Dictionary<Key, string> Shuimo = new()
    {
        [Key.PageBg] = "#F5F2EB", [Key.CardBg] = "#FFFFFF", [Key.FloatBg] = "#FFFFFF", [Key.PressedBg] = "#EDE8DC",
        [Key.Primary] = "#3D4A5C", [Key.OnPrimary] = "#FFFFFF",
        [Key.Danger] = "#B3403A", [Key.OnDanger] = "#FFFFFF", [Key.Accent] = "#A8402F",
        [Key.TextMain] = "#2A2A28", [Key.TextSub] = "#6B6B66", [Key.TextHint] = "#8A8A84",
        [Key.Stroke] = "#A8A296", [Key.Divider] = "#B8B2A4",
    };

    static readonly Dictionary<Key, string> Tianhui = new()
    {
        [Key.PageBg] = "#F0F2F4", [Key.CardBg] = "#FFFFFF", [Key.FloatBg] = "#FFFFFF", [Key.PressedBg] = "#E3E7EA",
        [Key.Primary] = "#46647E", [Key.OnPrimary] = "#FFFFFF",
        [Key.Danger] = "#B3403A", [Key.OnDanger] = "#FFFFFF", [Key.Accent] = "#3E6990",
        [Key.TextMain] = "#26282A", [Key.TextSub] = "#64686C", [Key.TextHint] = "#83878B",
        [Key.Stroke] = "#A6AAAE", [Key.Divider] = "#ACB0B4",
    };

    static readonly Dictionary<Key, string> Moye = new()
    {
        [Key.PageBg] = "#17191C", [Key.CardBg] = "#232629", [Key.FloatBg] = "#2C3034", [Key.PressedBg] = "#34383D",
        [Key.Primary] = "#577C9A", [Key.OnPrimary] = "#000000",
        [Key.Danger] = "#C94E46", [Key.OnDanger] = "#000000", [Key.Accent] = "#D08770",
        [Key.TextMain] = "#E8E4DC", [Key.TextSub] = "#A8ACB0", [Key.TextHint] = "#7E848A",
        [Key.Stroke] = "#565B62", [Key.Divider] = "#52565C",
    };

    static Dictionary<Key, string> PaletteOf(Xiang x) => x switch
    {
        Xiang.Tianhui => Tianhui,
        Xiang.Moye => Moye,
        _ => Shuimo,
    };

    const string PrefName = "taozip_theme";
    const string PrefKey = "xiang";

    /// <summary>启动时从偏好恢复上次的象。</summary>
    public static void Restore(Context ctx)
    {
        try
        {
            var saved = ctx.GetSharedPreferences(PrefName, FileCreationMode.Private)
                .GetString(PrefKey, null);
            // 首次启动 saved 为 null：TryParse(null) 返回 false，落默认水墨象，属正常路径
            if (saved != null && Enum.TryParse(saved, out Xiang x)) Current = x;
        }
        catch { Current = Xiang.Shuimo; }
    }

    /// <summary>切换主题并持久化；返回是否发生了变化。</summary>
    public static bool SetXiang(Context ctx, Xiang x)
    {
        if (x == Current) return false;
        Current = x;
        try
        {
            var editor = ctx.GetSharedPreferences(PrefName, FileCreationMode.Private).Edit();
            editor.PutString(PrefKey, x.ToString()).Apply();
        }
        catch { }
        return true;
    }

    public static string Name => Current switch
    {
        Xiang.Tianhui => "天灰",
        Xiang.Moye => "墨夜",
        _ => "水墨",
    };

    /// <summary>下一象（循环切换用）。</summary>
    public static Xiang Next => Current switch
    {
        Xiang.Shuimo => Xiang.Tianhui,
        Xiang.Tianhui => Xiang.Moye,
        _ => Xiang.Shuimo,
    };

    /// <summary>取色：全界面唯一入口，禁止绕开色板取色。</summary>
    public static Color C(Key k)
    {
        if (k == Key.LightStatusIcons)
            return LightStatusIcons ? Color.White : Color.Black; // 状态栏图标亮度随象
        return Color.ParseColor(PaletteOf(Current)[k]);
    }

    /// <summary>状态栏是否用浅色图标（深底象为 true）。</summary>
    public static bool LightStatusIcons => Current == Xiang.Moye;

    /// <summary>相对亮度（WCAG）。</summary>
    internal static double Luminance(string hex)
    {
        var c = Color.ParseColor(hex);
        return 0.2126 * Chan(c.R) + 0.7152 * Chan(c.G) + 0.0722 * Chan(c.B);
        static double Chan(int v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
    }

    /// <summary>对比度（WCAG）。</summary>
    internal static double Contrast(string a, string b)
    {
        var la = Luminance(a); var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>测试取整幅色板（hex），生产代码不使用。</summary>
    internal static IReadOnlyDictionary<string, IReadOnlyDictionary<Key, string>> AllPalettes => new Dictionary<string, IReadOnlyDictionary<Key, string>>
    {
        ["水墨"] = Shuimo, ["天灰"] = Tianhui, ["墨夜"] = Moye,
    };
}

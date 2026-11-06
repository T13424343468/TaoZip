using Android.App;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace TaoZip.UI.Android;

/// <summary>
/// 条目功能卡：贴着唤起元素弹出的悬浮卡片（长按与右侧按钮共用同一入口）。
/// 对位规则：唤起元素靠左卡片左对齐、靠右右对齐、居中水平居中；
/// 上下翻位：下方放得下就贴在唤起行下方，放不下则翻到唤起行上方；全程贴屏边收口不越界。
/// ShowBelow：固定在唤起元素正下方、左缘对齐唤起元素（顶栏下拉菜单用）。
/// ShowBubble：说明气泡卡（2.12），贴图标下方、右缘对齐图标右缘，3 秒自动收。
/// 侧栏卡不用投影（弹窗表面按卡片尺寸裁切，阴影贴边会留黑色残角），改细描边分层。
/// </summary>
internal static class AnchorMenu
{
    static PopupWindow? _bubble;
    public sealed class Item
    {
        public required string Text { get; init; }
        public Action? OnClick { get; init; }
        public bool Danger { get; init; }
    }

    /// <summary>说明气泡卡（2.12）：功能卡同款样式，贴在图标下方、右缘对齐图标右缘，
    /// 点别处或再点图标即收，3 秒自动关闭。</summary>
    public static void ShowBubble(Activity a, View anchor, string text)
    {
        CloseBubble();
        int Dp(float v) => (int)(v * a.Resources!.DisplayMetrics!.Density);

        var card = new LinearLayout(a) { Orientation = Orientation.Vertical };
        var bg = new GradientDrawable();
        bg.SetCornerRadius(Dp(24));
        bg.SetColor(ThemeService.C(ThemeService.Key.FloatBg));
        bg.SetStroke(Dp(1), ThemeService.C(ThemeService.Key.Stroke));
        card.Background = bg;

        var tv = new TextView(a) { Text = text, TextSize = 14 };
        tv.SetTextColor(ThemeService.C(ThemeService.Key.TextMain));
        tv.SetPadding(Dp(18), Dp(14), Dp(18), Dp(14));
        card.AddView(tv, new ViewGroup.LayoutParams(-2, -2));

        int screenW = a.Resources!.DisplayMetrics!.WidthPixels;
        card.Measure(
            View.MeasureSpec.MakeMeasureSpec((int)(screenW * 0.72f), MeasureSpecMode.AtMost),
            View.MeasureSpec.MakeMeasureSpec(0, MeasureSpecMode.Unspecified));
        int cardW = Math.Max(card.MeasuredWidth, Dp(168));

        _bubble = new PopupWindow(card, cardW, ViewGroup.LayoutParams.WrapContent, focusable: true)
        {
            OutsideTouchable = true,
        };
        _bubble.SetBackgroundDrawable(new ColorDrawable(global::Android.Graphics.Color.Transparent));

        var loc = new int[2];
        anchor.GetLocationOnScreen(loc);
        int margin = Dp(12), gap = Dp(4);
        int left = Math.Max(margin, Math.Min(loc[0] + anchor.Width - cardW, screenW - cardW - margin));
        int y = Math.Min(loc[1] + anchor.Height + gap, a.Resources!.DisplayMetrics!.HeightPixels - card.MeasuredHeight - margin);
        y = Math.Max(margin, y);
        new Handler(Looper.MainLooper!).PostDelayed(CloseBubble, 3000);
        _bubble.ShowAtLocation(a.Window!.DecorView, GravityFlags.NoGravity, left, y);
    }

    public static void CloseBubble()
    {
        _bubble?.Dismiss();
        _bubble = null;
    }

    public static void Show(Activity a, View anchor, Item[] items) => ShowInternal(a, anchor, items, belowLeft: false);

    public static void ShowBelow(Activity a, View anchor, Item[] items) => ShowInternal(a, anchor, items, belowLeft: true);

    static void ShowInternal(Activity a, View anchor, Item[] items, bool belowLeft)
    {
        int Dp(float v) => (int)(v * a.Resources!.DisplayMetrics!.Density);

        var card = new LinearLayout(a) { Orientation = Orientation.Vertical };
        var bg = new GradientDrawable();
        bg.SetCornerRadius(Dp(24));
        bg.SetColor(ThemeService.C(ThemeService.Key.FloatBg));
        bg.SetStroke(Dp(1), ThemeService.C(ThemeService.Key.Stroke));
        card.Background = bg;

        PopupWindow? pw = null;
        foreach (var it in items)
        {
            var row = new TextView(a) { Text = it.Text, TextSize = 15 };
            row.SetPadding(Dp(20), Dp(14), Dp(20), Dp(14));
            row.SetTextColor(ThemeService.C(it.Danger ? ThemeService.Key.Danger : ThemeService.Key.TextMain));
            var click = it.OnClick;
            row.Click += (s, e) => { pw?.Dismiss(); click?.Invoke(); };
            card.AddView(row, new ViewGroup.LayoutParams(-2, -2));
        }
        card.Measure(
            View.MeasureSpec.MakeMeasureSpec(0, MeasureSpecMode.Unspecified),
            View.MeasureSpec.MakeMeasureSpec(0, MeasureSpecMode.Unspecified));
        int cardW = Math.Max(card.MeasuredWidth, Dp(120));
        int cardH = card.MeasuredHeight;

        pw = new PopupWindow(card, cardW, ViewGroup.LayoutParams.WrapContent, focusable: true)
        {
            OutsideTouchable = true,
        };
        pw.SetBackgroundDrawable(new ColorDrawable(global::Android.Graphics.Color.Transparent));

        // 对位：唤起元素屏幕中线落在三分位哪一段，决定卡片左对齐 / 居中 / 右对齐
        var loc = new int[2];
        anchor.GetLocationOnScreen(loc);
        int screenW = a.Resources!.DisplayMetrics!.WidthPixels;
        int screenH = a.Resources!.DisplayMetrics!.HeightPixels;
        int margin = Dp(12);
        int gap = Dp(4);
        int left, y;
        if (belowLeft)
        {
            // 下拉模式：唤起元素正下方，左缘对齐唤起元素左缘，贴屏边收口
            left = Math.Max(margin, Math.Min(loc[0], screenW - cardW - margin));
            y = Math.Max(margin, Math.Min(loc[1] + anchor.Height + gap, screenH - cardH - margin));
        }
        else
        {
            int cx = loc[0] + anchor.Width / 2;
            left = cx < screenW / 3 ? loc[0]
                : cx > screenW * 2 / 3 ? loc[0] + anchor.Width - cardW
                : cx - cardW / 2;
            left = Math.Max(margin, Math.Min(left, screenW - cardW - margin));

            // 上下翻位：先量下方放不放得下，放不下就翻到唤起行上方；两头都紧时挑更宽裕的一侧
            int belowY = loc[1] + anchor.Height + gap;
            int aboveY = loc[1] - gap - cardH;
            bool belowFits = belowY + cardH <= screenH - margin;
            bool aboveFits = aboveY >= margin;
            y = belowFits || !aboveFits ? belowY : aboveY;
            y = Math.Max(margin, Math.Min(y, screenH - cardH - margin));
        }
        pw.ShowAtLocation(a.Window!.DecorView, GravityFlags.NoGravity, left, y);
    }
}

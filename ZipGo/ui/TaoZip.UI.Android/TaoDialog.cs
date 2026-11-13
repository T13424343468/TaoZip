using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Text;
using Android.Views;
using Android.Widget;

namespace TaoZip.UI.Android;

/// <summary>
/// 统一圆角弹窗：水墨卡片风格，替代系统 AlertDialog 默认样式。
/// 底色取悬浮层色，标题正文按文字层级取色，按钮主次分明。
/// </summary>
internal static class TaoDialog
{
    static GradientDrawable CardBg(Context ctx)
    {
        var bg = new GradientDrawable();
        bg.SetCornerRadius(Dp(ctx, 18));
        bg.SetColor(ThemeService.C(ThemeService.Key.FloatBg));
        return bg;
    }

    internal static int Dp(Context ctx, float v) => (int)(v * ctx.Resources!.DisplayMetrics!.Density);

    static LinearLayout BuildShell(Context ctx, string? title, out TextView? titleView)
    {
        var box = new LinearLayout(ctx) { Orientation = Orientation.Vertical };
        box.Background = CardBg(ctx);
        box.SetPadding(Dp(ctx, 22), Dp(ctx, 20), Dp(ctx, 22), Dp(ctx, 12));
        titleView = null;
        if (!string.IsNullOrEmpty(title))
        {
            titleView = new TextView(ctx) { Text = title, TextSize = 17 };
            titleView!.SetTypeface(Typeface.DefaultBold!, TypefaceStyle.Normal);
            titleView.SetTextColor(ThemeService.C(ThemeService.Key.TextMain));
            box.AddView(titleView, new LinearLayout.LayoutParams(-2, -2) { BottomMargin = Dp(ctx, 10) });
        }
        return box;
    }

    static Dialog Make(Activity ctxOwner, View content)
    {
        var d = new Dialog(ctxOwner);
        d.RequestWindowFeature((int)WindowFeatures.NoTitle);
        // 窗口底透明，遮罩交给窗口 DimAmount：内容层不再自叠暗色，杜绝卡片四周黑边
        var dlgWin = d.Window!;
        dlgWin.SetBackgroundDrawable(new ColorDrawable(global::Android.Graphics.Color.Transparent));
        d.SetContentView(WrapSheet(content));
        var win = dlgWin.Attributes;
        win.Width = (int)(ctxOwner.Resources!.DisplayMetrics!.WidthPixels * 0.86);
        win.Height = ViewGroup.LayoutParams.WrapContent;
        win.DimAmount = 0.45f;
        dlgWin.Attributes = win;
        return d;
    }

    /// <summary>弹窗内容容器：卡片留出圆角呼吸空间，背景压暗统一由窗口负责。</summary>
    static FrameLayout WrapSheet(View content)
    {
        var holder = new FrameLayout(content.Context!);
        holder.SetPadding(Dp(content.Context!, 8), Dp(content.Context!, 8), Dp(content.Context!, 8), Dp(content.Context!, 8));
        holder.AddView(content);
        return holder;
    }

    static LinearLayout ButtonRow(Context ctx, Dialog d, string? cancelText, (string Text, bool Primary, Action OnClick) ok)
    {
        var row = new LinearLayout(ctx) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        if (cancelText != null)
        {
            row.AddView(DimButton(ctx, d, cancelText), new LinearLayout.LayoutParams(0, -2, 1f) { RightMargin = Dp(ctx, 10) });
            row.AddView(MainButton(ctx, d, ok.Text, ok.OnClick), new LinearLayout.LayoutParams(0, -2, 1f));
        }
        else row.AddView(MainButton(ctx, d, ok.Text, ok.OnClick), new LinearLayout.LayoutParams(-1, -2));
        return row;
    }

    static TextView MainButton(Context ctx, Dialog d, string text, Action onClick)
    {
        var b = new TextView(ctx) { Text = text, TextSize = 15, Gravity = GravityFlags.Center };
        b.SetTypeface(Typeface.DefaultBold!, TypefaceStyle.Normal);
        b.SetTextColor(ThemeService.C(ThemeService.Key.OnPrimary));
        var bg = new GradientDrawable();
        bg.SetCornerRadius(Dp(ctx, 12));
        bg.SetColor(ThemeService.C(ThemeService.Key.Primary));
        b.Background = bg;
        b.SetPadding(0, Dp(ctx, 11), 0, Dp(ctx, 11));
        b.Click += (s, e) => { d.Dismiss(); onClick(); };
        return b;
    }

    static TextView DimButton(Context ctx, Dialog d, string text)
    {
        var b = new TextView(ctx) { Text = text, TextSize = 15, Gravity = GravityFlags.Center };
        b.SetTextColor(ThemeService.C(ThemeService.Key.TextSub));
        var bg = new GradientDrawable();
        bg.SetCornerRadius(Dp(ctx, 12));
        bg.SetColor(ThemeService.C(ThemeService.Key.PressedBg));
        b.Background = bg;
        b.SetPadding(0, Dp(ctx, 11), 0, Dp(ctx, 11));
        b.Click += (s, e) => d.Dismiss();
        return b;
    }

    /// <summary>统一输入框：随象圆角底，墨夜象下浅字不落白底。</summary>
    public static EditText MakeInput(Activity ctx, string hint, bool password = false)
    {
        var input = new EditText(ctx)
        {
            Hint = hint,
            TextSize = 14,
            InputType = password
                ? InputTypes.ClassText | InputTypes.TextVariationPassword
                : InputTypes.ClassText | InputTypes.TextVariationNormal,
        };
        input.SetSingleLine(true);
        input.SetTextColor(ThemeService.C(ThemeService.Key.TextMain));
        input.SetHintTextColor(ThemeService.C(ThemeService.Key.TextHint));
        var bg = new GradientDrawable();
        bg.SetCornerRadius(Dp(ctx, 10));
        bg.SetColor(ThemeService.C(ThemeService.Key.CardBg));
        bg.SetStroke(Dp(ctx, 1), ThemeService.C(ThemeService.Key.Stroke));
        input.Background = bg;
        input.SetPadding(Dp(ctx, 12), Dp(ctx, 10), Dp(ctx, 12), Dp(ctx, 10));
        return input;
    }

    /// <summary>信息弹窗：标题 + 说明 + 单确认。</summary>
    public static void Info(Activity ctx, string? title, string msg, string okText = "知道了")
    {
        var box = BuildShell(ctx, title, out _);
        var tv = new TextView(ctx) { Text = msg, TextSize = 14 };
        tv.SetLineSpacing(Dp(ctx, 3), 1f);
        tv.SetTextColor(ThemeService.C(ThemeService.Key.TextSub));
        box.AddView(tv, new LinearLayout.LayoutParams(-1, -2) { BottomMargin = Dp(ctx, 14) });
        var d = Make(ctx, box);
        box.AddView(ButtonRow(ctx, d, null, (okText, true, () => { })));
        d.Show();
    }

    /// <summary>确认弹窗：标题 + 说明 + 主按钮 + 次按钮。</summary>
    public static void Confirm(Activity ctx, string? title, string msg,
        string okText, Action onOk, string cancelText = "取消", bool primaryIsDanger = false)
    {
        var box = BuildShell(ctx, title, out _);
        var tv = new TextView(ctx) { Text = msg, TextSize = 14 };
        tv.SetLineSpacing(Dp(ctx, 3), 1f);
        tv.SetTextColor(ThemeService.C(ThemeService.Key.TextSub));
        box.AddView(tv, new LinearLayout.LayoutParams(-1, -2) { BottomMargin = Dp(ctx, 14) });
        var d = Make(ctx, box);
        box.AddView(ButtonRow(ctx, d, cancelText, (okText, false, onOk)));
        // 主按钮可带危险色（删除确认）
        if (primaryIsDanger)
        {
            var last = box.GetChildAt(box.ChildCount - 1);
            var btn = last is LinearLayout r ? (TextView)r.GetChildAt(r.ChildCount - 1)! : null;
            if (btn != null)
            {
                var bg = new GradientDrawable();
                bg.SetCornerRadius(Dp(ctx, 12));
                bg.SetColor(ThemeService.C(ThemeService.Key.Danger));
                btn.Background = bg;
            }
        }
        d.Show();
    }

    /// <summary>自定义内容弹窗：标题 + 任意视图 + 主/次按钮。</summary>
    public static void Custom(Activity ctx, string? title, View content,
        string okText, Action<Dialog> onOk, string? cancelText = "取消")
    {
        var box = BuildShell(ctx, title, out _);
        box.AddView(content, new LinearLayout.LayoutParams(-1, -2) { BottomMargin = Dp(ctx, 14) });
        var d = Make(ctx, box);
        box.AddView(ButtonRow(ctx, d, cancelText, (okText, true, () => onOk(d))));
        d.Show();
    }

    /// <summary>单选列表弹窗：水墨卡片风格的菜单/选择。</summary>
    public static void Items(Activity ctx, string? title, string[] items, Action<int> onPick, string? cancelText = "取消")
    {
        var box = BuildShell(ctx, title, out _);
        var list = new ListView(ctx) { Divider = null, DividerHeight = 0 };
        list.Adapter = new ItemAdapter(ctx, items);
        list.ItemClick += (s, e) =>
        {
            ((Dialog)((View)s!).Tag!)?.Dismiss();
            onPick(e.Position);
        };
        box.AddView(list, new LinearLayout.LayoutParams(-1, -2) { BottomMargin = Dp(ctx, 6) });
        var d = Make(ctx, box);
        list.Tag = d;
        if (cancelText != null)
        {
            var cancel = DimButton(ctx, d, cancelText);
            box.AddView(cancel, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(ctx, 4) });
        }
        d.Show();
    }

    sealed class ItemAdapter : BaseAdapter<string>
    {
        readonly Context _ctx;
        readonly string[] _items;
        public ItemAdapter(Context ctx, string[] items) { _ctx = ctx; _items = items; }
        public override string this[int p] => _items[p];
        public override int Count => _items.Length;
        public override long GetItemId(int p) => p;
        public override View GetView(int p, View? cv, ViewGroup? parent)
        {
            var t = cv as TextView;
            if (t == null)
            {
                t = new TextView(_ctx) { TextSize = 15 };
                t.SetPadding(Dp(_ctx, 4), Dp(_ctx, 12), Dp(_ctx, 4), Dp(_ctx, 12));
            }
            t.Text = _items[p];
            t.SetTextColor(ThemeService.C(ThemeService.Key.TextMain));
            return t;
        }
    }
}

/// <summary>TextView 扩展：链式内边距（简写）。</summary>
internal static class ViewExt
{
    public static TextView SetPaddingInternal(this TextView t, int l, int tp, int r, int b)
    {
        t.SetPadding(l, tp, r, b);
        return t;
    }
}

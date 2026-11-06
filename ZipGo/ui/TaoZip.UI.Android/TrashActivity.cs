using Android.App;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Widget;
using AndroidX.Core.View;
using FS = System.IO;

namespace TaoZip.UI.Android;

/// <summary>
/// 回收站页：列出 TaoZip 回收站内容，可放回原处、可彻底删除、可一键清空；
/// 图片视频走系统相册回收站，此页只管自家回收站。
/// 2.7 真机反馈：与主界面同景——显式三象浅色 NoActionBar 主题 + 状态栏随象取色，
/// 根除系统默认深色标题与「大标题 + 页内标题」两层头部；顶栏统一为 返回 + 标题 + 清空。
/// </summary>
[Activity(Label = "回收站", Exported = false,
    Theme = "@android:style/Theme.Material.Light.NoActionBar",
    ScreenOrientation = ScreenOrientation.Portrait,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
public class TrashActivity : Activity
{
    ListView _list = null!;
    LinearLayout _emptyBox = null!;
    TextView _emptyMain = null!;
    List<TrashHelper.TrashItem> _items = new();

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ThemeService.Restore(this);

        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetBackgroundColor(ThemeService.C(ThemeService.Key.PageBg));

        var header = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        header.SetGravity(GravityFlags.CenterVertical);
        header.SetPadding(Dp(10), Dp(14), Dp(12), Dp(10));
        var back = new IconView(this) { Clickable = true };
        back.Bind(IconKind.Back, ThemeService.Key.TextMain);
        back.Click += (s, e) => Finish();
        header.AddView(back, Dp(36), Dp(36));
        var title = new TextView(this) { Text = "回收站", TextSize = 17 };
        title.SetTypeface(Typeface.DefaultBold!, TypefaceStyle.Normal);
        title.SetTextColor(ThemeService.C(ThemeService.Key.TextMain));
        header.AddView(title, new LinearLayout.LayoutParams(0, -2, 1f) { LeftMargin = Dp(8) });
        // 2.12 真机反馈：清空改危险色垃圾桶图标；说明文字收进 ? 气泡卡，图标放最右侧
        var clear = new IconView(this) { Clickable = true };
        clear.Bind(IconKind.Trash, ThemeService.Key.Danger);
        clear.Click += (s, e) => ConfirmClear();
        header.AddView(clear, new LinearLayout.LayoutParams(Dp(30), Dp(30)) { RightMargin = Dp(10) });
        var help = new IconView(this) { Clickable = true };
        help.Bind(IconKind.Info, ThemeService.Key.TextMain);
        help.Click += (s, e) =>
            AnchorMenu.ShowBubble(this, help,
                "图片、视频删除后进手机相册回收站；其余内容在这里，可放回原处或彻底删除。");
        header.AddView(help, Dp(30), Dp(30));
        root.AddView(header);

        // 2.12 真机反馈：列表与文件页同款——整块 24dp 大圆角卡包住列表，高度占满页面
        _list = new ListView(this)
        {
            Divider = null,
            DividerHeight = 0,
            Selector = new ColorDrawable(global::Android.Graphics.Color.Transparent),
        };
        _list.Adapter = new TrashAdapter(this);
        _list.SetClipToPadding(false);
        _list.SetPadding(0, Dp(2), 0, Dp(10));
        var host = new FrameLayout(this);
        host.AddView(_list, new FrameLayout.LayoutParams(-1, -1));
        _emptyBox = new LinearLayout(this) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone };
        _emptyBox.SetGravity(GravityFlags.Center);
        var ei = new IconView(this);
        ei.Bind(IconKind.Archive, ThemeService.Key.Stroke);
        _emptyBox.AddView(ei, Dp(56), Dp(56));
        _emptyMain = new TextView(this) { Text = "回收站是空的", TextSize = 15 };
        _emptyMain.SetTextColor(ThemeService.C(ThemeService.Key.TextSub));
        _emptyBox.AddView(_emptyMain, new LinearLayout.LayoutParams(-2, -2) { TopMargin = Dp(12) });
        host.AddView(_emptyBox, new FrameLayout.LayoutParams(-1, -1));
        var listCard = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var cardBg = new GradientDrawable();
        cardBg.SetCornerRadius(Dp(24));
        cardBg.SetColor(ThemeService.C(ThemeService.Key.CardBg));
        listCard.Background = cardBg;
        listCard.ClipToOutline = true;
        listCard.AddView(host, new LinearLayout.LayoutParams(-1, 0, 1f));
        root.AddView(listCard, new LinearLayout.LayoutParams(-1, 0, 1f)
        { LeftMargin = Dp(12), RightMargin = Dp(12), BottomMargin = Dp(12) });

        SetContentView(root);
        ApplyEdge(root);
        Reload();
    }

    int Dp(int v) => (int)(v * Resources!.DisplayMetrics!.Density);

    /// <summary>子页与主界面同景（2.7）：状态栏/导航栏随象取色，杜绝顶部一片黑。</summary>
    void ApplyEdge(View root)
    {
        Window!.SetStatusBarColor(ThemeService.C(ThemeService.Key.PageBg));
        Window.SetNavigationBarColor(ThemeService.C(ThemeService.Key.PageBg));
        if (Build.VERSION.SdkInt >= BuildVersionCodes.P)
            Window.Attributes.LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.ShortEdges;
        WindowCompat.SetDecorFitsSystemWindows(Window!, false);
        var ctrl = WindowCompat.GetInsetsController(Window!, Window!.DecorView);
        if (ctrl != null) ctrl.AppearanceLightStatusBars = !ThemeService.LightStatusIcons;
        ViewCompat.SetOnApplyWindowInsetsListener(root, new MainActivity.InsetsListener());
    }

    void Reload()
    {
        _items = TrashHelper.ListTrash();
        _emptyBox.Visibility = _items.Count == 0 ? ViewStates.Visible : ViewStates.Gone;
        _list.Visibility = _items.Count == 0 ? ViewStates.Gone : ViewStates.Visible;
        ((TrashAdapter)_list.Adapter!).NotifyDataSetChanged();
    }

    /// <summary>2.21 回收站预览：点条目直接用系统方式打开——图片交系统图库（2.20 定音），视频优先道播放，其余交系统应用；文件夹仍是功能卡。</summary>
    void OpenPreview(TrashHelper.TrashItem it, View anchor)
    {
        if (it.IsDir) { ItemMenu(it, anchor); return; }
        if (!FS.File.Exists(it.EntryPath))
        {
            Toast.MakeText(this, new Java.Lang.String("文件不在了，先放回原处再预览"), ToastLength.Short)!.Show();
            Reload();
            return;
        }
        MainActivity.OpenExternally(this, it.EntryPath);
    }

    void ItemMenu(TrashHelper.TrashItem it, View anchor)
    {
        AnchorMenu.Show(this, anchor, new[]
        {
            new AnchorMenu.Item
            {
                Text = "放回原处",
                OnClick = () =>
                {
                    try
                    {
                        TrashHelper.Restore(it, this);
                        Toast.MakeText(this, new Java.Lang.String("已放回原处"), ToastLength.Short)!.Show();
                        Reload();
                    }
                    catch (Exception ex) { TaoDialog.Info(this, "恢复失败", ex.Message); }
                },
            },
            new AnchorMenu.Item
            {
                Text = "彻底删除",
                Danger = true,
                OnClick = () => TaoDialog.Confirm(this, null,
                    $"彻底删除「{it.Name}」？此操作不可恢复。", "删除",
                    () => { try { TrashHelper.Purge(it); Reload(); } catch (Exception ex) { TaoDialog.Info(this, "删除失败", ex.Message); } },
                    primaryIsDanger: true),
            },
        });
    }

    void ConfirmClear()
    {
        var all = TrashHelper.ListTrash();
        if (all.Count == 0) { Toast.MakeText(this, new Java.Lang.String("回收站本来就是空的"), ToastLength.Short)!.Show(); return; }
        TaoDialog.Confirm(this, null,
            $"清空回收站里的 {all.Count} 项？彻底删除后无法再找回。", "清空",
            () =>
            {
                foreach (var it in all)
                {
                    try { TrashHelper.Purge(it); }
                    catch (Exception ex) { TaoDialog.Info(this, "删除失败：" + it.Name, ex.Message); break; }
                }
                Reload();
            }, primaryIsDanger: true);
    }

    sealed class TrashAdapter : BaseAdapter<TrashHelper.TrashItem>
    {
        readonly TrashActivity _a;
        public TrashAdapter(TrashActivity a) { _a = a; }
        public override TrashHelper.TrashItem this[int p] => _a._items[p];
        public override int Count => _a._items.Count;
        public override long GetItemId(int p) => p;

        public override View GetView(int p, View? cv, ViewGroup? parent)
        {
            var it = _a._items[p];
            // 2.12 真机反馈：行与文件页同款——行透明（背景交给整卡容器），行间发丝线分隔
            var row = new LinearLayout(_a) { Orientation = Orientation.Horizontal, Clickable = true };
            row.SetGravity(GravityFlags.CenterVertical);
            row.SetPadding(_a.Dp(16), _a.Dp(9), _a.Dp(6), _a.Dp(9));
            var icon = new IconView(_a);
            var (kind, colorKey) = it.IsDir
                ? (IconKind.Folder, ThemeService.Key.Primary)
                : IconMap.For(it.EntryPath, false);
            icon.Bind(kind, colorKey);
            row.AddView(icon, _a.Dp(30), _a.Dp(30));
            var mid = new LinearLayout(_a) { Orientation = Orientation.Vertical };
            var name = new TextView(_a) { Text = it.Name, TextSize = 15, Ellipsize = TextUtils.TruncateAt.Middle };
            name.SetSingleLine(true);
            name.SetTextColor(ThemeService.C(ThemeService.Key.TextMain));
            mid.AddView(name);
            var from = string.IsNullOrEmpty(it.OriginalPath) ? "原位置未知" : it.OriginalPath;
            var sub = new TextView(_a)
            {
                Text = $"{(it.IsDir ? "文件夹" : MainActivity.FmtSize(it.Size))} · 原位置 {from}",
                TextSize = 11,
                Ellipsize = TextUtils.TruncateAt.Middle,
            };
            sub.SetSingleLine(true);
            sub.SetTextColor(ThemeService.C(ThemeService.Key.TextHint));
            mid.AddView(sub);
            row.AddView(mid, new LinearLayout.LayoutParams(0, -2, 1f) { LeftMargin = _a.Dp(10) });
            var more = new IconView(_a) { Clickable = true };
            more.Bind(IconKind.More, ThemeService.Key.TextHint);
            more.Click += (s, e) => _a.ItemMenu(it, more);
            row.AddView(more, new LinearLayout.LayoutParams(_a.Dp(38), _a.Dp(30)));
            row.Click += (s, e) => _a.OpenPreview(it, row);
            var outer = new FrameLayout(_a);
            outer.SetPadding(_a.Dp(10), _a.Dp(3), _a.Dp(10), _a.Dp(3));
            outer.AddView(row, new FrameLayout.LayoutParams(-1, -2));
            // 行间发丝线：极淡分隔，照文件页 SwipeRow 的写法融进整卡
            var div = ThemeService.C(ThemeService.Key.Divider);
            var hair = new View(_a);
            hair.Background = new ColorDrawable(new global::Android.Graphics.Color(
                (byte)(div.A * 0.35f), div.R, div.G, div.B));
            outer.AddView(hair, new FrameLayout.LayoutParams(-1, Math.Max(1, _a.Dp(1)), GravityFlags.Bottom));
            return outer;
        }
    }
}

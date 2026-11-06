using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.Core.View;
using TaoZip.BLL;
using TaoZip.Model;

namespace TaoZip.UI.Android;

/// <summary>
/// 任务中心（2.14）：所有压缩/解压任务的临时列表管理页——进行中的看进度、
/// 排队中的可取消、历史可清空。通知栏点进来或顶栏任务图标随时唤起。
/// </summary>
[Activity(Label = "任务中心", Exported = false,
    Theme = "@android:style/Theme.Material.Light.NoActionBar",
    ScreenOrientation = ScreenOrientation.Portrait,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
public class TasksActivity : Activity
{
    LinearLayout _list = null!;
    LinearLayout _empty = null!;
    bool _subscribed;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ThemeService.Restore(this);

        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetBackgroundColor(ThemeService.C(ThemeService.Key.PageBg));

        // 顶栏：返回 + 标题 + 清空（2.16 真机反馈：样式对齐回收站——清空收进顶栏危险色垃圾桶）
        var header = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        header.SetGravity(GravityFlags.CenterVertical);
        header.SetPadding(Dp(10), Dp(14), Dp(12), Dp(10));
        var back = new IconView(this) { Clickable = true };
        back.Bind(IconKind.Back, ThemeService.Key.TextMain);
        back.Click += (s, e) => Finish();
        header.AddView(back, Dp(36), Dp(36));
        var title = new TextView(this) { Text = "任务中心", TextSize = 17 };
        title.SetTypeface(Typeface.DefaultBold!, TypefaceStyle.Normal);
        title.SetTextColor(ThemeService.C(ThemeService.Key.TextMain));
        header.AddView(title, new LinearLayout.LayoutParams(0, -2, 1f) { LeftMargin = Dp(8) });
        var clear = new IconView(this) { Clickable = true };
        clear.Bind(IconKind.Trash, ThemeService.Key.Danger);
        clear.Click += (s, e) => ConfirmClear();
        header.AddView(clear, new LinearLayout.LayoutParams(Dp(30), Dp(30)) { RightMargin = Dp(10) });
        root.AddView(header);

        // 列表整卡：与回收站同款 24dp 大圆角卡包住列表，高度占满页面
        _list = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _list.SetClipToPadding(false);
        _list.SetPadding(Dp(4), Dp(10), Dp(4), Dp(10));
        var scroll = new ScrollView(this);
        // 2.15 修复保留：_list 只能挂一次父容器——由 scroll 统一装载
        scroll.AddView(_list);
        var host = new FrameLayout(this);
        host.AddView(scroll, new FrameLayout.LayoutParams(-1, -1));
        _empty = new LinearLayout(this) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone };
        _empty.SetGravity(GravityFlags.Center);
        var ei = new IconView(this);
        ei.Bind(IconKind.Archive, ThemeService.Key.Stroke);
        _empty.AddView(ei, Dp(56), Dp(56));
        var emptyMain = new TextView(this) { Text = "还没有压缩解压任务", TextSize = 15 };
        emptyMain.SetTextColor(ThemeService.C(ThemeService.Key.TextSub));
        _empty.AddView(emptyMain, new LinearLayout.LayoutParams(-2, -2) { TopMargin = Dp(12) });
        host.AddView(_empty, new FrameLayout.LayoutParams(-1, -1));
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
        Refresh();
    }

    void ConfirmClear()
    {
        TaoDialog.Confirm(this, "清空已结束记录",
            "只清掉已完成、失败与已取消的任务，正在进行的任务不受影响。确定清空吗？",
            "清空", () =>
            {
                int n = MainActivity.Service.RemoveFinished();
                Toast.MakeText(this, new Java.Lang.String(n > 0 ? $"已清空 {n} 条记录" : "没有可清的记录"), ToastLength.Short)!.Show();
                Refresh();
            });
    }

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

    protected override void OnResume()
    {
        base.OnResume();
        MainActivity.Service.TaskUpdated += OnUpdated;
        _subscribed = true;
        Refresh();
    }

    protected override void OnPause()
    {
        base.OnPause();
        if (_subscribed) { MainActivity.Service.TaskUpdated -= OnUpdated; _subscribed = false; }
    }

    void OnUpdated(ArchiveTaskRecord r) => RunOnUiThread(Refresh);

    void Refresh()
    {
        var records = MainActivity.Service.Records;
        _list.RemoveAllViews();
        bool empty = records.Count == 0;
        _empty.Visibility = empty ? ViewStates.Visible : ViewStates.Gone;
        _list.Visibility = empty ? ViewStates.Gone : ViewStates.Visible;
        // 新任务排上面
        foreach (var r in records.Reverse())
            _list.AddView(MakeCard(r), new LinearLayout.LayoutParams(-1, -2)
            { LeftMargin = Dp(8), RightMargin = Dp(8), TopMargin = Dp(4), BottomMargin = Dp(8) });
    }

    View MakeCard(ArchiveTaskRecord r)
    {
        var card = new LinearLayout(this) { Orientation = Orientation.Vertical };
        card.SetPadding(Dp(14), Dp(12), Dp(14), Dp(12));
        var bg = new GradientDrawable();
        bg.SetCornerRadius(Dp(16));
        bg.SetColor(ThemeService.C(ThemeService.Key.CardBg));
        card.Background = bg;

        // 标题行：任务名 + 状态徽标
        var top = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        top.SetGravity(GravityFlags.CenterVertical);
        var name = new TextView(this)
        {
            Text = r.Title, TextSize = 15,
        };
        name.SetTypeface(Typeface.DefaultBold!, TypefaceStyle.Bold);
        name.SetTextColor(ThemeService.C(ThemeService.Key.TextMain));
        top.AddView(name, new LinearLayout.LayoutParams(0, -2, 1f));
        var (stateText, stateKey) = r.State switch
        {
            TaskState.Queued => ("排队中", ThemeService.Key.TextHint),
            TaskState.Running => ("进行中", ThemeService.Key.Primary),
            TaskState.Done => ("已完成", ThemeService.Key.Accent),
            TaskState.Failed => ("失败", ThemeService.Key.Danger),
            _ => ("已取消", ThemeService.Key.TextHint),
        };
        var state = new TextView(this) { Text = stateText, TextSize = 13 };
        state.SetTextColor(ThemeService.C(stateKey));
        top.AddView(state, new LinearLayout.LayoutParams(-2, -2) { LeftMargin = Dp(8) });
        card.AddView(top);

        // 进度条 + 百分比 + 剩余时间（进度条只在排队/进行中显示，完成任务不再画圈）
        var live = r.State is TaskState.Queued or TaskState.Running;
        if (live)
        {
            var bar = new ProgressBar(this)!;
            bar.Max = 100;
            bar.Progress = r.ProgressPercent;
            bar.Indeterminate = r.State == TaskState.Queued;
            card.AddView(bar, new LinearLayout.LayoutParams(-1, -2) { TopMargin = Dp(8) });
        }

        var sub = r.State switch
        {
            TaskState.Running => $"{r.ProgressPercent}% · {TaskForegroundService.EstimateEtaPublic(r.Id, r.ProgressPercent, r.StartedAt)}",
            TaskState.Queued => "排在当前任务后面，轮到就跑",
            TaskState.Done => $"{r.ProgressPercent}% · {r.FinishedAt:HH:mm:ss} 完成",
            TaskState.Failed => r.Message ?? "出错了",
            _ => "已取消",
        };
        var subText = new TextView(this) { Text = sub, TextSize = 13 };
        subText.SetTextColor(ThemeService.C(r.State == TaskState.Failed ? ThemeService.Key.Danger : ThemeService.Key.TextSub));
        card.AddView(subText, new LinearLayout.LayoutParams(-2, -2) { TopMargin = Dp(6) });

        // 取消：只有排队/进行中的任务能取消
        if (live)
        {
            var cancel = new TextView(this)
            {
                Text = "取消", TextSize = 14, Gravity = GravityFlags.Center,
            };
            cancel.SetTextColor(ThemeService.C(ThemeService.Key.Danger));
            cancel.SetTypeface(Typeface.DefaultBold!, TypefaceStyle.Bold);
            cancel.Click += (s, e) => { MainActivity.Service.Cancel(r.Id); Refresh(); };
            var cb = new GradientDrawable();
            cb.SetCornerRadius(Dp(999));
            cb.SetStroke(Dp(1), ThemeService.C(ThemeService.Key.Danger));
            cancel.Background = cb;
            card.AddView(cancel, new LinearLayout.LayoutParams(-2, Dp(30))
            { TopMargin = Dp(10), Gravity = GravityFlags.Right });
        }
        return card;
    }

    int Dp(int v) => (int)(v * Resources!.DisplayMetrics!.Density);
}

using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Widget;
using TaoZip.BLL;
using TaoZip.Model;

using Path = System.IO.Path;

namespace TaoZip.UI.Android;

/// <summary>
/// 压缩包浏览页 2.0：水墨三象统一视觉——卡片化条目、随象图标、
/// 底部主操作栏（解压为主动作）、密码/编码/完整性测试齐备。
/// 2.7：与回收站页同型问题全系排查——显式三象浅色 NoActionBar 主题，杜绝系统默认深色标题。
/// </summary>
[Activity(Label = "TaoZip", Exported = false,
    Theme = "@android:style/Theme.Material.Light.NoActionBar",
    ScreenOrientation = ScreenOrientation.Portrait,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
public class ArchiveActivity : Activity
{
    FrameLayout _root = null!;
    TextView _title = null!, _meta = null!, _comment = null!;
    ListView _list = null!;
    LinearLayout _emptyBox = null!, _bar = null!;
    EntryAdapter _adapter = null!;

    string _path = "";
    string _password = "";
    string _encoding = "utf-8";
    ArchiveInfo? _info;
    bool _loading;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ThemeService.Restore(this);
        _path = Intent?.GetStringExtra("path") ?? "";
        Title = Path.GetFileName(_path);
        BuildUi();
        Reload();
    }

    void BuildUi()
    {
        // 2.23 真机反馈：底部操作栏改浮动胶囊——内容层铺满，底栏悬浮盖在内容上（与文件页同款）
        _root = new FrameLayout(this);
        _root.SetBackgroundColor(ThemeService.C(ThemeService.Key.PageBg));
        var page = new LinearLayout(this) { Orientation = Orientation.Vertical };

        // 顶栏：返回 + 标题
        var header = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        header.SetGravity(GravityFlags.CenterVertical);
        header.SetBackgroundColor(ThemeService.C(ThemeService.Key.PageBg));
        header.SetPadding(Dp(10), Dp(14), Dp(16), Dp(10));
        var back = new IconView(this) { Clickable = true };
        back.Bind(IconKind.Back, ThemeService.Key.TextMain);
        back.Click += (s, e) => Finish();
        header.AddView(back, Dp(36), Dp(36));
        _title = new TextView(this) { TextSize = 17, Ellipsize = TextUtils.TruncateAt.Middle };
        _title.SetSingleLine(true);
        _title.SetTypeface(Typeface.DefaultBold!, TypefaceStyle.Normal);
        _title.SetTextColor(ThemeService.C(ThemeService.Key.TextMain));
        header.AddView(_title, new LinearLayout.LayoutParams(0, -2, 1f) { LeftMargin = Dp(8) });
        page.AddView(header);

        // 信息卡：格式徽标 + 统计 + 注释
        var cardWrap = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var cardBg = new GradientDrawable();
        cardBg.SetCornerRadius(Dp(16));
        cardBg.SetColor(ThemeService.C(ThemeService.Key.CardBg));
        cardWrap.Background = cardBg;
        cardWrap.SetPadding(Dp(16), Dp(12), Dp(16), Dp(12));
        _meta = new TextView(this) { TextSize = 12 };
        _meta.SetTextColor(ThemeService.C(ThemeService.Key.TextSub));
        cardWrap.AddView(_meta);
        _comment = new TextView(this) { TextSize = 12 };
        _comment.SetTextColor(ThemeService.C(ThemeService.Key.TextHint));
        _comment.Visibility = ViewStates.Gone;
        _comment.SetPadding(0, Dp(6), 0, 0);
        cardWrap.AddView(_comment);
        page.AddView(cardWrap, new LinearLayout.LayoutParams(-1, -2)
        {
            LeftMargin = Dp(12), RightMargin = Dp(12), TopMargin = Dp(4),
        });

        // 条目列表 + 空态
        _adapter = new EntryAdapter(this);
        _list = new ListView(this)
        {
            Divider = new global::Android.Graphics.Drawables.ColorDrawable(global::Android.Graphics.Color.Transparent),
            DividerHeight = Dp(6),
            Selector = new global::Android.Graphics.Drawables.ColorDrawable(global::Android.Graphics.Color.Transparent),
        };
        _list.Adapter = _adapter;
        _list.SetClipToPadding(false);
        // 2.23：底栏悬浮后列表底部留出胶囊让位，最后几行不被盖住
        _list.SetPadding(0, Dp(4), 0, Dp(84));
        _emptyBox = new LinearLayout(this) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone };
        _emptyBox.SetGravity(GravityFlags.Center);
        var emptyIcon = new IconView(this);
        emptyIcon.Bind(IconKind.Archive, ThemeService.Key.Stroke);
        _emptyBox.AddView(emptyIcon, Dp(52), Dp(52));
        var emptyText = new TextView(this) { Text = "正在读这个压缩包…", TextSize = 14 };
        emptyText.SetTextColor(ThemeService.C(ThemeService.Key.TextSub));
        _emptyBox.AddView(emptyText, new LinearLayout.LayoutParams(-2, -2) { TopMargin = Dp(12) });
        var host = new FrameLayout(this);
        host.AddView(_list, new FrameLayout.LayoutParams(-1, -1));
        host.AddView(_emptyBox, new FrameLayout.LayoutParams(-1, -1));
        page.AddView(host, new LinearLayout.LayoutParams(-1, 0, 1f));
        _root.AddView(page, new FrameLayout.LayoutParams(-1, -1));

        // 底栏（2.23 改浮动胶囊）：与文件页批量浮层同款半透明胶囊，悬浮盖在列表上
        _bar = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        _bar.SetGravity(GravityFlags.CenterVertical);
        var pillBg = new GradientDrawable();
        pillBg.SetCornerRadius(Dp(30));
        var fbg = ThemeService.C(ThemeService.Key.FloatBg);
        pillBg.SetColor(new global::Android.Graphics.Color((byte)(fbg.A * 0.78f), fbg.R, fbg.G, fbg.B));
        pillBg.SetStroke(Dp(1), ThemeService.C(ThemeService.Key.Stroke));
        _bar.Background = pillBg;
        _bar.Elevation = Dp(6);
        _bar.SetPadding(Dp(8), Dp(8), Dp(8), Dp(8));
        BarButton(_bar, "密码", AskPassword);
        BarButton(_bar, "编码", AskEncoding);
        BarButton(_bar, "测完整性", () => RunTest(), danger: true);
        BarButton(_bar, "解压到…", DoExtract, primary: true);
        _root.AddView(_bar, new FrameLayout.LayoutParams(-1, -2, GravityFlags.Bottom)
        { LeftMargin = Dp(14), RightMargin = Dp(14), BottomMargin = Dp(12) });

        SetContentView(_root, new ViewGroup.LayoutParams(-1, -1));
        Window!.SetStatusBarColor(ThemeService.C(ThemeService.Key.PageBg));
        Window.SetNavigationBarColor(ThemeService.C(ThemeService.Key.PageBg));
        var ctrl = AndroidX.Core.View.WindowCompat.GetInsetsController(Window!, Window!.DecorView);
        if (ctrl != null) ctrl.AppearanceLightStatusBars = !ThemeService.LightStatusIcons;
    }

    void BarButton(LinearLayout bar, string text, Action onClick, bool primary = false, bool danger = false)
    {
        var b = new TextView(this) { Text = text, TextSize = 14, Gravity = GravityFlags.Center };
        b.SetTypeface(Typeface.DefaultBold!, TypefaceStyle.Normal);
        b.SetPadding(Dp(6), Dp(9), Dp(6), Dp(9));
        b.SetTextColor(primary ? ThemeService.C(ThemeService.Key.OnPrimary)
            : danger ? ThemeService.C(ThemeService.Key.Danger)
            : ThemeService.C(ThemeService.Key.TextMain));
        var bg = new GradientDrawable();
        bg.SetCornerRadius(Dp(12));
        bg.SetColor(primary ? ThemeService.C(ThemeService.Key.Primary)
            : ThemeService.C(ThemeService.Key.CardBg));
        b.Background = bg;
        b.Click += (s, e) => onClick();
        bar.AddView(b, new LinearLayout.LayoutParams(0, -2, 1f)
        {
            LeftMargin = Dp(4),
            RightMargin = Dp(4),
        });
    }

    // ---------- 加载 ----------

    void Reload()
    {
        if (_loading) return;
        _loading = true;
        _title.Text = Path.GetFileName(_path);
        _meta.Text = "读取中…";
        Task.Run(() =>
        {
            try
            {
                var info = ArchiveEngine.OpenInfo(_path, string.IsNullOrEmpty(_password) ? null : _password, _encoding);
                RunOnUiThread(() =>
                {
                    _loading = false;
                    _info = info;
                    var enc = info.Entries.Count(e => !e.IsDirectory);
                    _meta.Text = $"{info.Format.ToUpperInvariant()} · {enc} 个文件 · 原始 {MainActivity.FmtSize(info.UncompressedTotal)}"
                               + (info.IsEncrypted ? " · 已加密" : "");
                    if (!string.IsNullOrEmpty(info.Comment))
                    {
                        _comment.Text = "备注：" + info.Comment;
                        _comment.Visibility = ViewStates.Visible;
                    }
                    _emptyBox.Visibility = ViewStates.Gone;
                    _adapter.NotifyDataSetChanged();
                });
            }
            catch (Exception ex)
            {
                RunOnUiThread(() =>
                {
                    _loading = false;
                    _meta.Text = "读取失败：" + ex.Message;
                    if (ex.Message.Contains("加密") || ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase)
                        || ex.Message.Contains("Crc", StringComparison.OrdinalIgnoreCase) || infoLooksEncrypted(ex))
                        AskPassword();
                    else
                        TaoDialog.Info(this, "打不开这个压缩包", ex.Message + "\n\n如果反复出现，可能是文件下载不完整或格式不支持。");
                });
            }
        });
    }

    bool infoLooksEncrypted(Exception ex) => ex.Message.Contains("密钥") || ex.Message.Contains("Incorrect");

    /// <summary>列表条目：随象图标 + 路径 + 大小/时间。行间距用透明分隔条，行本身是卡片。</summary>
    class EntryAdapter : BaseAdapter<ArchiveEntry>
    {
        readonly ArchiveActivity _a;
        public EntryAdapter(ArchiveActivity a) { _a = a; }
        public override ArchiveEntry this[int p] => _a._info!.Entries[p];
        public override int Count => _a._info?.Entries.Count ?? 0;
        public override long GetItemId(int p) => p;

        sealed class RowRefs : Java.Lang.Object
        {
            public readonly IconView Icon;
            public readonly TextView Name, Sub;
            public RowRefs(IconView i, TextView n, TextView s) { Icon = i; Name = n; Sub = s; }
        }

        public override View GetView(int p, View? cv, ViewGroup? parent)
        {
            var e = _a._info!.Entries[p];
            LinearLayout row;
            IconView icon;
            TextView name, sub;
            if (cv is LinearLayout l && l.Tag is RowRefs refs)
            {
                row = l;
                icon = refs.Icon;
                name = refs.Name;
                sub = refs.Sub;
            }
            else
            {
                row = new LinearLayout(_a) { Orientation = Orientation.Horizontal };
                row.SetGravity(GravityFlags.CenterVertical);
                row.SetPadding(_a.Dp(12), _a.Dp(8), _a.Dp(12), _a.Dp(8));
                var rowBg = new GradientDrawable();
                rowBg.SetCornerRadius(_a.Dp(12));
                rowBg.SetColor(ThemeService.C(ThemeService.Key.CardBg));
                row.Background = rowBg;
                icon = new IconView(_a);
                row.AddView(icon, _a.Dp(28), _a.Dp(28));
                var mid = new LinearLayout(_a) { Orientation = Orientation.Vertical };
                name = new TextView(_a) { TextSize = 14, Ellipsize = TextUtils.TruncateAt.Middle };
                name.SetSingleLine(true);
                name.SetTextColor(ThemeService.C(ThemeService.Key.TextMain));
                sub = new TextView(_a) { TextSize = 11 };
                sub.SetTextColor(ThemeService.C(ThemeService.Key.TextHint));
                mid.AddView(name);
                mid.AddView(sub);
                row.AddView(mid, new LinearLayout.LayoutParams(-1, -2) { LeftMargin = _a.Dp(10) });
                row.Tag = new RowRefs(icon, name, sub);
            }
            name.Text = e.Path;
            sub.Text = e.IsDirectory ? "文件夹"
                : $"{MainActivity.FmtSize(e.Size)} · {e.Modified:yyyy-MM-dd HH:mm}" + (e.IsEncrypted ? " · 加密" : "");
            var (kind, colorKey) = e.IsDirectory
                ? (IconKind.Folder, ThemeService.Key.Primary)
                : IconMap.For(e.Path, isDir: false);
            icon.Bind(kind, colorKey);
            return row;
        }
    }

    // ---------- 交互 ----------

    void AskPassword()
    {
        var input = TaoDialog.MakeInput(this, "压缩包密码", password: true);
        if (!string.IsNullOrEmpty(_password)) input.Text = _password;
        var box = new LinearLayout(this) { Orientation = Orientation.Vertical };
        box.AddView(input);
        TaoDialog.Custom(this, "加密压缩包", box, "确定", _ =>
        {
            _password = input.Text ?? "";
            Reload();
        }, cancelText: "取消");
    }

    ProgressDialog MakeProgress(string title, string message)
    {
        var d = new ProgressDialog(this);
        d.SetTitle(title);
        d.SetMessage(message);
        d.SetCancelable(false);
        return d;
    }

    void AskEncoding()
    {
        // 单选弹窗：逐项点选即生效并重新加载
        var box = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var opts = new[] { "UTF-8（默认）", "GBK（旧版中文压缩包）" };
        for (int i = 0; i < opts.Length; i++)
        {
            var idx = i;
            var row = new TextView(this) { Text = (_encoding == (idx == 1 ? "gbk" : "utf-8") ? "● " : "○ ") + opts[i], TextSize = 15 };
            row.SetPadding(Dp(4), Dp(12), Dp(4), Dp(12));
            row.SetTextColor(ThemeService.C(_encoding == (idx == 1 ? "gbk" : "utf-8") ? ThemeService.Key.Primary : ThemeService.Key.TextMain));
            row.Click += (s, e) =>
            {
                _encoding = idx == 1 ? "gbk" : "utf-8";
                Reload();
            };
            box.AddView(row);
        }
        TaoDialog.Custom(this, "文件名编码（选择即重新加载）", box, "完成", _ => { }, cancelText: null);
    }

    void RunTest()
    {
        var progressDialog = MakeProgress("完整性测试", "进行中…");
        progressDialog.SetCancelable(false);
        progressDialog.Show();
        Task.Run(() =>
        {
            try
            {
                var bad = ArchiveEngine.Test(_path, string.IsNullOrEmpty(_password) ? null : _password);
                RunOnUiThread(() =>
                {
                    progressDialog.Dismiss();
                    if (bad.Count == 0)
                        TaoDialog.Info(this, "测试通过", "所有条目完整，没有发现损坏。");
                    else
                        TaoDialog.Info(this, $"发现 {bad.Count} 个问题",
                            string.Join("\n", bad.Take(20)) + (bad.Count > 20 ? $"\n…共 {bad.Count} 条" : ""));
                });
            }
            catch (Exception ex)
            {
                RunOnUiThread(() =>
                {
                    progressDialog.Dismiss();
                    TaoDialog.Info(this, "测试没跑成", ex.Message + "\n\n加密包请先点「密码」输入后再试。");
                });
            }
        });
    }

    void DoExtract()
    {
        if (_info == null) return;
        // 2.23 真机反馈：目标文件夹可挑，默认选中压缩包所在目录
        var parent = Path.GetDirectoryName(_path) ?? "/";
        var box = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var input = TaoDialog.MakeInput(this, "解压到哪个文件夹");
        input.Text = parent;
        box.AddView(input);
        box.AddView(FolderPickLink(input, () => parent));
        if (_info.IsEncrypted && string.IsNullOrEmpty(_password))
        {
            var warn = new TextView(this) { Text = "这个压缩包已加密，请先点「密码」输入后再解压。", TextSize = 12 };
            warn.SetTextColor(ThemeService.C(ThemeService.Key.Accent));
            warn.SetPadding(Dp(4), Dp(8), Dp(4), 0);
            box.AddView(warn);
        }

        TaoDialog.Custom(this, "解压到", box, "开始解压", _ =>
        {
            var dest = input.Text?.Trim();
            if (string.IsNullOrEmpty(dest)) { Toast.MakeText(this, new Java.Lang.String("先填解压到哪个文件夹"), ToastLength.Short)!.Show(); return; }
            var req = new ExtractRequest
            {
                ArchivePath = _path,
                DestDir = dest,
                Password = string.IsNullOrEmpty(_password) ? null : _password,
                FileNameEncoding = _encoding,
            };
            var progressDialog = new ProgressDialog(this);
            progressDialog.SetTitle("解压中");
            progressDialog.SetMessage("准备中…");
            progressDialog.SetCancelable(false);
            progressDialog.SetButton((int)DialogButtonType.Negative, "取消", (s2, e2) =>
            {
                var running = MainActivity.Service.Records.LastOrDefault(r => r.State is TaskState.Queued or TaskState.Running);
                if (running != null) MainActivity.Service.Cancel(running.Id);
            });
            progressDialog.Show();
            var record = MainActivity.Service.Enqueue(TaskKind.Extract, "解压 " + Path.GetFileName(_path),
                (id, p, c) => ArchiveEngine.Extract(req, id, p, c));
            void OnUpdated(ArchiveTaskRecord r)
            {
                if (!ReferenceEquals(r, record)) return;
                if (r.State == TaskState.Running)
                    RunOnUiThread(() => progressDialog.SetMessage($"进行中 {r.ProgressPercent}%"));
                else
                {
                    MainActivity.Service.TaskUpdated -= OnUpdated;
                    RunOnUiThread(() =>
                    {
                        progressDialog.Dismiss();
                        if (r.State == TaskState.Done)
                            Toast.MakeText(this, new Java.Lang.String("解压完成：" + dest), ToastLength.Long)!.Show();
                        else if (r.State == TaskState.Failed)
                            TaoDialog.Info(this, "解压失败", (r.Message ?? "") + "\n\n加密包请先点「密码」；存储空间不足时清一色提示，可先清理空间再试。");
                        else if (r.State == TaskState.Cancelled)
                            Toast.MakeText(this, new Java.Lang.String("已取消"), ToastLength.Short)!.Show();
                    });
                }
            }
            MainActivity.Service.TaskUpdated += OnUpdated;
        });
    }

    int Dp(int v) => (int)(v * Resources!.DisplayMetrics!.Density);

    /// <summary>解压弹窗里的「选个文件夹」入口（2.23 真机反馈）：点了弹目录选择器，选完回填输入框。</summary>
    TextView FolderPickLink(EditText input, Func<string> startDir)
    {
        var link = new TextView(this) { Text = "📁 选个文件夹", TextSize = 13 };
        link.SetTypeface(Typeface.DefaultBold!, TypefaceStyle.Normal);
        link.SetTextColor(ThemeService.C(ThemeService.Key.Accent));
        link.SetPadding(Dp(2), Dp(10), Dp(2), Dp(2));
        link.Click += (s, e) => PickFolder(input.Text?.Trim() is { Length: > 0 } cur && Directory.Exists(cur) ? cur : startDir(),
            pick => input.Text = pick);
        return link;
    }

    /// <summary>简易目录选择器（2.23 真机反馈）：列表逐级进退，顶部常显当前路径，「用这里」确认。</summary>
    void PickFolder(string start, Action<string> onPick)
    {
        var cur = start;
        var shown = new List<string>();
        var pathLabel = new TextView(this) { TextSize = 11, Ellipsize = TextUtils.TruncateAt.Start };
        pathLabel.SetSingleLine(true);
        pathLabel.SetTextColor(ThemeService.C(ThemeService.Key.TextSub));
        pathLabel.SetPadding(Dp(2), 0, Dp(2), Dp(6));
        var listV = new ListView(this) { Divider = null, DividerHeight = 0 };
        var box = new LinearLayout(this) { Orientation = Orientation.Vertical };
        box.AddView(pathLabel);
        box.AddView(listV, new LinearLayout.LayoutParams(-1, Dp(340)));

        void Load(string dir)
        {
            cur = dir;
            pathLabel.Text = dir;
            var dirs = new List<string>();
            var parent = Path.GetDirectoryName(dir.TrimEnd('/'));
            if (!string.IsNullOrEmpty(parent)) dirs.Add("⬆ 上级目录");
            try
            {
                foreach (var d in Directory.EnumerateDirectories(dir).OrderBy(p => p))
                    dirs.Add("📁 " + Path.GetFileName(d));
            }
            catch { }
            shown = dirs;
            listV.Adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleListItem1, dirs);
        }
        listV.ItemClick += (s, e) =>
        {
            var pick = shown[e.Position];
            if (pick.StartsWith("⬆"))
            {
                var parent = Path.GetDirectoryName(cur.TrimEnd('/'));
                Load(string.IsNullOrEmpty(parent) ? "/" : parent);
            }
            else Load(Path.Combine(cur, pick[2..]));
        };
        TaoDialog.Custom(this, "选择目标文件夹", box, "用这里", _ => onPick(cur), cancelText: null);
        Load(start);
    }
}

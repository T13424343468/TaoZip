using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Provider;
using Android.Text;
using Android.Views;
using Android.Widget;
using TaoZip.BLL;
using TaoZip.Model;
using AndroidX.Core.Content;
using AndroidX.Core.View;
using FS = System.IO;
using Path = System.IO.Path;
using AEnvironment = Android.OS.Environment;

// 权限声明（程序集级）
[assembly: UsesPermission(global::Android.Manifest.Permission.ReadExternalStorage)]
[assembly: UsesPermission(global::Android.Manifest.Permission.WriteExternalStorage)]
[assembly: UsesPermission(global::Android.Manifest.Permission.ManageExternalStorage)]
[assembly: UsesPermission(global::Android.Manifest.Permission.PostNotifications)]
[assembly: UsesPermission(global::Android.Manifest.Permission.ForegroundService)]
[assembly: UsesPermission(global::Android.Manifest.Permission.ForegroundServiceDataSync)]

namespace TaoZip.UI.Android;

/// <summary>
/// 主界面 2.7：水墨三象主题（水墨默认 / 天灰 / 墨夜），按人的真实目的组织——
/// 两页签「文件 / 压缩包」（2.7 砍掉独立分类页）：文件页管目录与空间，分类筛选并入
/// 文件页单页完成；压缩包页聚合最近压缩包直达浏览解压。空态齐备，弹窗统一水墨卡片。
/// </summary>
/// <summary>列表条目视图模型：目录、文件、分类与压缩包入口统一表示。</summary>
internal sealed class EntryVM
{
    public required string Name { get; init; }
    public required string FullName { get; init; }
    public bool IsDir { get; init; }
    public long Size { get; init; }
    public DateTime Time { get; init; }
    public string Ext => IsDir ? "" : Path.GetExtension(FullName);
    public (IconKind Kind, ThemeService.Key Color) Icon => IconMap.For(FullName, IsDir);
    public string Sub => IsDir ? "文件夹" : $"{MainActivity.FmtSize(Size)} · {Time:yyyy-MM-dd HH:mm}";

    public static EntryVM From(FS.FileSystemInfo f) => new()
    {
        Name = f.Name,
        FullName = f.FullName,
        IsDir = f is FS.DirectoryInfo,
        Size = f is FS.FileInfo fi ? fi.Length : 0,
        Time = f.LastWriteTime,
    };
}

[Activity(Label = "TaoZip", MainLauncher = true, Exported = true,
    Theme = "@android:style/Theme.Material.Light.NoActionBar",
    LaunchMode = LaunchMode.SingleTask,
    ScreenOrientation = ScreenOrientation.Portrait,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
// 系统文件管理器中压缩文件 → 用 TaoZip 打开：按 MIME 类型注册
[IntentFilter(new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataMimeType = "application/zip", Label = "TaoZip")]
[IntentFilter(new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataMimeType = "application/x-zip-compressed", Label = "TaoZip")]
[IntentFilter(new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataMimeType = "application/x-7z-compressed", Label = "TaoZip")]
[IntentFilter(new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataMimeType = "application/x-rar-compressed", Label = "TaoZip")]
[IntentFilter(new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataMimeType = "application/vnd.rar", Label = "TaoZip")]
[IntentFilter(new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataMimeType = "application/x-tar", Label = "TaoZip")]
[IntentFilter(new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataMimeType = "application/gzip", Label = "TaoZip")]
[IntentFilter(new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataMimeType = "application/x-gzip", Label = "TaoZip")]
[IntentFilter(new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataMimeType = "application/x-bzip2", Label = "TaoZip")]
[IntentFilter(new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataMimeType = "application/x-xz", Label = "TaoZip")]
[IntentFilter(new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataMimeType = "application/x-compressed", Label = "TaoZip")]
// 按扩展名兜底（部分文件管理器只给扩展名路径）
[IntentFilter(new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataPathPatterns = new[] { @".*\\.zip", @".*\\.ZIP", @".*\\.7z", @".*\\.7Z", @".*\\.rar", @".*\\.RAR",
        @".*\\.tar", @".*\\.TAR", @".*\\.gz", @".*\\.GZ", @".*\\.tgz", @".*\\.bz2", @".*\\.xz",
        @".*\\.001", @".*\\.cab", @".*\\.jar" }, Label = "TaoZip")]
public class MainActivity : Activity
{
    // 2.27 根修：任务服务惰性构建——安卓端历史落盘必须用应用私有目录，
    // 默认 AppContext.BaseDirectory 在安卓是 APK 所在系统目录（只读），写历史必抛异常，
    // 旧写法还会在任务收尾炸掉并发许可造成任务永远排队，连锁引发前台服务刷新时闪退
    static readonly object _serviceLock = new();
    static ArchiveService? _service;
    internal static ArchiveService Service
    {
        get { lock (_serviceLock) { return _service ??= CreateService(); } }
    }

    static ArchiveService CreateService()
    {
        try
        {
            var dir = global::Android.App.Application.Context!.FilesDir!.AbsolutePath;
            return new ArchiveService(new global::TaoZip.DAL.HistoryStore(FS.Path.Combine(dir, "taozip_history.json")));
        }
        catch
        {
            // 私有目录读不到就退回默认路径：丢历史记录也不允许挡任务执行
            return new ArchiveService();
        }
    }
    internal const string RootDir = "/storage/emulated/0";

    /// <summary>回收站里的东西只在回收站页出现，列表、分类、搜索、压缩包扫描一律不再显示。</summary>
    internal static bool InTrash(string p) =>
        !string.IsNullOrEmpty(p) && (p == TrashHelper.TrashDir || p.StartsWith(TrashHelper.TrashDir + "/"));
    internal const string Authority = "com.taozip.app.fileprovider";

    // ---- 跨重建状态（切主题 Recreate 后保持人的位置） ----
    internal static ViewMode Mode = ViewMode.Files;
    internal static string CurrentDir = RootDir;
    internal static SortMode Sort = SortMode.Name;
    internal static bool Asc = true, ShowHidden;
    internal static string? LastCategory;

    // ---- 视图状态 ----
    internal enum ViewMode { Files, Archives }
    internal enum SortMode { Name, Time, Size, Type }
    string? _category;
    string? _searchKeyword;

    FrameLayout _root = null!;
    LinearLayout _pageFiles = null!;
    LinearLayout _pathRow = null!;
    View _logo = null!;
    IconView _eyeIcon = null!;
    TextView _title = null!, _btnPaste = null!;
    HorizontalScrollView _chipsScroll = null!;
    LinearLayout _chipsBar = null!;
    readonly Dictionary<string, List<EntryVM>> _catCache = new();
    int _catScanGen;    // 2.16：分类后台扫描代号——删除后，删除前起飞的旧扫描回调不得复活已删行
    int _archScanGen;   // 2.16：压缩包后台扫描代号，同上
    readonly Dictionary<string, LinearLayout> _catTiles = new();
    List<EntryVM> _catAll = new();
    string? _catFilter;
    LinearLayout _storageCard = null!;
    TextView _storageText = null!;
    View _storageBarFill = null!;
    ListView _list = null!;
    LinearLayout _emptyBox = null!;
    TextView _emptyMain = null!, _emptySub = null!;
    LinearLayout _tabBar = null!;
    LinearLayout _tabFiles = null!, _tabArchives = null!;
    TextView _tabFilesText = null!, _tabArchivesText = null!;
    IconView _tabFilesIcon = null!, _tabArchivesIcon = null!;

    FileAdapter _adapter = null!;
    List<EntryVM> _items = new();
    List<string> _clipPaths = new();
    bool _clipMove;
    bool _pendingPermission;

    // 2.11 多选批量：长按进多选，列表右侧亮出竖排批量胶囊
    bool _selectMode;
    readonly HashSet<string> _selected = new();
    LinearLayout _batchBar = null!, _listCard = null!;
    bool _titleCompact;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ThemeService.Restore(this);
        BuildUi();
        if (RequestStorage())
        {
            _pendingPermission = false;
            Refresh();
        }
        else _pendingPermission = true;
        ApplyEdgeToEdge();
        HandleExternalIntent(Intent);
    }

    /// <summary>把系统栏/圆角安全边距变成根布局内边距。</summary>
    internal sealed class InsetsListener : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        public WindowInsetsCompat OnApplyWindowInsets(View v, WindowInsetsCompat ins)
        {
            var bars = ins.GetInsets(WindowInsetsCompat.Type.SystemBars());
            var cut = ins.GetInsets(WindowInsetsCompat.Type.DisplayCutout());
            v.SetPadding(
                Math.Max(bars.Left, cut.Left),
                Math.Max(bars.Top, cut.Top),
                Math.Max(bars.Right, cut.Right),
                Math.Max(bars.Bottom, cut.Bottom));
            return WindowInsetsCompat.Consumed;
        }
    }

    /// <summary>全面屏适配：内容延伸到系统栏之下，状态栏图标亮度随主题象。</summary>
    void ApplyEdgeToEdge()
    {
        var win = Window!;
        win.SetStatusBarColor(ThemeService.C(ThemeService.Key.PageBg));
        win.SetNavigationBarColor(ThemeService.C(ThemeService.Key.PageBg));
        if (Build.VERSION.SdkInt >= BuildVersionCodes.P)
            win.Attributes.LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.ShortEdges;
        WindowCompat.SetDecorFitsSystemWindows(win, false);
        var ctrl = WindowCompat.GetInsetsController(win, win.DecorView);
        if (ctrl != null) ctrl.AppearanceLightStatusBars = !ThemeService.LightStatusIcons;
        ViewCompat.SetOnApplyWindowInsetsListener(_root, new InsetsListener());
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleExternalIntent(intent);
    }

    /// <summary>从系统文件管理器「用 TaoZip 打开」进入：解析 URI 并跳转压缩包浏览页。</summary>
    void HandleExternalIntent(Intent? intent)
    {
        if (intent?.Action != Intent.ActionView || intent.Data == null) return;
        var uri = intent.Data;
        // 自家提供器直接是本地路径
        if (uri.Scheme == "content" && uri.Authority == Authority)
        {
            OpenExternalArchive(uri.Path ?? "");
            return;
        }
        // 其他来源：拷贝到缓存目录再打开
        var name = uri.LastPathSegment?.Split('/').Last() ?? "external.bin";
        var safeName = string.Concat(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
        if (!ArchiveEngine.IsArchiveFile(safeName)) safeName += ".bin";
        var tmp = new FileInfo(Path.Combine(CacheDir!.AbsolutePath, "in_" + Guid.NewGuid().ToString("N")[..6] + "_" + safeName));
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                using var src = ContentResolver!.OpenInputStream(uri)!;
                using var dst = tmp.OpenWrite();
                src.CopyTo(dst);
                RunOnUiThread(() => StartActivity(new Intent(this, typeof(ArchiveActivity)).PutExtra("path", tmp.FullName)));
            }
            catch (Exception ex) { ShowErr("打开失败", ex); }
        });
    }

    void OpenExternalArchive(string path)
    {
        if (File.Exists(path)) StartActivity(new Intent(this, typeof(ArchiveActivity)).PutExtra("path", path));
        else ToastText("文件不存在或已被移动");
    }

    protected override void OnResume()
    {
        base.OnResume();
        if (_pendingPermission && RequestStorage()) { _pendingPermission = false; Refresh(); }
        else if (!_pendingPermission) Refresh();
    }

    // ================= 权限 =================

    bool RequestStorage()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
        {
            if (AEnvironment.IsExternalStorageManager) return true;
            try
            {
                StartActivity(new Intent(Settings.ActionManageAppAllFilesAccessPermission,
                    global::Android.Net.Uri.Parse("package:" + ApplicationInfo!.PackageName)));
            }
            catch
            {
                StartActivity(new Intent(Settings.ActionManageAllFilesAccessPermission));
            }
            ToastText("请允许 TaoZip 访问所有文件，返回后即可使用");
            return false;
        }
        var need = new[] { global::Android.Manifest.Permission.ReadExternalStorage, global::Android.Manifest.Permission.WriteExternalStorage }
            .Where(p => CheckSelfPermission(p) != Permission.Granted).ToArray();
        if (need.Length == 0) return true;
        RequestPermissions(need, 1);
        return false;
    }

    /// <summary>安卓 13+ 通知权限：任务进度通知需要，用户拒绝也不影响任务照跑，只是通知栏不亮。</summary>
    internal static void EnsureNotificationPermission(Activity a)
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu) return;
        if (a.CheckSelfPermission(global::Android.Manifest.Permission.PostNotifications) == Permission.Granted) return;
        a.RequestPermissions(new[] { global::Android.Manifest.Permission.PostNotifications }, 2);
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == 1 && grantResults.All(g => g == Permission.Granted))
        {
            _pendingPermission = false;
            Refresh();
        }
    }

    // ================= 界面 =================

    void BuildUi()
    {
        // 一加式骨架：内容层铺满，顶部/底部各一条同款悬浮胶囊盖在内容上
        _root = new FrameLayout(this);
        _root.SetBackgroundColor(ThemeService.C(ThemeService.Key.PageBg));

        // 文件页容器：顶部先让出悬浮胶囊的高度；搜索分类合并卡 + 存储卡 + 面包屑 + 列表
        _pageFiles = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _pageFiles.SetPadding(0, Dp(66), 0, 0);
        _root.AddView(_pageFiles, new FrameLayout.LayoutParams(-1, -1));
        BuildHomeStrip();
        BuildStorageCard();
        BuildFileListArea();

        // 底部悬浮胶囊标签栏：文件 / 压缩包（2.7 砍掉分类页签，分类筛选并入文件页）
        _tabBar = MakeBar();
        var pillBg = new GradientDrawable();
        pillBg.SetCornerRadius(Dp(30));
        pillBg.SetColor(ThemeService.C(ThemeService.Key.Primary));
        _tabBar.Background = pillBg;
        _tabBar.Elevation = Dp(6);
        _tabBar.SetPadding(Dp(6), Dp(6), Dp(6), Dp(6));
        _tabFiles = MakeTab(IconKind.Folder, "文件", out _tabFilesIcon, out _tabFilesText);
        _tabFiles.Click += (s, e) => SwitchMode(ViewMode.Files);
        _tabBar.AddView(_tabFiles, new LinearLayout.LayoutParams(0, -2, 1f));
        _tabArchives = MakeTab(IconKind.Archive, "压缩包", out _tabArchivesIcon, out _tabArchivesText);
        _tabArchives.Click += (s, e) => SwitchMode(ViewMode.Archives);
        _tabBar.AddView(_tabArchives, new LinearLayout.LayoutParams(0, -2, 1f));
        var tabLp = new FrameLayout.LayoutParams(-1, -2, GravityFlags.Bottom)
        { LeftMargin = Dp(14), RightMargin = Dp(14), BottomMargin = Dp(12) };
        _root.AddView(_tabBar, tabLp);

        // 顶部浮动工具栏：与底部页签同款胶囊，一条全宽一体结构（2.11 真机反馈）
        BuildTopBar();
        BuildBatchBar();

        SetContentView(_root, new ViewGroup.LayoutParams(-1, -1));
        HighlightTab();
        SwitchMode(Mode);
    }

    View _topBar = null!;

    /// <summary>顶部浮动工具栏（2.11 真机反馈改一体化）：软件印在左、标题随后、五个功能图标在右，
    /// 一条与底部页签同风格的整宽悬浮胶囊，所有页面常驻。</summary>
    void BuildTopBar()
    {
        var bar = MakeBar();
        var topPill = new GradientDrawable();
        topPill.SetCornerRadius(Dp(30));
        topPill.SetColor(ThemeService.C(ThemeService.Key.Primary));
        bar.Background = topPill;
        bar.Elevation = Dp(6);
        bar.SetPadding(Dp(8), Dp(8), Dp(6), Dp(8));
        // 软件印：圆角应用图标徽标，与功能图标同尺寸（30dp）
        var logo = new ImageView(this);
        logo.SetImageResource(Resource.Drawable.ic_bg);
        logo.SetScaleType(ImageView.ScaleType.FitXy);
        var lg = new GradientDrawable();
        lg.SetCornerRadius(Dp(8));
        logo.Background = lg;
        logo.ClipToOutline = true;
        bar.AddView(logo, Dp(30), Dp(30));
        _logo = logo;
        // 标题跟在软件印右侧，可随列表滚动收成小标题
        _title = new TextView(this) { Text = Mode == ViewMode.Archives ? "压缩包" : "文件", TextSize = 20 };
        _title.SetTypeface(Typeface.DefaultBold!, TypefaceStyle.Bold);
        _title.SetTextColor(ThemeService.C(ThemeService.Key.OnPrimary));
        bar.AddView(_title, new LinearLayout.LayoutParams(0, -2, 1f) { LeftMargin = Dp(10) });
        // 五个功能图标在右，尺寸与软件印一致（30dp）；2.27 用户定音：排序从顶栏提出，
        // 常驻「内部存储」标题行右侧，作为所有筛选项的组合条件在列表侧随时可用
        bar.AddView(HeaderIcon(IconKind.Tasks, () => StartActivity(new Intent(this, typeof(TasksActivity)))));
        bar.AddView(HeaderIcon(IconKind.Trash, () => StartActivity(new Intent(this, typeof(TrashActivity)))));
        _eyeIcon = HeaderIcon(IconKind.Eye, ToggleHidden);
        bar.AddView(_eyeIcon);
        bar.AddView(HeaderIcon(IconKind.Theme, SwitchXiang));
        IconView infoBtn = null!;
        infoBtn = HeaderIcon(IconKind.Info, () => ShowAbout(infoBtn));
        bar.AddView(infoBtn);
        _topBar = bar;
        var topLp = new FrameLayout.LayoutParams(-1, -2, GravityFlags.Top)
        { LeftMargin = Dp(14), RightMargin = Dp(14), TopMargin = Dp(10) };
        _root.AddView(_topBar, topLp);
    }

    /// <summary>关于（2.12 改气泡卡：点关于图标弹出，点别处或再点即收，3 秒自动关闭）。</summary>
    void ShowAbout(View anchor)
        => AnchorMenu.ShowBubble(this, anchor,
            "TaoZip 2.27（安卓）\n压缩：zip / tar / tar.gz\n解压：zip / 7z / rar(仅解压) / tar / gz / bz2 / xz\n压缩解压后台跑不中断，通知栏实时进度；顶栏任务清单随时唤起\n\n太极 · 拉链 —— 道法自然，收放自如。");

    View _homeStrip = null!;

    /// <summary>文件页顶部：搜索与分类宫格合并成一整块大圆角卡（2.10），分类筛选态保持显示、点块即切换分类。</summary>
    void BuildHomeStrip()
    {
        var box = new LinearLayout(this) { Orientation = Orientation.Vertical };
        // 搜索 + 分类合并一卡：同一块 24dp 圆角卡，上搜索下分类，与存储卡、列表卡同风格
        var mergeCard = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var cb = new GradientDrawable();
        cb.SetCornerRadius(Dp(24));
        cb.SetColor(ThemeService.C(ThemeService.Key.CardBg));
        mergeCard.Background = cb;
        mergeCard.SetPadding(Dp(10), Dp(10), Dp(10), Dp(10));
        // 胶囊搜索条
        var pill = new LinearLayout(this) { Orientation = Orientation.Horizontal, Clickable = true };
        pill.SetGravity(GravityFlags.CenterVertical);
        var pb = new GradientDrawable();
        pb.SetCornerRadius(Dp(999));
        pb.SetColor(ThemeService.C(ThemeService.Key.PressedBg));
        pill.Background = pb;
        pill.SetPadding(Dp(14), Dp(7), Dp(14), Dp(7));
        var mag = new IconView(this);
        mag.Bind(IconKind.Search, ThemeService.Key.TextHint);
        pill.AddView(mag, Dp(16), Dp(16));
        var hint = new TextView(this) { Text = "搜索文件名、类型、内容", TextSize = 13 };
        hint.SetTextColor(ThemeService.C(ThemeService.Key.TextHint));
        pill.AddView(hint, new LinearLayout.LayoutParams(-2, -2) { LeftMargin = Dp(8) });
        pill.Click += (s, e) => DoSearch();
        mergeCard.AddView(pill, new LinearLayout.LayoutParams(-1, -2)
        { LeftMargin = Dp(4), RightMargin = Dp(4), BottomMargin = Dp(12) });
        // 分类快捷宫格：7 类一行铺满（2.8 起单行，块收紧），点击在文件页内切到该类筛选
        {
            var strip = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            foreach (var cat in Categories)
                strip.AddView(MakeCatTile(cat.Name), new LinearLayout.LayoutParams(0, -2, 1f));
            mergeCard.AddView(strip);
        }
        box.AddView(mergeCard, new LinearLayout.LayoutParams(-1, -2)
        { LeftMargin = Dp(12), RightMargin = Dp(12), BottomMargin = Dp(12) });
        _homeStrip = box;
        _pageFiles.AddView(_homeStrip);
    }

    LinearLayout MakeCatTile(string name)
    {
        var tile = new LinearLayout(this) { Orientation = Orientation.Vertical, Clickable = true };
        tile.SetGravity(GravityFlags.CenterHorizontal);
        var bg = new GradientDrawable(GradientDrawable.Orientation.TopBottom, CatTileGradient(name));
        bg.SetCornerRadius(Dp(15));
        var iconHost = new FrameLayout(this);
        iconHost.Background = bg;
        var iv = new IconView(this);
        iv.BindColor(Categories.First(c => c.Name == name).Kind, global::Android.Graphics.Color.White);
        iconHost.AddView(iv, new FrameLayout.LayoutParams(-1, -1));
        tile.AddView(iconHost, Dp(28), Dp(28));
        var label = new TextView(this) { Text = name, TextSize = 9, Gravity = GravityFlags.Center };
        label.SetTextColor(ThemeService.C(ThemeService.Key.TextSub));
        tile.AddView(label, new LinearLayout.LayoutParams(-2, -2) { TopMargin = Dp(3) });
        tile.Click += (s, e) => EnterCategory(name);
        _catTiles[name] = tile;
        return tile;
    }

    /// <summary>分类块选中态（2.10）：当前分类名亮主色加粗，合并卡在分类页也常驻，点别的块即切换。</summary>
    void UpdateCatTileHighlight(string? active)
    {
        foreach (var (name, tile) in _catTiles)
        {
            if (tile.ChildCount < 2) continue;
            var label = (TextView)tile.GetChildAt(1)!;
            bool on = name == active;
            label.SetTextColor(ThemeService.C(on ? ThemeService.Key.Primary : ThemeService.Key.TextSub));
            label.SetTypeface(Typeface.DefaultBold!, on ? TypefaceStyle.Bold : TypefaceStyle.Normal);
        }
    }

    /// <summary>分类块渐变色：三象各一套，从现有主题气质派生，不引入新色板。</summary>
    internal static (string A, string B) CatTileColors(string name) => ThemeService.Current switch
    {
        ThemeService.Xiang.Tianhui => name switch
        {
            "最近" => ("#5c7d99", "#46647E"),
            "图片" => ("#7fa0bc", "#5a7f9e"),
            "视频" => ("#6b5b8e", "#544870"),
            "音频" => ("#4E7E8A", "#3a6570"),
            _ => ("#547d9c", "#3E6990"),
        },
        ThemeService.Xiang.Moye => name switch
        {
            "最近" => ("#577C9A", "#3e5d77"),
            "图片" => ("#C98A5E", "#a5643c"),
            "视频" => ("#9A7FB8", "#7a6194"),
            "音频" => ("#6FA8A0", "#4d857d"),
            _ => ("#C46B62", "#a34c44"),
        },
        _ => name switch
        {
            "最近" => ("#57687d", "#3D4A5C"),
            "图片" => ("#c87a45", "#A8402F"),
            "视频" => ("#7d6b9e", "#5E4E7E"),
            "音频" => ("#4E8A7E", "#376B60"),
            _ => ("#c4535e", "#A8402F"),
        },
    };

    /// <summary>渐变色对转 Android int[]（ARGB），供 GradientDrawable 构造。</summary>
    internal static int[] CatTileGradient(string name)
    {
        var (a, b) = CatTileColors(name);
        return new[] { ParseArgbHex(a), ParseArgbHex(b) };
    }

    static int ParseArgbHex(string hex) => unchecked((int)(0xFF000000u | (Convert.ToUInt32(hex.TrimStart('#'), 16) & 0xFFFFFFu)));

    IconView HeaderIcon(IconKind kind, Action onClick)
    {
        var v = new IconView(this) { Clickable = true };
        // 2.12 真机反馈：胶囊底是 Primary，图标一律用 OnPrimary，任何象都看得见
        v.Bind(kind, ThemeService.Key.OnPrimary);
        v.Click += (s, e) => onClick();
        var lp = new LinearLayout.LayoutParams(Dp(30), Dp(30)) { LeftMargin = Dp(10) };
        v.LayoutParameters = lp;
        var pad = (View)v;
        pad.SetPadding(Dp(4), Dp(4), Dp(4), Dp(4));
        return v;
    }

    LinearLayout MakeTab(IconKind kind, string label, out IconView icon, out TextView text)
    {
        // 2.11 真机反馈：页签改图标在左、文字在右的横排，图标统一 30dp 与顶栏一致
        var tab = new LinearLayout(this) { Orientation = Orientation.Horizontal, Clickable = true };
        tab.SetGravity(GravityFlags.Center);
        icon = new IconView(this);
        icon.Bind(kind, ThemeService.Key.OnPrimary);
        tab.AddView(icon, Dp(30), Dp(30));
        text = new TextView(this) { Text = label, TextSize = 13 };
        text.SetTextColor(ThemeService.C(ThemeService.Key.OnPrimary));
        tab.AddView(text, new LinearLayout.LayoutParams(-2, -2) { LeftMargin = Dp(7) });
        return tab;
    }

    LinearLayout MakeBar()
    {
        var bar = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        bar.SetGravity(GravityFlags.CenterVertical);
        bar.SetPadding(Dp(6), Dp(6), Dp(6), Dp(6));
        return bar;
    }

    void HighlightTab()
    {
        (_tabFiles, _tabFilesIcon, _tabFilesText).Highlight(Mode == ViewMode.Files);
        (_tabArchives, _tabArchivesIcon, _tabArchivesText).Highlight(Mode == ViewMode.Archives);
    }

    void SwitchMode(ViewMode mode)
    {
        Mode = mode;
        _searchKeyword = null;
        HighlightTab();
        _title.Text = mode switch { ViewMode.Archives => "压缩包", _ => "文件" };
        ShowContainers(filesPage: true);
        if (mode == ViewMode.Files)
        {
            // 2.7：回到普通文件态，退出分类筛选，宫格与存储卡亮出
            _category = null;
            _homeStrip.Visibility = ViewStates.Visible;
            _storageCard.Visibility = ViewStates.Visible;
            _pathRow.Visibility = ViewStates.Visible;
            _chipsScroll.Visibility = ViewStates.Gone;
            UpdateCatTileHighlight(null);
            RefreshStorageCard();
            Refresh();
        }
        else
        {
            _homeStrip.Visibility = ViewStates.Gone;
            _storageCard.Visibility = ViewStates.Gone;
            _pathRow.Visibility = ViewStates.Gone;
            _chipsScroll.Visibility = ViewStates.Gone;
            LoadArchives();
        }
    }

    /// <summary>2.7 分类并入文件页：宫格点击/筛选入口都落到这里，在文件页内切到该类筛选，单页完成。</summary>
    void EnterCategory(string name)
    {
        Mode = ViewMode.Files;
        HighlightTab();
        _title.Text = "文件";
        _searchKeyword = null;
        _category = name;
        LastCategory = name;
        ShowContainers(filesPage: true);
        // 2.10：搜索分类合并卡在分类筛选态保持显示，点块即换类，选中块亮主色
        _pathRow.Visibility = ViewStates.Visible;
        _storageCard.Visibility = ViewStates.Gone;
        UpdateCatTileHighlight(name);
        SetBreadcrumb("分类 · " + name);
        LoadCategoryData(name);
    }

    /// <summary>两个内容容器的显隐统一从这里走：漏隐漏显会让底部标签栏被顶到页面上部。</summary>
    void ShowContainers(bool filesPage)
    {
        _pageFiles.Visibility = filesPage ? ViewStates.Visible : ViewStates.Gone;
    }

    // ================= 存储卡 =================

    void BuildStorageCard()
    {
        _storageCard = new LinearLayout(this) { Orientation = Orientation.Vertical, Clickable = true };
        var bg = new GradientDrawable();
        bg.SetCornerRadius(Dp(24));
        bg.SetColor(ThemeService.C(ThemeService.Key.CardBg));
        _storageCard.Background = bg;
        _storageCard.SetPadding(Dp(14), Dp(8), Dp(14), Dp(9));
        var lp = new LinearLayout.LayoutParams(-1, -2)
        { LeftMargin = Dp(12), RightMargin = Dp(12), BottomMargin = Dp(2) };
        _storageCard.Click += (s, e) =>
        {
            SwitchMode(ViewMode.Files);
            Navigate(RootDir);
        };
        var head = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        head.SetGravity(GravityFlags.CenterVertical);
        var icon = new IconView(this);
        icon.Bind(IconKind.Storage, ThemeService.Key.Primary);
        head.AddView(icon, Dp(22), Dp(22));
        var t = new TextView(this) { Text = "内部存储", TextSize = 14 };
        t.SetTypeface(Typeface.DefaultBold!, TypefaceStyle.Normal);
        t.SetTextColor(ThemeService.C(ThemeService.Key.TextMain));
        head.AddView(t, new LinearLayout.LayoutParams(0, -2, 1f) { LeftMargin = Dp(8) });
        _storageText = new TextView(this) { TextSize = 12 };
        _storageText.SetTextColor(ThemeService.C(ThemeService.Key.TextSub));
        head.AddView(_storageText);
        // 2.27 用户定音：排序常驻「内部存储」标题行右侧，尺寸收到 20dp 与行高相称，
        // 点开功能卡照旧靠右下呼出；排序作为所有筛选项的组合条件全链生效
        var sortInCard = new IconView(this) { Clickable = true };
        sortInCard.Bind(IconKind.Sort, ThemeService.Key.Primary);
        sortInCard.Click += (s, e) => ShowSortMenu(sortInCard);
        head.AddView(sortInCard, new LinearLayout.LayoutParams(Dp(20), Dp(20)) { LeftMargin = Dp(10) });
        _storageCard.AddView(head);
        // 用量条：底槽 + 前景填充（紧凑单行卡，条收窄）
        var slot = new FrameLayout(this);
        slot.SetPadding(0, Dp(7), 0, 0);
        var track = new View(this);
        var tb = new GradientDrawable();
        tb.SetCornerRadius(Dp(2));
        tb.SetColor(ThemeService.C(ThemeService.Key.PressedBg));
        track.Background = tb;
        slot.AddView(track, new ViewGroup.LayoutParams(-1, Dp(4)));
        _storageBarFill = new View(this);
        var fb = new GradientDrawable();
        fb.SetCornerRadius(Dp(2));
        fb.SetColor(ThemeService.C(ThemeService.Key.Primary));
        _storageBarFill.Background = fb;
        slot.AddView(_storageBarFill, new ViewGroup.LayoutParams(0, Dp(4)));
        _storageCard.AddView(slot);
        _pageFiles.AddView(_storageCard, lp);
    }

    void RefreshStorageCard()
    {
        try
        {
            var stat = new StatFs(RootDir);
            long total = stat.TotalBytes, free = stat.AvailableBytes;
            int usedPct = (int)((total - free) * 100 / Math.Max(total, 1));
            _storageText.Text = $"已用 {usedPct}% · 可用 {FmtSize(free)}";
            _storageBarFill.Post(() =>
            {
                var lp = (FrameLayout.LayoutParams)_storageBarFill.LayoutParameters!;
                lp.Width = Math.Max(Dp(5), (int)(slotWidth() * usedPct / 100.0));
                _storageBarFill.LayoutParameters = lp;
            });
            long slotWidth() => (_storageCard.Width > 0 ? _storageCard.Width : Resources!.DisplayMetrics!.WidthPixels)
                - Dp(32);
        }
        catch { _storageText.Text = "存储信息暂时读不到"; }
    }

    // ================= 列表区（文件/分类/压缩包共用 ListView + 空态） =================

    void BuildFileListArea()
    {
        // 面包屑路径条 + 粘贴按钮
        _pathRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        _pathRow.SetGravity(GravityFlags.CenterVertical);
        _pathView = new TextView(this) { TextSize = 12, Ellipsize = TextUtils.TruncateAt.Start };
        _pathView.SetSingleLine(true);
        _pathView.SetTextColor(ThemeService.C(ThemeService.Key.TextHint));
        _pathView.SetPadding(Dp(16), Dp(4), Dp(8), Dp(4));
        _pathView.Click += (s, e) => ShowBreadcrumb();
        _pathRow.AddView(_pathView, new LinearLayout.LayoutParams(0, -2, 1f));
        _btnPaste = new TextView(this) { Text = "粘贴", TextSize = 13 };
        _btnPaste.SetTypeface(Typeface.DefaultBold!, TypefaceStyle.Normal);
        _btnPaste.SetTextColor(ThemeService.C(ThemeService.Key.Accent));
        _btnPaste.SetPadding(Dp(14), Dp(6), Dp(16), Dp(6));
        _btnPaste.Click += (s, e) => PasteHere();
        _btnPaste.Visibility = ViewStates.Gone;
        _pathRow.AddView(_btnPaste);
        _pageFiles.AddView(_pathRow);

        // 分类细分格式筛选条
        _chipsScroll = new HorizontalScrollView(this) { HorizontalScrollBarEnabled = false, Visibility = ViewStates.Gone };
        _chipsBar = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        _chipsBar.SetPadding(Dp(10), Dp(4), Dp(10), Dp(10));   // 2.22：底部间距与左右 12 一档取齐，筛选条和列表不再贴死
        _chipsScroll.AddView(_chipsBar);
        _pageFiles.AddView(_chipsScroll);

        // 列表 + 空态：整块包进大圆角卡片，列表在卡内滚动，底部留出悬浮胶囊的空间
        _adapter = new FileAdapter(this);
        _list = new ListView(this)
        {
            Divider = null,
            DividerHeight = 0,
            Selector = new global::Android.Graphics.Drawables.ColorDrawable(global::Android.Graphics.Color.Transparent),
        };
        _list.Adapter = _adapter;
        _list.SetClipToPadding(false);
        _list.SetPadding(0, Dp(2), 0, Dp(10));
        _emptyBox = new LinearLayout(this) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone };
        _emptyBox.SetGravity(GravityFlags.Center);
        _emptyBox.SetPadding(0, 0, 0, Dp(10));
        var emptyIcon = new IconView(this);
        emptyIcon.Bind(IconKind.Archive, ThemeService.Key.Stroke);
        _emptyBox.AddView(emptyIcon, Dp(56), Dp(56));
        _emptyMain = new TextView(this) { TextSize = 15, Gravity = GravityFlags.Center };
        _emptyMain.SetTextColor(ThemeService.C(ThemeService.Key.TextSub));
        _emptyBox.AddView(_emptyMain, new LinearLayout.LayoutParams(-2, -2) { TopMargin = Dp(12) });
        _emptySub = new TextView(this) { TextSize = 12, Gravity = GravityFlags.Center };
        _emptySub.SetTextColor(ThemeService.C(ThemeService.Key.TextHint));
        _emptyBox.AddView(_emptySub, new LinearLayout.LayoutParams(-2, -2) { TopMargin = Dp(5) });
        var host = new FrameLayout(this);
        host.AddView(_list, new FrameLayout.LayoutParams(-1, -1));
        host.AddView(_emptyBox, new FrameLayout.LayoutParams(-1, -1));
        // 大圆角卡片容器：整块列表融为一体，圆角 24（2.11 多选时右侧让位给批量胶囊）
        _listCard = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var cardBg = new GradientDrawable();
        cardBg.SetCornerRadius(Dp(24));
        cardBg.SetColor(ThemeService.C(ThemeService.Key.CardBg));
        _listCard.Background = cardBg;
        _listCard.ClipToOutline = true;
        _listCard.AddView(host, new LinearLayout.LayoutParams(-1, 0, 1f));
        _pageFiles.AddView(_listCard, new LinearLayout.LayoutParams(-1, 0, 1f)
        { LeftMargin = Dp(12), RightMargin = Dp(12), BottomMargin = Dp(80) });

        // 大标题滚动收起（2.11 改状态标记，避开 px/dp 单位比较坑）：离开顶部收小，回顶恢复
        _list.Scroll += (s, e) =>
        {
            if (e.FirstVisibleItem > 1 && !_titleCompact)
            {
                _titleCompact = true;
                _title.TextSize = 15;
            }
            else if (e.FirstVisibleItem <= 1 && _titleCompact)
            {
                _titleCompact = false;
                _title.TextSize = 20;
            }
        };
    }

    TextView _pathView = null!;

    /// <summary>列表空态：文案按人的处境给指引（虚以待物）。</summary>
    void ShowEmpty(bool show, string main = "这里还空着", string sub = "")
    {
        _emptyBox.Visibility = show ? ViewStates.Visible : ViewStates.Gone;
        _list.Visibility = show ? ViewStates.Gone : ViewStates.Visible;
        _emptyMain.Text = main;
        _emptySub.Text = sub;
        _emptySub.Visibility = sub.Length > 0 ? ViewStates.Visible : ViewStates.Gone;
    }

    class FileAdapter : BaseAdapter<EntryVM>
    {
        readonly MainActivity _a;
        public FileAdapter(MainActivity a) { _a = a; }
        public override EntryVM this[int p] => _a._items[p];
        public override int Count => _a._items.Count;
        public override long GetItemId(int p) => p;

        public override View GetView(int p, View? cv, ViewGroup? parent)
        {
            var vm = _a._items[p];
            if (cv is SwipeRow sr)
            {
                sr.Rebind(vm);
                return sr;
            }
            var row = new LinearLayout(_a) { Orientation = Orientation.Horizontal };
            row.SetGravity(GravityFlags.CenterVertical);
            // 行背景交给整卡容器，行间靠发丝线分隔（一加式大圆角列表卡）；右侧收窄让更多钮尽量靠右（2.11）
            // 2.13 真机反馈：行内左右留白取齐——内容靠左、按钮靠右，两侧到卡缘的间距一致
            row.SetPadding(_a.Dp(12), _a.Dp(9), _a.Dp(6), _a.Dp(9));
            var icon = new ThumbView(_a);
            row.AddView(icon, _a.Dp(30), _a.Dp(30));
            var mid = new LinearLayout(_a) { Orientation = Orientation.Vertical };
            var name = new TextView(_a) { TextSize = 15, Ellipsize = TextUtils.TruncateAt.Middle };
            name.SetSingleLine(true);
            name.SetTextColor(ThemeService.C(ThemeService.Key.TextMain));
            var sub = new TextView(_a) { TextSize = 11 };
            sub.SetTextColor(ThemeService.C(ThemeService.Key.TextHint));
            mid.AddView(name);
            mid.AddView(sub);
            row.AddView(mid, new LinearLayout.LayoutParams(0, -2, 1f) { LeftMargin = _a.Dp(11) });
            var more = new IconView(_a) { Clickable = true };
            more.Bind(IconKind.More, ThemeService.Key.TextHint);
            row.AddView(more, new LinearLayout.LayoutParams(_a.Dp(38), _a.Dp(30)));
            // 2.11 多选指示：默认藏起，进多选态按选中态点亮（行尾第 4 个子元素）
            var check = new IconView(_a);
            check.Bind(IconKind.Check, ThemeService.Key.Stroke);
            check.Visibility = ViewStates.Gone;
            row.AddView(check, new LinearLayout.LayoutParams(_a.Dp(26), _a.Dp(30)));
            var outer = new FrameLayout(_a);
            outer.SetPadding(_a.Dp(10), _a.Dp(3), _a.Dp(10), _a.Dp(3));
            outer.AddView(row, new FrameLayout.LayoutParams(-1, -2));
            return new SwipeRow(_a, row, outer, vm);
        }
    }

    /// <summary>单行左滑手势：拖出「删除」，松手超阈值即移入回收站；展开行点击别处自动收起。</summary>
    internal sealed class SwipeRow : FrameLayout
    {
        readonly MainActivity _a;
        readonly LinearLayout _content;
        readonly ThumbView _thumb;
        readonly TextView _name, _sub;
        readonly IconView _more, _check;
        EntryVM _vm = null!;
        string _path = "";
        float _downX, _downY;
        bool _dragging, _longFired;
        float _dx;
        Java.Lang.Runnable? _longPending;

        public static SwipeRow? Open;

        public SwipeRow(MainActivity a, LinearLayout content, ViewGroup outerRow, EntryVM vm) : base(a)
        {
            _a = a; _content = content;
            _thumb = (ThumbView)content.GetChildAt(0)!;
            var mid = (LinearLayout)content.GetChildAt(1)!;
            _name = (TextView)mid.GetChildAt(0)!;
            _sub = (TextView)mid.GetChildAt(1)!;
            _more = (IconView)content.GetChildAt(2)!;
            _check = (IconView)content.GetChildAt(3)!;
            // 2.19 用户定音：砍掉左滑红色删除按钮，只保留左滑删除手势（滑过阈值直接进删除确认）
            AddView(outerRow, new LayoutParams(-1, -2));
            // 行间发丝线：极淡分隔，融进整卡不抢眼
            var div = ThemeService.C(ThemeService.Key.Divider);
            var hair = new View(a);
            hair.Background = new global::Android.Graphics.Drawables.ColorDrawable(
                new global::Android.Graphics.Color((byte)(div.A * 0.35f), div.R, div.G, div.B));
            outerRow.AddView(hair, new LayoutParams(-1, Math.Max(1, a.Dp(1)), GravityFlags.Bottom));
            Rebind(vm);
            // 2.26 真机反馈根修：「更多」点回调此前在行首建时钉死条目，视图复用后仍拿旧条目——
            // 压缩包分类页因此点出文件夹菜单（缺「打开」缺「解压」）或非压缩包文件菜单。
            // 回调改读 _vm 字段，永远取当次绑定的条目；与行点击/长按的 GetItem() 同口径。
            _more.Click += (s2, e2) => { SwipeRow.Close(null); _a.ShowEntryMenu(_vm, _more); };
        }

        public void Rebind(EntryVM vm)
        {
            _vm = vm;
            _path = vm.FullName;
            ApplySlide(0);
            if (Open == this) Open = null;
            _thumb.Bind(vm);
            _name.Text = vm.Name;
            _sub.Text = vm.Sub;
            // 2.11 多选态：行尾在「更多」与选中勾之间切换，选中亮主色、未选用描边色
            bool sel = _a._selectMode && _a._selected.Contains(vm.FullName);
            _more.Visibility = _a._selectMode ? ViewStates.Invisible : ViewStates.Visible;
            _check.Visibility = _a._selectMode ? ViewStates.Visible : ViewStates.Gone;
            _check.Bind(IconKind.Check, sel ? ThemeService.Key.Primary : ThemeService.Key.Stroke);
        }

        public static void Close(SwipeRow? except)
        {
            if (Open != null && Open != except)
            {
                Open.SnapBack();
                Open = null;
            }
        }

        /// <summary>左滑反馈：行内容跟手滑出，松手后弹回原位（2.19 去按钮，只留手势）。</summary>
        void ApplySlide(float nt) => _content.TranslationX = nt;

        // 行视图自己消费完整触摸序列：横向拖出删除，纵向交给列表，点击/长按回调业务
        public override bool OnTouchEvent(MotionEvent? ev)
        {
            if (ev == null) return base.OnTouchEvent(ev);
            switch (ev.Action)
            {
                case MotionEventActions.Down:
                    _downX = ev.RawX; _downY = ev.RawY;
                    _dragging = false; _longFired = false;
                    _dx = _content.TranslationX;
                    // 2.11 多选态：不再启动长按菜单，抬起即切换选中
                    if (_a._selectMode) return true;
                    CancelLongPress();
                    _longPending ??= new Java.Lang.Runnable(() =>
                    {
                        if (!_dragging && !_longFired)
                        {
                            _longFired = true;
                            Close(null);
                            // 2.11 真机反馈：砍掉长按操作卡片，长按改为进多选
                            _a.EnterSelectMode(GetItem());
                        }
                    });
                    Handler!.PostDelayed(_longPending!, 450);
                    return true;

                case MotionEventActions.Move:
                {
                    // 2.11 多选态：禁用滑删拖动，避免误滑
                    if (_a._selectMode) return true;
                    var dX = ev.RawX - _downX; var dY = ev.RawY - _downY;
                    if (!_dragging && Math.Abs(dX) > _a.Dp(14) && Math.Abs(dX) > Math.Abs(dY) * 1.4f)
                    {
                        _dragging = true;
                        CancelLongPress();
                        Close(this);
                        Parent?.RequestDisallowInterceptTouchEvent(true);
                    }
                    if (_dragging)
                    {
                        var nt = Math.Max(-_a.Dp(96), Math.Min(0f, _dx + dX));
                        ApplySlide(nt);
                        return true;
                    }
                    if (Math.Abs(dY) > _a.Dp(14))
                    {
                        // 纵向交给列表滚动：撤销长按，让 ListView 重新接管
                        CancelLongPress();
                        _longFired = true;
                        (Parent as ViewGroup)?.RequestDisallowInterceptTouchEvent(false);
                        return false;
                    }
                    return true;
                }

                case MotionEventActions.Up:
                {
                    CancelLongPress();
                    // 2.11 多选态：抬起即切换该项选中
                    if (_a._selectMode)
                    {
                        _dragging = false;
                        Parent?.RequestDisallowInterceptTouchEvent(false);
                        _a.ToggleSelect(GetItem());
                        return true;
                    }
                    if (_dragging)
                    {
                        _dragging = false;
                        Parent?.RequestDisallowInterceptTouchEvent(false);
                        SnapBack();
                        // 滑动超过阈值：先问一声再删（2.10 真机反馈恢复确认，防误删）
                        if (-_content.TranslationX >= _a.Dp(48)) DeleteRow();
                        return true;
                    }
                    if (!_longFired)
                    {
                        Close(null);
                        _a.OnItemClick(GetItem());
                    }
                    return true;
                }

                case MotionEventActions.Cancel:
                    CancelLongPress();
                    _dragging = false;
                    Parent?.RequestDisallowInterceptTouchEvent(false);
                    SnapBack();
                    return true;
            }
            return base.OnTouchEvent(ev);
        }

        protected override void CancelLongPress()
        {
            if (_longPending != null) Handler!.RemoveCallbacks(_longPending);
        }

        /// <summary>无论何种中断，行内容一律弹回原位，绝不残留偏移。</summary>
        void SnapBack()
        {
            if (_content.TranslationX != 0)
                _content.Animate()!.TranslationX(0).SetDuration(150)!.Start();
        }

        /// <summary>滑删与行尾删除键同口径（2.10 真机反馈）：先确认再移入回收站，防误删。</summary>
        void DeleteRow() => _a.ConfirmSwipeDelete(_path, _vm.Name);

        EntryVM GetItem() => _vm;
    }

    // ================= 模式刷新 =================

    void Refresh()
    {
        switch (Mode)
        {
            case ViewMode.Files:
                // 分类筛选活着就续上该类（2.7），否则正常按目录刷新
                if (_category != null) { EnterCategory(_category); return; }
                Navigate(CurrentDir); break;
            case ViewMode.Archives: LoadArchives(); break;
        }
    }

    void SetBreadcrumb(string text)
    {
        _pathView.Text = text;
    }

    void Navigate(string dir)
    {
        _searchKeyword = null;
        try
        {
            var di = new FS.DirectoryInfo(dir);
            var infos = di.GetFileSystemInfos();
            if (!ShowHidden) infos = infos.Where(i => !i.Name.StartsWith(".")).ToArray();
            infos = infos.Where(i => !InTrash(i.FullName)).ToArray();
            _items = infos.Select(EntryVM.From).ToList();
            SortItems(_items);
            CurrentDir = dir;
            SetBreadcrumb(BreadcrumbText(dir));
            // 2.6 减挤：存储卡只在内部存储根目录亮出，进子目录把顶部让给内容
            _storageCard.Visibility = dir == RootDir ? ViewStates.Visible : ViewStates.Gone;
        }
        catch
        {
            ToastText("无法读取目录：" + dir);
            return;
        }
        ShowEmpty(_items.Count == 0, "这个文件夹是空的", "把文件放进来，或换个目录看看");
        _adapter.NotifyDataSetChanged();
    }

    void SortItems(List<EntryVM> items)
    {
        IEnumerable<EntryVM> q = items;
        q = Sort switch
        {
            SortMode.Time => Asc ? q.OrderBy(i => i.Time) : q.OrderByDescending(i => i.Time),
            SortMode.Size => Asc ? q.OrderBy(i => i.Size) : q.OrderByDescending(i => i.Size),
            SortMode.Type => Asc ? q.OrderBy(i => i.IsDir ? "" : i.Ext, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                                  : q.OrderByDescending(i => i.IsDir ? "\uffff" : i.Ext, StringComparer.OrdinalIgnoreCase),
            _ => Asc ? q.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase) : q.OrderByDescending(i => i.Name, StringComparer.OrdinalIgnoreCase),
        };
        // 文件夹始终在前
        _items = q.OrderByDescending(i => i.IsDir).ToList();
    }

    string BreadcrumbText(string dir)
    {
        if (dir == RootDir) return "内部存储";
        return dir.StartsWith(RootDir) ? "内部存储 / " + dir[(RootDir.Length + 1)..].Replace("/", " / ") : dir;
    }

    // ================= 分类 =================

    static readonly (string Name, IconKind Kind, ThemeService.Key Color, string[] Exts)[] Categories =
    {
        ("最近", IconKind.Clock, ThemeService.Key.Primary, Array.Empty<string>()),
        ("图片", IconKind.Image, ThemeService.Key.Accent, new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".heic", ".heif", ".tiff", ".svg" }),
        ("视频", IconKind.Video, ThemeService.Key.Accent, new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".ts", ".m4v", ".3gp", ".webm", ".mpg", ".mpeg" }),
        ("音频", IconKind.Audio, ThemeService.Key.Accent, new[] { ".mp3", ".flac", ".wav", ".aac", ".ogg", ".m4a", ".ape", ".wma", ".opus" }),
        ("文档", IconKind.Doc, ThemeService.Key.Primary, new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".md", ".epub", ".csv", ".log", ".rtf", ".wps", ".et" }),
        ("安装包", IconKind.Apk, ThemeService.Key.Primary, new[] { ".apk", ".apks", ".xapk" }),
        ("压缩包", IconKind.Archive, ThemeService.Key.Accent, Array.Empty<string>()), // 用 IsArchiveFile 判定
    };

    /// <summary>分类数据装载：缓存先出，后台重扫。2.7 起在文件页内完成，不再有独立分类页。</summary>
    void LoadCategoryData(string name)
    {
        _catFilter = null;
        BuildChips(name);
        // 命中缓存立即显示，后台再刷新
        if (_catCache.TryGetValue(name, out var cached))
        {
            // 缓存先滤掉已删条目，避免分类页出现删除后仍残留的文件
            _catAll = cached.Where(i => FS.Directory.Exists(i.FullName) || FS.File.Exists(i.FullName)).ToList();
            _catCache[name] = _catAll;
            ApplyCatFilter();
        }
        else
        {
            _items.Clear();
            ShowEmpty(true, "正在找这一类的文件…", "第一次会稍慢，下次就快了");
            _adapter.NotifyDataSetChanged();
        }

        int gen = ++_catScanGen;
        System.Threading.Tasks.Task.Run(() =>
        {
            var cat = Categories.First(c => c.Name == name);
            List<FS.FileInfo> found = new();
            try { ScanForCategory(RootDir, cat, found, 0); } catch { }
            found = name == "最近"
                ? found.OrderByDescending(f => f.LastWriteTime).Take(100).ToList()
                : found.OrderByDescending(f => f.LastWriteTime).Take(500).ToList();
            RunOnUiThread(() =>
            {
                // 2.16：旧扫描回调一律作废——期间发生的删除不能被扫描结果覆盖回来
                if (gen != _catScanGen || Mode != ViewMode.Files || _category != name) return;
                _catAll = found.Select(EntryVM.From).ToList();
                _catCache[name] = _catAll;
                ApplyCatFilter();
            });
        });
    }

    /// <summary>分类页细分格式筛选条：按当前分类的常用扩展名生成可点选的筛选片。</summary>
    void BuildChips(string name)
    {
        _chipsBar.RemoveAllViews();
        var cat = Categories.FirstOrDefault(c => c.Name == name);
        var exts = (cat.Exts ?? Array.Empty<string>()).Distinct().ToList();
        if (exts.Count == 0) { _chipsScroll.Visibility = ViewStates.Gone; return; }
        _chipsScroll.Visibility = ViewStates.Visible;
        AddChip("全部", null);
        foreach (var ext in exts) AddChip(ext.TrimStart('.').ToUpperInvariant(), ext);
    }

    void AddChip(string label, string? ext)
    {
        var chip = new TextView(this) { Text = label, TextSize = 12, Gravity = GravityFlags.Center };
        chip.SetPadding(Dp(14), Dp(6), Dp(14), Dp(6));
        bool active = _catFilter == ext;
        chip.SetTextColor(active ? ThemeService.C(ThemeService.Key.OnPrimary) : ThemeService.C(ThemeService.Key.TextSub));
        var bg = new GradientDrawable();
        bg.SetCornerRadius(Dp(14));
        bg.SetColor(active ? ThemeService.C(ThemeService.Key.Primary) : ThemeService.C(ThemeService.Key.CardBg));
        chip.Background = bg;
        chip.Click += (s, e) =>
        {
            _catFilter = ext;
            ApplyCatFilter();
            BuildChips(_category ?? "");
        };
        _chipsBar.AddView(chip, new LinearLayout.LayoutParams(-2, -2) { RightMargin = Dp(8) });
    }

    /// <summary>应用细分筛选：全部=当前分类完整列表，否则按扩展名过滤。2.27：排序作为组合条件在此链路同样生效。</summary>
    void ApplyCatFilter()
    {
        _items = _catFilter == null
            ? _catAll.ToList()
            : _catAll.Where(i => i.Ext.Equals(_catFilter, StringComparison.OrdinalIgnoreCase)).ToList();
        SortItems(_items);
        if (_items.Count == 0)
            ShowEmpty(true, "没有找到这一类的文件",
                _catFilter == null ? "手机里暂时没有「" + (_category ?? "") + "」类文件" : "没有 " + _catFilter.TrimStart('.').ToUpperInvariant() + " 格式的文件");
        else ShowEmpty(false);
        _adapter.NotifyDataSetChanged();
    }

    static void ScanForCategory(string dir, (string Name, IconKind Kind, ThemeService.Key Color, string[] Exts) cat, List<FS.FileInfo> acc, int depth)
    {
        if (acc.Count > 800 || depth > 6) return;
        FS.DirectoryInfo di;
        try { di = new FS.DirectoryInfo(dir); } catch { return; }
        FS.FileSystemInfo[] entries;
        try { entries = di.GetFileSystemInfos(); } catch { return; }
        foreach (var e in entries)
        {
            if (e.Name.StartsWith(".")) continue;
            if (InTrash(e.FullName)) continue;
            if (e is FS.DirectoryInfo d)
            {
                // 跳过系统噪音目录
                if (d.Name is "Android" or "WhatsApp" && cat.Name != "最近") continue;
                ScanForCategory(d.FullName, cat, acc, depth + 1);
            }
            else if (e is FS.FileInfo f)
            {
                bool hit = cat.Name == "最近"
                    || (cat.Name == "压缩包" && ArchiveEngine.IsArchiveFile(f.FullName))
                    || cat.Exts.Contains(f.Extension, StringComparer.OrdinalIgnoreCase);
                if (hit) acc.Add(f);
            }
        }
    }

    // ================= 压缩包页 =================

    /// <summary>聚合最近的压缩包与分卷首卷，直达浏览与解压。</summary>
    void LoadArchives()
    {
        ShowContainers(filesPage: true);
        _pathRow.Visibility = ViewStates.Gone;
        _chipsScroll.Visibility = ViewStates.Gone;
        _storageCard.Visibility = ViewStates.Gone;

        if (ArchivesCache.Count > 0)
        {
            // 缓存先滤掉已删条目，绝不把已经删掉的东西又列出来
            ArchivesCache = ArchivesCache.Where(i => FS.File.Exists(i.FullName)).ToList();
            _items = ArchivesCache;
            SortItems(_items);   // 2.27：排序作为组合条件，压缩包页同样受用户排序管
            ShowEmpty(false);
            _adapter.NotifyDataSetChanged();
        }
        else
        {
            _items.Clear();
            ShowEmpty(true, "正在找手机里的压缩包…", "第一次会稍慢，之后秒开");
            _adapter.NotifyDataSetChanged();
        }

        int gen = ++_archScanGen;
        System.Threading.Tasks.Task.Run(() =>
        {
            var found = new List<FS.FileInfo>();
            try { ScanArchives(RootDir, found, 0); } catch { }
            var list = found.OrderByDescending(f => f.LastWriteTime).Take(300).Select(EntryVM.From).ToList();
            RunOnUiThread(() =>
            {
                // 2.16：旧扫描回调一律作废——期间发生的删除不能被扫描结果覆盖回来
                // 2.21 同型排查：缓存赋值也要作废在代号检查之后，删除后起飞的旧扫描不得把已删数据写回 ArchivesCache
                if (gen != _archScanGen || Mode != ViewMode.Archives) return;
                ArchivesCache = list;
                _items = list;
                SortItems(_items);   // 2.27：排序作为组合条件，扫描回填同样受用户排序管
                ShowEmpty(list.Count == 0, "还没找到压缩包", "去「文件」里选中文件点「压缩」，或把压缩包放进手机存储");
                _adapter.NotifyDataSetChanged();
            });
        });
    }

    internal static List<EntryVM> ArchivesCache = new();

    static void ScanArchives(string dir, List<FS.FileInfo> acc, int depth)
    {
        if (acc.Count > 600 || depth > 6) return;
        FS.FileSystemInfo[] entries;
        try { entries = new FS.DirectoryInfo(dir).GetFileSystemInfos(); } catch { return; }
        foreach (var e in entries)
        {
            if (e.Name.StartsWith(".")) continue;
            if (InTrash(e.FullName)) continue;
            if (e is FS.DirectoryInfo d)
            {
                if (d.Name is "Android" or "WhatsApp") continue;
                ScanArchives(d.FullName, acc, depth + 1);
            }
            else if (e is FS.FileInfo f)
            {
                var ext = f.Extension.ToLowerInvariant();
                if (ArchiveEngine.IsArchiveFile(f.FullName) || ext == ".001") acc.Add(f);
            }
        }
    }

    // ================= 点击与返回 =================

    void OnItemClick(EntryVM item)
    {
        SwipeRow.Close(null);
        if (item.IsDir && Mode == ViewMode.Archives)
        {
            // 压缩包页只该有条目文件，兜底转浏览
            Mode = ViewMode.Files; HighlightTab();
            Navigate(item.FullName);
            return;
        }
        if (item.IsDir)
        {
            // 2.7：目录浏览属普通文件态，退出分类筛选，宫格与存储卡回到页面
            Mode = ViewMode.Files; HighlightTab();
            _title.Text = "文件";
            _pageFiles.Visibility = ViewStates.Visible;
            _category = null;
            _homeStrip.Visibility = ViewStates.Visible;
            _chipsScroll.Visibility = ViewStates.Gone;
            Navigate(item.FullName);
            return;
        }
        if (TrashHelper.IsImage(item.FullName))
        {
            // 2.20 用户定音：预览闪退两轮未断根，砍应用内预览——点图只把当前一张交给系统图库，
            // 左右翻页、缩放手势全交给系统预览器，TaoZip 不再自己做预览页
            OpenWithSystem(item.FullName);
            return;
        }
        if (item.Ext.Equals(".001", StringComparison.OrdinalIgnoreCase)) { AskMerge(item.FullName); return; }
        if (ArchiveEngine.IsArchiveFile(item.FullName))
        {
            StartActivity(new Intent(this, typeof(ArchiveActivity)).PutExtra("path", item.FullName));
            return;
        }
        OpenWithSystem(item.FullName);
    }

    /// <summary>系统返回键：多选退出 → 清粘贴 → 退搜索 → 退分类筛选 → 压缩包回文件 → 逐级返回 → 退出。</summary>
    public override void OnBackPressed()
    {
        // 2.11 多选态先退多选
        if (_selectMode) { ExitSelectMode(); return; }
        if (_clipPaths.Count > 0) { _clipPaths = new List<string>(); ToastText("已清除待粘贴内容"); UpdatePasteButton(); return; }
        if (_searchKeyword != null) { _searchKeyword = null; Refresh(); return; }
        // 2.7：分类筛选在文件页内，返回键先退筛选再退目录
        if (_category != null) { SwitchMode(ViewMode.Files); return; }
        if (Mode == ViewMode.Archives) { SwitchMode(ViewMode.Files); return; }
        if (CurrentDir != RootDir)
        {
            var parent = Path.GetDirectoryName(CurrentDir.TrimEnd('/'));
            Navigate(string.IsNullOrEmpty(parent) ? RootDir : parent);
            return;
        }
        Finish();
    }

    /// <summary>面包屑：弹出从内部存储到当前目录的每一级，点击直达。</summary>
    void ShowBreadcrumb()
    {
        // 2.7：分类态点面包屑即退出筛选，回到普通文件页
        if (_category != null) { SwitchMode(ViewMode.Files); return; }
        if (Mode != ViewMode.Files || _searchKeyword != null) return;
        var levels = new List<string> { RootDir };
        var rel = CurrentDir.StartsWith(RootDir) ? CurrentDir[(RootDir.Length + 1)..] : "";
        if (!string.IsNullOrEmpty(rel))
        {
            var parts = rel.Split('/');
            for (int i = 0; i < parts.Length; i++)
                levels.Add(RootDir + "/" + string.Join('/', parts[..(i + 1)]));
        }
        var names = levels.Select(l => l == RootDir ? "内部存储" : Path.GetFileName(l)).ToArray();
        TaoDialog.Items(this, "跳转到", names, i => Navigate(levels[i]));
    }

    // ================= 打开文件 =================

    /// <summary>通过内容提供器以只读方式把文件交给系统里能处理它的应用；视频优先唤起自家播放器 TaoPlay。</summary>
    /// <summary>通过内容提供器以只读方式把文件交给系统里能处理它的应用；视频优先唤起自家播放器 TaoPlay。</summary>
    void OpenWithSystem(string path) => OpenExternally(this, path);

    /// <summary>静态共享实现（2.21）：主界面与回收站预览同一口径——同一套 MIME 映射、同一个 FileProvider、视频同样优先定向道播放，绝不各写一份。</summary>
    internal static void OpenExternally(Activity a, string path)
    {
        try
        {
            var uri = FileProvider.GetUriForFile(a, Authority, new Java.IO.File(path));
            var ext = Path.GetExtension(path).ToLowerInvariant().TrimStart('.');
            var mime = global::Android.Webkit.MimeTypeMap.Singleton!.GetMimeTypeFromExtension(ext)
                ?? (ext switch
                {
                    "mkv" => "video/x-matroska",
                    "avi" => "video/x-msvideo",
                    "wmv" => "video/x-ms-wmv",
                    "flv" => "video/x-flv",
                    "mov" => "video/mp4",
                    "m4v" => "video/mp4",
                    "ts" => "video/mp2t",
                    "apk" => "application/vnd.android.package-archive",
                    "7z" => "application/x-7z-compressed",
                    "rar" => "application/x-rar-compressed",
                    _ => "application/octet-stream",
                });
            var intent = new Intent(Intent.ActionView)
                .SetDataAndType(uri, mime)
                .AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
            if (mime.StartsWith("video/"))
            {
                try
                {
                    a.StartActivity(new Intent(intent).SetPackage(TaoPlayPackage));
                    return;
                }
                catch
                {
                    // 2.21：道播放已开放接收外部播放；走到这里多半是它没响应或被系统拦了——说清楚再走系统，绝不静默
                    bool installed;
                    try { installed = a.PackageManager!.GetPackageInfo(TaoPlayPackage, 0) != null; }
                    catch { installed = false; }
                    Toast.MakeText(a, installed
                        ? "道播放没响应，先用系统播放器打开"
                        : "未安装道播放，先用系统播放器打开", ToastLength.Short)!.Show();
                }
            }
            a.StartActivity(intent);
        }
        catch
        {
            Toast.MakeText(a, "没有可以打开此文件的应用", ToastLength.Short)!.Show();
        }
    }

    void AskMerge(string firstVolume)
    {
        TaoDialog.Confirm(this, "分卷合并",
            "这是分卷压缩包的第一个卷，要合并还原成完整文件吗？",
            "合并", () =>
            {
                try
                {
                    var dest = VolumeHelper.MergeVolumes(firstVolume, keepParts: false);
                    ToastText("已合并为 " + Path.GetFileName(dest));
                    Refresh();
                }
                catch (Exception ex) { ShowErr("合并失败", ex); }
            });
    }

    // ================= 条目功能卡 =================

    /// <summary>条目功能卡：所有功能收进唤起元素下方的悬浮卡片，替代长按底部操作条与旧列表弹窗。</summary>
    internal void ShowEntryMenu(EntryVM vm, View anchor)
    {
        FS.FileSystemInfo fi = FS.Directory.Exists(vm.FullName)
            ? new FS.DirectoryInfo(vm.FullName)
            : new FS.FileInfo(vm.FullName);
        bool isDir = fi is FS.DirectoryInfo;
        bool isArchive = !isDir && ArchiveEngine.IsArchiveFile(fi.FullName);
        var acts = new List<AnchorMenu.Item>();
        if (!isDir)
        {
            acts.Add(new AnchorMenu.Item { Text = "打开", OnClick = () => OpenWithSystem(fi.FullName) });
            // 2.26 用户定音：解压入口按后缀判定——支持表里的压缩包格式一律显示「解压」。
            // 此前入口缺失的真因是行复用拿旧条目（见 SwipeRow 构造注释），判定链本身没问题
            if (isArchive) acts.Add(new AnchorMenu.Item { Text = "解压", OnClick = () => ExtractArchive(fi.FullName) });
        }
        acts.Add(new AnchorMenu.Item { Text = "压缩", OnClick = () => CompressOne(fi) });
        acts.Add(new AnchorMenu.Item { Text = "复制", OnClick = () => ClipOne(fi, false) });
        acts.Add(new AnchorMenu.Item { Text = "移动", OnClick = () => ClipOne(fi, true) });
        acts.Add(new AnchorMenu.Item { Text = "重命名", OnClick = () => RenameItem(fi) });
        // 2.22 用户定音：右侧更多菜单砍掉「删除」——删除只留左滑手势与多选批量两条路，入口少才不误触
        acts.Add(new AnchorMenu.Item { Text = "详情", OnClick = () =>
        {
            var size = fi is FS.FileInfo f ? FmtSize(f.Length) : "文件夹";
            TaoDialog.Info(this, "详情",
                $"名称：{fi.Name}\n路径：{fi.FullName}\n大小：{size}\n修改时间：{fi.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
        } });
        AnchorMenu.Show(this, anchor, acts.ToArray());
    }

    // ================= 回收站删除 =================

    internal const string TaoPlayPackage = "com.allplayer.phone";

    /// <summary>滑删防错（2.10 真机反馈）：先问一声再落回收站，删了仍可放回。</summary>
    internal void ConfirmSwipeDelete(string path, string name)
        => TaoDialog.Confirm(this, "删除确认",
            $"「{name}」将移入回收站，随时可以在回收站放回原处。确定删除吗？",
            "删除", () => DeleteToTrash(path));

    /// <summary>
    /// 删除一律移入「TaoZip 回收站」（真实搬移，可再找回）：
    /// 2.6 根因实锤——系统媒体回收站只打 IS_TRASHED 标记不搬文件，重扫列表必然残留「已删数据」；
    /// 统一走真实文件搬移后，列表刷新即对齐磁盘真相。更多菜单 → 回收站 可放回原处或彻底删除。
    /// </summary>
    internal void DeleteToTrash(string path)
    {
        bool exists = FS.Directory.Exists(path) || FS.File.Exists(path);
        if (!exists)
        {
            // 上一次删除多半已经成功，只是列表没跟上——按已删除处理，绝不拿"找不到"吓人
            ToastText("已删除，列表已刷新");
            Refresh();
            return;
        }
        string? moveErr = null;
        try { TrashHelper.MoveToTrash(path); }
        catch (Exception ex) { moveErr = ex.Message; }
        bool stillThere = FS.Directory.Exists(path) || FS.File.Exists(path);
        if (!stillThere)
        {
            // 2.10：删除成功当场从列表与各缓存除名，绝不让已删条目留在页面上
            PurgeFromLists(path);
            // 2.16：媒体文件删后通知媒体库重扫，系统相册不再留"鬼影"条目
            if (TrashHelper.IsMedia(path))
                global::Android.Media.MediaScannerConnection.ScanFile(this, new[] { path }, null, null);
            ToastText("已移入回收站（右上回收站里可放回原处）");
        }
        else if (moveErr != null)
            ToastText("删除失败：" + moveErr);
        else
            ToastText("没有删掉，请再试一次");
        // 不管成败一律刷新：列表永远对齐磁盘真相，绝不残留已删条目
        Refresh();
        // 2.21 兜底清场：无论刷新后列表数据来自哪个缓存，只要已删路径还在可见列表里就当场除名，残留零容忍
        if (_items.RemoveAll(i => i.FullName == path) > 0)
            _adapter.NotifyDataSetChanged();
    }

    /// <summary>把已删条目从当前列表、分类集合、压缩包与分类缓存中当场除名并刷新。</summary>
    void PurgeFromLists(string path)
    {
        _items.RemoveAll(i => i.FullName == path);
        _catAll.RemoveAll(i => i.FullName == path);
        ArchivesCache.RemoveAll(i => i.FullName == path);
        foreach (var k in _catCache.Keys.ToList())
            _catCache[k] = _catCache[k].Where(i => i.FullName != path).ToList();
        _adapter.NotifyDataSetChanged();
    }

    void ClipOne(FS.FileSystemInfo item, bool move)
    {
        _clipPaths = new List<string> { item.FullName };
        _clipMove = move;
        UpdatePasteButton();
        ToastText(_clipMove ? "已选中（移动）：去目标目录点「粘贴」" : "已选中（复制）：去目标目录点「粘贴」");
    }

    void RenameItem(FS.FileSystemInfo item)
    {
        var input = TaoDialog.MakeInput(this, "新名称");
        input.Text = item.Name;
        var box = new LinearLayout(this) { Orientation = Orientation.Vertical };
        box.AddView(input);
        TaoDialog.Custom(this, "重命名", box, "确定", _ =>
        {
            try
            {
                var target = Path.Combine(CurrentDir, input.Text?.Trim() ?? "");
                if (item is FS.DirectoryInfo) FS.Directory.Move(item.FullName, target);
                else FS.File.Move(item.FullName, target);
                Refresh();
            }
            catch (Exception ex) { ShowErr("重命名失败", ex); }
        });
    }

    // ================= 搜索 =================

    void DoSearch()
    {
        var input = TaoDialog.MakeInput(this, "全盘搜索：文件名 / 路径 / 类型…（空格分词）");
        var box = new LinearLayout(this) { Orientation = Orientation.Vertical };
        box.AddView(input);
        TaoDialog.Custom(this, "搜索", box, "搜索", _ =>
        {
            var kw = input.Text?.Trim();
            if (string.IsNullOrEmpty(kw)) return;
            Mode = ViewMode.Files;
            HighlightTab();
            _title.Text = "文件";
            _pageFiles.Visibility = ViewStates.Visible;
            _category = null;
            _searchKeyword = kw;
            SetBreadcrumb($"搜索：{kw}（点返回键结束）");
            _items.Clear();
            ShowEmpty(true, "正在全盘找「" + kw + "」…", "多个关键词用空格隔开，比如：壁纸 png");
            _adapter.NotifyDataSetChanged();
            System.Threading.Tasks.Task.Run(() =>
            {
                var hits = new List<(FS.FileSystemInfo Info, int Score)>();
                try { SearchAll(RootDir, kw, hits, 0); } catch { }
                RunOnUiThread(() =>
                {
                    if (_searchKeyword != kw) return;
                    _items = hits
                        .OrderByDescending(h => h.Score)
                        .ThenByDescending(h => h.Info.LastWriteTime)
                        .Take(200)
                        .Select(h => EntryVM.From(h.Info))
                        .ToList();
                    // 2.27：相关性决定候选，用户排序作为组合条件决定最终呈现
                    SortItems(_items);
                    if (_items.Count == 0)
                        ShowEmpty(true, "没找到匹配的文件", "换个词试试；路径、类型、大小、日期也都能搜");
                    else ShowEmpty(false);
                    _adapter.NotifyDataSetChanged();
                });
            });
        });
    }

    /// <summary>全局分词搜索：目录和文件都进结果，按打分排序（文件名命中权重最高）。</summary>
    static void SearchAll(string dir, string kw, List<(FS.FileSystemInfo Info, int Score)> acc, int depth)
    {
        if (acc.Count >= 600 || depth > 8) return;
        FS.FileSystemInfo[] entries;
        try { entries = new FS.DirectoryInfo(dir).GetFileSystemInfos(); } catch { return; }
        foreach (var e in entries)
        {
            if (e.Name.StartsWith(".")) continue;
            if (InTrash(e.FullName)) continue;
            int score = MatchScore(e, kw);
            if (score > 0) acc.Add((e, score));
            if (e is FS.DirectoryInfo d) SearchAll(d.FullName, kw, acc, depth + 1);
            if (acc.Count >= 600) return;
        }
    }

    /// <summary>关键词按空格分词，每个词都要命中才计入；文件名 &gt; 类型 &gt; 路径 &gt; 大小/日期。</summary>
    internal static int MatchScore(FS.FileSystemInfo e, string kw)
    {
        var tokens = SearchTokens(kw);
        if (tokens.Count == 0) return 0;
        bool isDir = e is FS.DirectoryInfo;
        string name = e.Name;
        string ext = isDir ? "" : FS.Path.GetExtension(name).ToLowerInvariant().TrimStart('.');
        string path = e.FullName;
        string size = !isDir && e is FS.FileInfo f ? FmtSize(f.Length) : "";
        string time = e.LastWriteTime.ToString("yyyy-MM-dd");
        int total = 0;
        foreach (var t in tokens)
        {
            if (name.Contains(t, StringComparison.OrdinalIgnoreCase))
                total += 100 + (name.StartsWith(t, StringComparison.OrdinalIgnoreCase) ? 20 : 0);
            else if (ext.Length > 0 && t == ext)
                total += 60;
            else if (path.Contains(t, StringComparison.OrdinalIgnoreCase))
                total += 20;
            else if ((size.Length > 0 && size.Contains(t, StringComparison.OrdinalIgnoreCase))
                  || time.Contains(t, StringComparison.OrdinalIgnoreCase))
                total += 10;
            else
                return 0; // 任一分词没命中就不算匹配
        }
        return total;
    }

    /// <summary>空格分词，去空去重（大小写不敏感）。</summary>
    internal static List<string> SearchTokens(string kw)
        => kw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    // ================= 排序菜单与换象 =================

    void SwitchXiang()
    {
        var next = ThemeService.Next;
        ThemeService.SetXiang(this, next);
        Recreate(); // 整树重画，杜绝任何控件残留旧色
    }

    /// <summary>顶栏眼睛一键切显示隐藏文件：图标点亮即开，列表即时重扫（图标随胶囊反白，任何象都可见）。</summary>
    void ToggleHidden()
    {
        ShowHidden = !ShowHidden;
        _eyeIcon.Bind(IconKind.Eye, ThemeService.Key.OnPrimary);
        ToastText(ShowHidden ? "已显示隐藏文件" : "已隐藏隐藏文件");
        Refresh();
    }

    void ShowSortMenu(View? anchor)
    {
        var acts = new List<AnchorMenu.Item>
        {
            new() { Text = Sort == SortMode.Name ? "按名称（当前）" : "按名称", OnClick = () => { Sort = SortMode.Name; Refresh(); } },
            new() { Text = Sort == SortMode.Time ? "按时间（当前）" : "按时间", OnClick = () => { Sort = SortMode.Time; Refresh(); } },
            new() { Text = Sort == SortMode.Size ? "按大小（当前）" : "按大小", OnClick = () => { Sort = SortMode.Size; Refresh(); } },
            new() { Text = Sort == SortMode.Type ? "按类型（当前）" : "按类型", OnClick = () => { Sort = SortMode.Type; Refresh(); } },
            new() { Text = Asc ? "切换为降序（当前升序）" : "切换为升序（当前降序）", OnClick = () => { Asc = !Asc; Refresh(); } },
        };
        // 2.27 用户定音：排序入口移到「内部存储」标题行右侧后，功能卡照旧靠右下呼出（同条目功能卡对位）
        AnchorMenu.Show(this, anchor ?? _logo, acts.ToArray());
    }

    // ================= 多选与批量（2.11 真机反馈：长按即多选，右侧竖排胶囊批量操作） =================

    /// <summary>长按进入多选：第一项随手选中，列表右侧亮出竖排批量胶囊。</summary>
    internal void EnterSelectMode(EntryVM first)
    {
        _selectMode = true;
        _selected.Clear();
        _selected.Add(first.FullName);
        SwipeRow.Close(null);
        SetSelectUi(true);
        _adapter.NotifyDataSetChanged();
        ToastText("已进多选：点条目勾选，右侧浮层批量操作");
    }

    void ExitSelectMode()
    {
        _selectMode = false;
        _selected.Clear();
        SetSelectUi(false);
        _adapter.NotifyDataSetChanged();
    }

    internal void ToggleSelect(EntryVM item)
    {
        if (!_selectMode) return;
        if (!_selected.Remove(item.FullName)) _selected.Add(item.FullName);
        if (_selected.Count == 0) { ExitSelectMode(); return; }
        _adapter.NotifyDataSetChanged();
    }

    void SelectAllOrNone()
    {
        if (_selected.Count == _items.Count && _items.Count > 0) _selected.Clear();
        else foreach (var i in _items) _selected.Add(i.FullName);
        _adapter.NotifyDataSetChanged();
    }

    /// <summary>多选态界面切换（2.13 真机反馈）：批量浮层亮出即可，列表不再让位缩宽，浮层直接悬浮在列表上方。</summary>
    void SetSelectUi(bool on)
    {
        _batchBar.Visibility = on ? ViewStates.Visible : ViewStates.Gone;
    }

    /// <summary>批量操作竖排浮层（2.13 真机反馈改半透明底+文字按钮）：全选/复制/移动/删除/压缩/退出，
    /// 直接悬浮在列表上方不挤占列表宽度；右缘收在行尾按钮列左侧，绝不浮在更多/勾选按钮上方。
    /// 2.14：浮层属锦上添花件，构建失败绝不拖累整页启动——兜底一个空壳并落日志可查。</summary>
    void BuildBatchBar()
    {
        _batchBar = new LinearLayout(this) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone };
        try
        {
            var bg = new GradientDrawable();
            bg.SetCornerRadius(Dp(18));
            var fbg = ThemeService.C(ThemeService.Key.FloatBg);
            bg.SetColor(new global::Android.Graphics.Color((byte)(fbg.A * 0.78f), fbg.R, fbg.G, fbg.B));
            bg.SetStroke(Dp(1), ThemeService.C(ThemeService.Key.Stroke));
            _batchBar.Background = bg;
            _batchBar.Elevation = Dp(6);
            _batchBar.SetPadding(Dp(12), Dp(8), Dp(12), Dp(8));
            _batchBar.AddView(BatchText("全选", ThemeService.Key.TextMain, SelectAllOrNone));
            _batchBar.AddView(BatchText("复制", ThemeService.Key.TextMain, () => ClipMany(false)));
            _batchBar.AddView(BatchText("移动", ThemeService.Key.TextMain, () => ClipMany(true)));
            _batchBar.AddView(BatchText("删除", ThemeService.Key.Danger, BatchDelete));
            _batchBar.AddView(BatchText("压缩", ThemeService.Key.TextMain, BatchCompress));
            // 2.23 真机反馈：批量补上「解压」——选中的压缩包批量解压，目标文件夹挑一次全用
            _batchBar.AddView(BatchText("解压", ThemeService.Key.TextMain, BatchExtract));
            _batchBar.AddView(BatchText("退出", ThemeService.Key.TextMain, ExitSelectMode));
            // 右缘停位：列表卡右距 12 + 行外侧距 10 + 行尾按钮列宽 38 ≈ 62，让出整列按钮
            var lp = new FrameLayout.LayoutParams(-2, -2, GravityFlags.Right | GravityFlags.CenterVertical)
            { RightMargin = Dp(62) };
            _root.AddView(_batchBar, lp);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("TaoZip", "批量浮层构建失败：" + ex);
        }
    }

    TextView BatchText(string label, ThemeService.Key color, Action onClick)
    {
        var t = new TextView(this) { Text = label, TextSize = 13 };
        t.SetTextColor(ThemeService.C(color));
        t.SetTypeface(Typeface.DefaultBold!, TypefaceStyle.Normal);
        t.Clickable = true;
        t.SetPadding(Dp(6), Dp(8), Dp(6), Dp(8));
        t.Click += (s, e) => onClick();
        // 2.15 修复：只创建并返回，不在此处挂父——调用方 BuildBatchBar 已统一 AddView，
        // 双重挂父会抛 already has a parent（这正是 2.13/2.14 浮层不显示的根因）
        return t;
    }

    /// <summary>批量复制/移动：选中项整批进待粘贴剪贴板，去目标目录点「粘贴」。</summary>
    void ClipMany(bool move)
    {
        if (_selected.Count == 0) { ToastText("还没有选中任何项"); return; }
        _clipPaths = _selected.ToList();
        _clipMove = move;
        UpdatePasteButton();
        int n = _clipPaths.Count;
        ExitSelectMode();
        ToastText($"已选中 {n} 项（{(move ? "移动" : "复制")}）：去目标目录点「粘贴」");
    }

    /// <summary>批量删除：先确认，再逐项移入回收站，删多少报多少。</summary>
    void BatchDelete()
    {
        if (_selected.Count == 0) { ToastText("还没有选中任何项"); return; }
        var paths = _selected.ToList();
        TaoDialog.Confirm(this, "批量删除",
            $"选中的 {paths.Count} 项将移入回收站，随时可以放回原处。确定删除吗？",
            "删除", () =>
            {
                int ok = 0;
                foreach (var p in paths)
                {
                    try { TrashHelper.MoveToTrash(p); PurgeFromLists(p); ok++; } catch { }
                }
                ExitSelectMode();
                Refresh();
                ToastText($"已移入回收站 {ok}/{paths.Count} 项");
            });
    }

    /// <summary>批量压缩：选中项打成一个包，输出目录口径与单项压缩一致。</summary>
    void BatchCompress()
    {
        if (_selected.Count == 0) { ToastText("还没有选中任何项"); return; }
        var sources = _selected.ToList();
        var first = sources[0];
        var outDir = Mode == ViewMode.Files && _searchKeyword == null
            ? CurrentDir
            : (FS.Path.GetDirectoryName(first) ?? CurrentDir);
        var box = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var nameEdit = TaoDialog.MakeInput(this, "输出文件名");
        nameEdit.Text = Path.GetFileNameWithoutExtension(first);
        box.AddView(FieldLabel($"输出到 {outDir}，{sources.Count} 项打成一个包，文件名："));
        box.AddView(nameEdit);
        var fmt = new Spinner(this);
        fmt.Adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleSpinnerItem,
            new[] { "zip（支持加密/分卷）", "tar", "tar.gz" });
        box.AddView(FieldLabel("格式："));
        box.AddView(fmt);
        TaoDialog.Custom(this, $"压缩 {sources.Count} 项", box, "开始压缩", _ =>
        {
            int fmtIdx = fmt.SelectedItemPosition;
            var format = fmtIdx == 1 ? CompressFormat.Tar : fmtIdx == 2 ? CompressFormat.Tgz : CompressFormat.Zip;
            var ext = fmtIdx == 0 ? ".zip" : fmtIdx == 1 ? ".tar" : ".tar.gz";
            var destPath = Path.Combine(outDir, SanitizeName(nameEdit.Text ?? "archive") + ext);
            var req = new CompressRequest
            {
                SourcePaths = sources,
                DestPath = destPath,
                Format = format,
                Level = 6,
            };
            RunTask(TaskKind.Compress, "压缩 " + Path.GetFileName(destPath),
                (id, p, c) => ArchiveWriter.Compress(req, id, p, c),
                () => "已生成 " + Path.GetFileName(destPath));
            ExitSelectMode();
        });
    }

    // ================= 多选与操作 =================

    /// <summary>单条目压缩：功能卡入口，输出到条目所在目录或当前浏览目录。</summary>
    void CompressOne(FS.FileSystemInfo item)
    {
        var source = item.FullName;
        var outDir = Mode == ViewMode.Files && _searchKeyword == null
            ? CurrentDir
            : (FS.Path.GetDirectoryName(source) ?? CurrentDir);
        var box = new LinearLayout(this) { Orientation = Orientation.Vertical };

        var nameEdit = TaoDialog.MakeInput(this, "输出文件名");
        nameEdit.Text = Path.GetFileNameWithoutExtension(source);
        box.AddView(FieldLabel("输出到 " + outDir + "，文件名："));
        box.AddView(nameEdit);

        var fmt = new Spinner(this);
        fmt.Adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleSpinnerItem,
            new[] { "zip（支持加密/分卷）", "tar", "tar.gz" });
        box.AddView(FieldLabel("格式："));
        box.AddView(fmt);

        var level = new SeekBar(this) { Max = 8, Progress = 5 };
        box.AddView(FieldLabel("压缩级别 1-9（zip/tar）"));
        box.AddView(level);

        var pass = TaoDialog.MakeInput(this, "密码（仅 zip，可空）", password: true);
        box.AddView(pass);

        var vol = TaoDialog.MakeInput(this, "分卷大小 MB（0=不分卷）");
        vol.InputType = InputTypes.ClassNumber;
        vol.Text = "0";
        box.AddView(vol);

        box.AddView(FieldLabel("说明：7z/rar 因授权限制仅支持解压，不支持创建。"));

        TaoDialog.Custom(this, "压缩 1 项", box, "开始压缩", _ =>
        {
            int fmtIdx = fmt.SelectedItemPosition;
            var format = fmtIdx == 1 ? CompressFormat.Tar : fmtIdx == 2 ? CompressFormat.Tgz : CompressFormat.Zip;
            var ext = fmtIdx == 0 ? ".zip" : fmtIdx == 1 ? ".tar" : ".tar.gz";
            var destPath = Path.Combine(outDir, SanitizeName(nameEdit.Text ?? "archive") + ext);
            var req = new CompressRequest
            {
                SourcePaths = new List<string> { source },
                DestPath = destPath,
                Format = format,
                Level = level.Progress + 1,
                Password = string.IsNullOrWhiteSpace(pass.Text) ? null : pass.Text,
                VolumeSizeMb = int.TryParse(vol.Text, out var mb) && mb > 0 ? mb : 0,
            };
            RunTask(TaskKind.Compress, "压缩 " + Path.GetFileName(destPath),
                (id, p, c) => ArchiveWriter.Compress(req, id, p, c),
                () => "已生成 " + Path.GetFileName(destPath));
        });
    }

    /// <summary>直接解压（2.23 真机反馈）：目标文件夹可挑，默认选中当前目录（功能卡「解压」入口）。</summary>
    void ExtractArchive(string path)
    {
        var box = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var input = TaoDialog.MakeInput(this, "解压到哪个文件夹");
        input.Text = CurrentDir;
        box.AddView(input);
        box.AddView(FolderPickLink(input, () => CurrentDir));
        TaoDialog.Custom(this, "解压 " + FS.Path.GetFileName(path), box, "开始解压", _ =>
        {
            var dest = input.Text?.Trim();
            if (string.IsNullOrEmpty(dest)) { ToastText("先填解压到哪个文件夹"); return; }
            RunTask(TaskKind.Extract, "解压 " + Path.GetFileName(path),
                (id, p, c) => ArchiveEngine.Extract(new ExtractRequest
                {
                    ArchivePath = path,
                    DestDir = dest,
                    FileNameEncoding = "utf-8",
                }, id, p, c),
                () => "已解压到 " + dest);
        });
    }

    /// <summary>解压弹窗里的「选个文件夹」入口（2.23 真机反馈）：点了弹目录选择器，选完回填输入框。</summary>
    TextView FolderPickLink(EditText input, Func<string> startDir)
    {
        var link = new TextView(this) { Text = "📁 选个文件夹", TextSize = 13 };
        link.SetTypeface(Typeface.DefaultBold!, TypefaceStyle.Normal);
        link.SetTextColor(ThemeService.C(ThemeService.Key.Accent));
        link.SetPadding(Dp(2), Dp(10), Dp(2), Dp(2));
        link.Click += (s, e) => PickFolder(input.Text?.Trim() is { Length: > 0 } cur && FS.Directory.Exists(cur) ? cur : startDir(),
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
            pathLabel.Text = dir == RootDir ? "内部存储" : dir;
            var dirs = new List<string>();
            if (dir != RootDir) dirs.Add("⬆ 上级目录");
            try
            {
                foreach (var d in FS.Directory.EnumerateDirectories(dir).OrderBy(p => p))
                    dirs.Add("📁 " + FS.Path.GetFileName(d));
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
                var parent = FS.Path.GetDirectoryName(cur.TrimEnd('/'));
                Load(string.IsNullOrEmpty(parent) ? RootDir : parent);
            }
            else Load(FS.Path.Combine(cur, pick[2..]));
        };
        TaoDialog.Custom(this, "选择目标文件夹", box, "用这里", _ => onPick(cur), cancelText: null);
        Load(start);
    }

    /// <summary>批量解压（2.23 真机反馈）：选中的压缩包挑一次目标文件夹，逐个解到其下同名子文件夹。</summary>
    void BatchExtract()
    {
        if (_selected.Count == 0) { ToastText("还没有选中任何项"); return; }
        var archives = _selected.Where(p => !FS.Directory.Exists(p) && ArchiveEngine.IsArchiveFile(p)).ToList();
        if (archives.Count == 0) { ToastText("选中的项里没有压缩包"); return; }
        PickFolder(CurrentDir, dest =>
        {
            foreach (var a in archives)
            {
                var sub = FS.Path.Combine(dest, FS.Path.GetFileNameWithoutExtension(a));
                RunTask(TaskKind.Extract, "解压 " + FS.Path.GetFileName(a),
                    (id, p, c) => ArchiveEngine.Extract(new ExtractRequest
                    {
                        ArchivePath = a,
                        DestDir = sub,
                        FileNameEncoding = "utf-8",
                    }, id, p, c),
                    () => "已解压到 " + sub);
            }
            ToastText($"{archives.Count} 个压缩包开始解压");
        });
    }

    /// <summary>删除失败必须报错给出原因，绝不静默无效。</summary>
    static void TryDelete(FS.FileSystemInfo fsi)
    {
        if (fsi is FS.DirectoryInfo d)
        {
            foreach (var c in d.EnumerateFileSystemInfos()) TryDelete(c);
            d.Delete();
            if (d.Exists) throw new Exception("文件夹没有删干净，可能里面有被占用的内容");
        }
        else
        {
            FS.File.Delete(fsi.FullName); // 删不掉会直接抛错，不再静默无效
            if (FS.File.Exists(fsi.FullName)) throw new Exception("文件没有删掉，可能被其他应用占用");
        }
    }

    void UpdatePasteButton()
    {
        _btnPaste.Visibility = _clipPaths.Count > 0 ? ViewStates.Visible : ViewStates.Gone;
    }

    // ================= 粘贴 =================

    /// <summary>把功能卡里复制/移动的条目粘贴到当前目录（浏览模式）。</summary>
    void PasteHere()
    {
        if (_clipPaths.Count == 0) { ToastText("没有待粘贴的内容"); return; }
        if (Mode != ViewMode.Files || _searchKeyword != null || _category != null) { ToastText("请先到「文件」进入目标目录再粘贴"); return; }
        var dest = CurrentDir;
        var sources = _clipPaths;
        _clipPaths = new List<string>();
        UpdatePasteButton();
        foreach (var src in sources)
        {
            var target = Path.Combine(dest, Path.GetFileName(src));
            try
            {
                if (_clipMove) MoveEntry(src, target);
                else CopyEntry(src, target);
            }
            catch (Exception ex) { ShowErr("操作失败：" + Path.GetFileName(src), ex); }
        }
        Refresh();
        ToastText(_clipMove ? "已移动" : "已复制");
    }

    static void CopyEntry(string src, string dest)
    {
        if (FS.Directory.Exists(src))
        {
            FS.Directory.CreateDirectory(dest);
            foreach (var c in FS.Directory.GetFileSystemEntries(src))
                CopyEntry(c, Path.Combine(dest, Path.GetFileName(c)));
        }
        else FS.File.Copy(src, dest, overwrite: true);
    }

    static void MoveEntry(string src, string dest)
    {
        try { if (FS.Directory.Exists(src)) { FS.Directory.Move(src, dest); return; } } catch { /* 跨分区回落 */ }
        try { FS.File.Move(src, dest); return; } catch { /* 跨分区回落 */ }
        CopyEntry(src, dest);
        TryDelete(new FS.FileInfo(src));
    }

    // ================= 压缩 =================

    TextView FieldLabel(string text)
    {
        var t = new TextView(this) { Text = text, TextSize = 12 };
        t.SetTextColor(ThemeService.C(ThemeService.Key.TextSub));
        t.SetPadding(Dp(4), Dp(10), Dp(4), Dp(4));
        return t;
    }

    static string SanitizeName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "archive" : name.Trim();
    }

    // ================= 后台任务 =================

    void RunTask(TaskKind kind, string title, Action<string, IProgress<ArchiveProgress>, CancellationToken> work, Func<string> doneMsg)
    {
        // 2.14：任务交给前台服务保活，切后台/锁屏不中断，通知栏实时进度，随时点进任务中心
        TaskForegroundService.Start(this);
        var progress = new ProgressDialog(this);
        progress.SetTitle(title);
        progress.SetMessage("准备中…");
        progress.SetCancelable(false);
        progress.SetButton((int)DialogButtonType.Negative, "取消", (s, e) =>
        {
            var running = Service.Records.LastOrDefault(r => r.Title == title && r.State is TaskState.Queued or TaskState.Running);
            if (running != null) Service.Cancel(running.Id);
        });
        progress.Show();

        var record = Service.Enqueue(kind, title, work);
        void OnUpdated(ArchiveTaskRecord r)
        {
            if (!ReferenceEquals(r, record)) return;
            if (r.State == TaskState.Running)
                RunOnUiThread(() => progress.SetMessage($"进行中 {r.ProgressPercent}%"));
            else
            {
                Service.TaskUpdated -= OnUpdated;
                RunOnUiThread(() =>
                {
                    progress.Dismiss();
                    if (r.State == TaskState.Done)
                    {
                        ToastText(doneMsg());
                        Refresh();
                    }
                    else if (r.State == TaskState.Failed)
                        ShowErr("任务失败：" + r.Title, new Exception(r.Message));
                    else if (r.State == TaskState.Cancelled)
                        ToastText("已取消");
                });
            }
        }
        Service.TaskUpdated += OnUpdated;
    }

    // ================= 杂项 =================

    internal void ToastText(string msg)
        => RunOnUiThread(() => Toast.MakeText(this, new Java.Lang.String(msg), ToastLength.Short)!.Show());

    internal void ShowErr(string title, Exception ex)
    {
        RunOnUiThread(() => TaoDialog.Info(this, title, ex.Message));
    }

    internal static string FmtSize(long b) => b switch
    {
        >= 1L << 30 => $"{b / (float)(1L << 30):F1} GB",
        >= 1L << 20 => $"{b / (float)(1L << 20):F1} MB",
        >= 1L << 10 => $"{b / 1024f:F1} KB",
        _ => $"{b} B",
    };

    internal int Dp(int v) => (int)(v * Resources!.DisplayMetrics!.Density);
}

/// <summary>悬浮胶囊页签高亮：选中亮出白胶囊（2.9 起固定白底，墨夜 OnPrimary 为黑不再用作胶囊底），配深字深图标；未选中用主色上的反白。</summary>
internal static class TabHighlight
{
    // 胶囊上的深字/深图标固定深灰：三象白底通吃，不随象取反
    static readonly global::Android.Graphics.Color PillInk = global::Android.Graphics.Color.Rgb(0x21, 0x21, 0x21);

    public static void Highlight(this (LinearLayout Tab, IconView Icon, TextView Text) tab, bool active)
    {
        int Dp(float v) => (int)(v * tab.Tab.Resources!.DisplayMetrics!.Density);
        if (active)
        {
            var pill = new global::Android.Graphics.Drawables.GradientDrawable();
            pill.SetCornerRadius(Dp(999));
            pill.SetColor(global::Android.Graphics.Color.White);
            tab.Tab.Background = pill;
            tab.Icon.BindColor(tab.Icon.Kind, PillInk);
            tab.Text.SetTextColor(PillInk);
            tab.Text.SetTypeface(Typeface.DefaultBold!, TypefaceStyle.Bold);
        }
        else
        {
            tab.Tab.Background = null;
            tab.Icon.Bind(tab.Icon.Kind, ThemeService.Key.OnPrimary);
            // 2.12 真机反馈：未选中文字不再打透明度（墨夜象 OnPrimary 黑字会被糊掉），直接全色反白
            tab.Text.SetTextColor(ThemeService.C(ThemeService.Key.OnPrimary));
            tab.Text.SetTypeface(Typeface.DefaultBold!, TypefaceStyle.Normal);
        }
    }
}

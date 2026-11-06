using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Media;
using Android.Util;
using Android.Views;
using Android.Widget;
using Java.Lang;

namespace TaoZip.UI.Android;

/// <summary>
/// 图文预览：图片/视频条目直接显示缩略图，其余显示水墨图标。
/// 缩略图带内存缓存、后台解码限并发，滑动列表不卡顿、不串行。
/// </summary>
internal sealed class ThumbView : FrameLayout
{
    // 缓存按条目数计：256 张缩略图约几十 MB 内，够列表页翻页复用
    static readonly LruCache Cache = new(256);
    static readonly System.Threading.SemaphoreSlim Gate = new(2, 2);

    readonly IconView _icon;
    readonly ImageView _img;
    string _path = "";

    public ThumbView(Context ctx) : base(ctx)
    {
        _icon = new IconView(ctx);
        AddView(_icon, new LayoutParams(-1, -1));
        _img = new ImageView(ctx);
        _img.SetScaleType(ImageView.ScaleType.CenterCrop);
        var bg = new GradientDrawable();
        float d = ctx.Resources!.DisplayMetrics!.Density;
        bg.SetCornerRadius(10 * d);
        bg.SetColor(ThemeService.C(ThemeService.Key.PressedBg));
        _img.Background = bg;
        _img.ClipToOutline = true;
        AddView(_img, new LayoutParams(-1, -1));
        _img.Visibility = ViewStates.Gone;
    }

    /// <summary>重绑列表行：媒体条目先显图标占位，缩略图就绪后切换；非媒体条目只显图标。</summary>
    public void Bind(EntryVM vm)
    {
        _path = vm.FullName;
        ShowIcon(vm);
        if (vm.IsDir || !(TrashHelper.IsImage(vm.FullName) || TrashHelper.IsVideo(vm.FullName))) return;

        if (Cache.Get(vm.FullName) is Bitmap ready) { Apply(ready, vm.FullName); return; }
        var path = vm.FullName;
        System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await Gate.WaitAsync();
                try
                {
                    if (Cache.Get(path) is Bitmap cached) { Post(() => Apply(cached, path)); return; }
                    var bmp = TrashHelper.IsVideo(path) ? DecodeVideo(path) : DecodeImage(path);
                    if (bmp != null)
                    {
                        Cache.Put(path, bmp);
                        Post(() => Apply(bmp, path));
                    }
                }
                finally { Gate.Release(); }
            }
            catch { } // 解码失败保持图标占位，绝不崩
        });
    }

    void ShowIcon(EntryVM vm)
    {
        var (kind, colorKey) = vm.Icon;
        _icon.Bind(kind, colorKey);
        _icon.Visibility = ViewStates.Visible;
        _img.Visibility = ViewStates.Gone;
    }

    void Apply(Bitmap bmp, string path)
    {
        if (_path != path) return; // 行已复用到别的条目，绝不串图
        _img.SetImageBitmap(bmp);
        _img.Visibility = ViewStates.Visible;
        _icon.Visibility = ViewStates.Gone;
    }

    static Bitmap? DecodeImage(string path)
    {
        try
        {
            var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
            BitmapFactory.DecodeFile(path, bounds);
            int sample = 1;
            while (bounds.OutWidth / sample > 256 || bounds.OutHeight / sample > 256) sample *= 2;
            return BitmapFactory.DecodeFile(path, new BitmapFactory.Options { InSampleSize = sample });
        }
        catch { return null; }
    }

    static Bitmap? DecodeVideo(string path)
    {
        try
        {
            using var mr = new MediaMetadataRetriever();
            mr.SetDataSource(path);
            using var frame = mr.GetFrameAtTime(1_000_000); // 1 秒处抽帧
            if (frame == null) return null;
            int scale = System.Math.Max(1, System.Math.Max(frame.Width, frame.Height) / 256);
            return scale > 1
                ? Bitmap.CreateScaledBitmap(frame, frame.Width / scale, frame.Height / scale, true)
                : frame;
        }
        catch { return null; }
    }
}

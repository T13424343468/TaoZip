using Android.Content;
using Android.Graphics;
using Android.Views;

namespace TaoZip.UI.Android;

/// <summary>图标种类：全部单色矢量，随主题取色，保证三象下风格统一。</summary>
internal enum IconKind
{
    Folder, Archive, Image, Video, Audio, Doc, Apk, File, Volume,
    Back, Search, More, Paste, Sort, Theme, Clock, Storage, Check, Close, Eye, Trash, Info,
    Copy, Move, Tasks,
}

/// <summary>
/// 自绘单色图标视图（24x24 视口，2dp 圆帽描边，水墨笔意）。
/// 颜色一律取自 ThemeService 色板键，禁硬编码色。
/// </summary>
internal sealed class IconView : View
{
    readonly Paint _p;
    public IconKind Kind { get; private set; } = IconKind.File;
    ThemeService.Key _colorKey;

    public IconView(Context ctx) : base(ctx)
    {
        _p = new Paint(PaintFlags.AntiAlias)
        {
            StrokeCap = Paint.Cap.Round,
            StrokeJoin = Paint.Join.Round,
        };
    }

    /// <summary>设置图标与颜色键（重绑列表行时复用）。</summary>
    public void Bind(IconKind kind, ThemeService.Key colorKey, float strokeWidthDp = 2f)
    {
        Kind = kind;
        _colorKey = colorKey;
        _p.StrokeWidth = Dp(strokeWidthDp);
        _p.Color = ThemeService.C(colorKey);
        Invalidate();
    }

    /// <summary>直接指定颜色（不走色板）：仅供沉浸黑底等特殊场景，常规界面一律走 Bind 色板键。</summary>
    public void BindColor(IconKind kind, global::Android.Graphics.Color color, float strokeWidthDp = 2f)
    {
        Kind = kind;
        _p.StrokeWidth = Dp(strokeWidthDp);
        _p.Color = color;
        Invalidate();
    }

    float Dp(float v) => v * Resources!.DisplayMetrics!.Density;

    protected override void OnDraw(Canvas? canvas)
    {
        if (canvas == null) return;
        base.OnDraw(canvas);
        // 24x24 视口等比缩放到视图实际大小
        float s = Math.Min(Width, Height) / 24f;
        canvas.Save();
        canvas.Scale(s, s);
        _p.StrokeWidth = 2f;
        Draw(Kind, canvas, _p);
        canvas.Restore();
    }

    void Draw(IconKind k, Canvas c, Paint p)
    {
        switch (k)
        {
            case IconKind.Folder:
                var fp = new global::Android.Graphics.Path();
                fp.MoveTo(3, 7); fp.LineTo(3, 19);
                fp.LineTo(21, 19); fp.LineTo(21, 9);
                fp.LineTo(12, 9); fp.LineTo(9.5f, 6);
                fp.LineTo(3, 6); fp.Close();
                p.SetStyle(Paint.Style.Stroke);
                c.DrawPath(fp, p);
                break;

            case IconKind.Archive:
                var box = new global::Android.Graphics.Path();
                // 绑定属性 Direction.Cw 标注可空（8604），取局部量判空后再传
                var cw = global::Android.Graphics.Path.Direction.Cw;
                if (cw != null) box.AddRoundRect(new RectF(4, 4, 20, 20), 3, 3, cw);
                p.SetStyle(Paint.Style.Stroke);
                c.DrawPath(box, p);
                // 拉链：中缝三段短竖线 + 拉头方点
                c.DrawLine(12, 6.5f, 12, 8.5f, p);
                c.DrawLine(12, 11, 12, 13, p);
                c.DrawLine(12, 15.5f, 12, 17, p);
                p.SetStyle(Paint.Style.Fill);
                c.DrawRect(10.6f, 9.2f, 13.4f, 10.3f, p);
                c.DrawRect(10.6f, 13.7f, 13.4f, 14.8f, p);
                break;

            case IconKind.Image:
                p.SetStyle(Paint.Style.Stroke);
                c.DrawRoundRect(new RectF(3.5f, 4.5f, 20.5f, 19.5f), 3, 3, p);
                c.DrawCircle(9, 10, 1.8f, p);
                var m = new global::Android.Graphics.Path();
                m.MoveTo(6, 17); m.LineTo(11, 12); m.LineTo(15, 16); m.LineTo(18, 13);
                c.DrawPath(m, p);
                break;

            case IconKind.Video:
                p.SetStyle(Paint.Style.Stroke);
                c.DrawRoundRect(new RectF(3.5f, 5.5f, 16.5f, 18.5f), 3, 3, p);
                var tri = new global::Android.Graphics.Path();
                tri.MoveTo(17, 10); tri.LineTo(21.5f, 7.5f); tri.LineTo(21.5f, 16.5f); tri.LineTo(17, 14);
                c.DrawPath(tri, p);
                break;

            case IconKind.Audio:
                p.SetStyle(Paint.Style.Stroke);
                c.DrawLine(10, 5, 10, 16, p);
                c.DrawLine(16, 3.5f, 16, 14.5f, p);
                var beam = new global::Android.Graphics.Path();
                beam.MoveTo(10, 5); beam.LineTo(16, 3.5f);
                c.DrawPath(beam, p);
                c.DrawCircle(7.6f, 16.5f, 2.4f, p);
                c.DrawCircle(13.6f, 15, 2.4f, p);
                break;

            case IconKind.Doc:
                p.SetStyle(Paint.Style.Stroke);
                var dp2 = new global::Android.Graphics.Path();
                dp2.MoveTo(6, 3.5f); dp2.LineTo(15, 3.5f); dp2.LineTo(19, 7.5f);
                dp2.LineTo(19, 20.5f); dp2.LineTo(6, 20.5f); dp2.Close();
                c.DrawPath(dp2, p);
                c.DrawLine(15, 3.5f, 15, 7.5f, p);
                c.DrawLine(9, 12, 16, 12, p);
                c.DrawLine(9, 15.5f, 16, 15.5f, p);
                break;

            case IconKind.Apk:
                p.SetStyle(Paint.Style.Stroke);
                var body = new global::Android.Graphics.Path();
                body.ArcTo(new RectF(5, 7, 19, 21), 180, 180);
                body.LineTo(5, 7);
                c.DrawPath(body, p);
                c.DrawLine(3, 13.5f, 21, 13.5f, p);
                c.DrawLine(6.5f, 4.5f, 8.5f, 7.5f, p);
                c.DrawLine(17.5f, 4.5f, 15.5f, 7.5f, p);
                break;

            case IconKind.File:
                p.SetStyle(Paint.Style.Stroke);
                var fp2 = new global::Android.Graphics.Path();
                fp2.MoveTo(6, 3.5f); fp2.LineTo(15, 3.5f); fp2.LineTo(19, 7.5f);
                fp2.LineTo(19, 20.5f); fp2.LineTo(6, 20.5f); fp2.Close();
                c.DrawPath(fp2, p);
                c.DrawLine(15, 3.5f, 15, 7.5f, p);
                break;

            case IconKind.Volume:
                p.SetStyle(Paint.Style.Stroke);
                c.DrawOval(new RectF(4.5f, 4, 19.5f, 9), p);
                c.DrawLine(4.5f, 6.5f, 4.5f, 18, p);
                c.DrawLine(19.5f, 6.5f, 19.5f, 18, p);
                c.DrawArc(new RectF(4.5f, 15.5f, 19.5f, 20.5f), 180, 180, false, p);
                break;

            case IconKind.Back:
                p.SetStyle(Paint.Style.Stroke);
                var bk = new global::Android.Graphics.Path();
                bk.MoveTo(14.5f, 5); bk.LineTo(7.5f, 12); bk.LineTo(14.5f, 19);
                c.DrawPath(bk, p);
                break;

            case IconKind.Search:
                p.SetStyle(Paint.Style.Stroke);
                c.DrawCircle(10.5f, 10.5f, 5.5f, p);
                c.DrawLine(15, 15, 20, 20, p);
                break;

            case IconKind.More:
                p.SetStyle(Paint.Style.Fill);
                c.DrawCircle(12, 5, 1.7f, p);
                c.DrawCircle(12, 12, 1.7f, p);
                c.DrawCircle(12, 19, 1.7f, p);
                break;

            case IconKind.Paste:
                p.SetStyle(Paint.Style.Stroke);
                c.DrawRoundRect(new RectF(5, 5, 19, 20.5f), 3, 3, p);
                c.DrawRoundRect(new RectF(9, 2.5f, 15, 7), 2, 2, p);
                break;

            case IconKind.Sort:
                p.SetStyle(Paint.Style.Stroke);
                c.DrawLine(4.5f, 7, 19.5f, 7, p);
                c.DrawLine(4.5f, 12, 15.5f, 12, p);
                c.DrawLine(4.5f, 17, 11.5f, 17, p);
                break;

            case IconKind.Theme:
                // 阴阳意象：外圆 + 半圆填
                p.SetStyle(Paint.Style.Stroke);
                c.DrawCircle(12, 12, 8.5f, p);
                var half = new global::Android.Graphics.Path();
                half.AddArc(new RectF(3.5f, 3.5f, 20.5f, 20.5f), -90, 180);
                half.Close();
                p.SetStyle(Paint.Style.Fill);
                c.DrawPath(half, p);
                break;

            case IconKind.Clock:
                p.SetStyle(Paint.Style.Stroke);
                c.DrawCircle(12, 12, 8.5f, p);
                c.DrawLine(12, 7, 12, 12.5f, p);
                c.DrawLine(12, 12.5f, 16, 15, p);
                break;

            case IconKind.Storage:
                p.SetStyle(Paint.Style.Stroke);
                c.DrawOval(new RectF(4.5f, 4, 19.5f, 9), p);
                c.DrawLine(4.5f, 6.5f, 4.5f, 17, p);
                c.DrawLine(19.5f, 6.5f, 19.5f, 17, p);
                c.DrawArc(new RectF(4.5f, 14.5f, 19.5f, 19.5f), 0, 180, false, p);
                c.DrawArc(new RectF(4.5f, 9, 19.5f, 14), 0, 180, false, p);
                break;

            case IconKind.Check:
                p.SetStyle(Paint.Style.Stroke);
                var ck = new global::Android.Graphics.Path();
                ck.MoveTo(5, 12.5f); ck.LineTo(10, 17.5f); ck.LineTo(19, 6.5f);
                c.DrawPath(ck, p);
                break;

            case IconKind.Close:
                p.SetStyle(Paint.Style.Stroke);
                c.DrawLine(6, 6, 18, 18, p);
                c.DrawLine(18, 6, 6, 18, p);
                break;

            case IconKind.Eye:
                // 眼：上下两弧合目 + 圆瞳，显示隐藏文件的状态灯
                p.SetStyle(Paint.Style.Stroke);
                var lid = new global::Android.Graphics.Path();
                lid.MoveTo(3.5f, 12);
                lid.QuadTo(12, 4.5f, 20.5f, 12);
                lid.QuadTo(12, 19.5f, 3.5f, 12);
                lid.Close();
                c.DrawPath(lid, p);
                c.DrawCircle(12, 12, 3, p);
                break;

            case IconKind.Trash:
                // 回收站：盖沿 + 提手 + 桶身 + 内棱
                p.SetStyle(Paint.Style.Stroke);
                var bin = new global::Android.Graphics.Path();
                bin.MoveTo(6.5f, 7); bin.LineTo(7.5f, 20); bin.LineTo(16.5f, 20); bin.LineTo(17.5f, 7);
                c.DrawPath(bin, p);
                c.DrawLine(4.5f, 6.5f, 19.5f, 6.5f, p);
                c.DrawLine(10, 4, 14, 4, p);
                c.DrawLine(10.2f, 10, 10.2f, 17, p);
                c.DrawLine(13.8f, 10, 13.8f, 17, p);
                break;

            case IconKind.Info:
                // 关于：圆 + 问询点与竖杠
                p.SetStyle(Paint.Style.Stroke);
                c.DrawCircle(12, 12, 8.5f, p);
                p.SetStyle(Paint.Style.Fill);
                c.DrawCircle(12, 7.8f, 1.3f, p);
                c.DrawRect(11, 10.8f, 13, 16.8f, p);
                break;

            case IconKind.Copy:
                // 复制：前后两叠圆角矩形
                p.SetStyle(Paint.Style.Stroke);
                c.DrawRoundRect(new RectF(8.5f, 3.5f, 20.5f, 15.5f), 3, 3, p);
                c.DrawRoundRect(new RectF(3.5f, 8.5f, 15.5f, 20.5f), 3, 3, p);
                break;

            case IconKind.Move:
                // 移动：竖线（原位）+ 右向箭头（挪往别处）
                p.SetStyle(Paint.Style.Stroke);
                c.DrawLine(6, 4, 6, 20, p);
                var mv = new global::Android.Graphics.Path();
                mv.MoveTo(11, 12); mv.LineTo(20, 12);
                c.DrawPath(mv, p);
                var mvh = new global::Android.Graphics.Path();
                mvh.MoveTo(16.5f, 8.5f); mvh.LineTo(20, 12); mvh.LineTo(16.5f, 15.5f);
                c.DrawPath(mvh, p);
                break;

            case IconKind.Tasks:
                // 任务清单：左列三个圆点，右侧三行任务条
                p.SetStyle(Paint.Style.Stroke);
                c.DrawLine(10, 7, 20, 7, p);
                c.DrawLine(10, 12, 20, 12, p);
                c.DrawLine(10, 17, 20, 17, p);
                p.SetStyle(Paint.Style.Fill);
                c.DrawCircle(4.5f, 7, 1.5f, p);
                c.DrawCircle(4.5f, 12, 1.5f, p);
                c.DrawCircle(4.5f, 17, 1.5f, p);
                break;
        }
    }
}

/// <summary>文件类型 → 图标与色键的映射（唯一事实来源，列表与宫格共用）。</summary>
internal static class IconMap
{
    public static (IconKind Kind, ThemeService.Key Color) For(string path, bool isDir)
    {
        if (isDir) return (IconKind.Folder, ThemeService.Key.Primary);
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (TaoZip.BLL.ArchiveEngine.IsArchiveFile(path)) return (IconKind.Archive, ThemeService.Key.Accent);
        if (ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".heic" or ".heif" or ".svg") return (IconKind.Image, ThemeService.Key.TextSub);
        if (ext is ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".flv" or ".ts" or ".m4v" or ".webm" or ".3gp") return (IconKind.Video, ThemeService.Key.TextSub);
        if (ext is ".mp3" or ".flac" or ".wav" or ".aac" or ".ogg" or ".m4a" or ".ape" or ".opus" or ".wma") return (IconKind.Audio, ThemeService.Key.TextSub);
        if (ext is ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".txt" or ".md" or ".epub" or ".csv" or ".log" or ".rtf") return (IconKind.Doc, ThemeService.Key.TextSub);
        if (ext == ".apk" || ext == ".apks" || ext == ".xapk") return (IconKind.Apk, ThemeService.Key.TextSub);
        if (ext is ".zip.001" or ".001" or ".002") return (IconKind.Volume, ThemeService.Key.Accent);
        return (IconKind.File, ThemeService.Key.TextSub);
    }
}

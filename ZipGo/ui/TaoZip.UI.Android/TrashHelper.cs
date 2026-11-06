using Android.Content;
using FS = System.IO;

namespace TaoZip.UI.Android;

/// <summary>
/// 回收站：删除不再是「不可恢复」。
/// 媒体文件（图片/视频/音频）在安卓 11+ 走系统回收站（MediaStore，进手机相册回收站，由 MainActivity 发起）；
/// 其余文件与文件夹移入「TaoZip 回收站」文件夹，原路径记在同名元数据里，可放回原处、也可彻底删除。
/// </summary>
internal static class TrashHelper
{
    public static readonly string TrashDir = "/storage/emulated/0/TaoZip回收站";
    // 元数据后缀用 ASCII（命名铁律：健壮性敏感处零中文）；2.16 及更早写的是中文后缀，读删时两种都要认
    const string MetaExt = ".trashmeta";
    const string LegacyMetaExt = ".回收站来源";

    public static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".heic", ".heif" };
    public static readonly string[] VideoExts = { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".ts", ".m4v", ".webm", ".3gp", ".mpg", ".mpeg" };
    public static readonly string[] AudioExts = { ".mp3", ".flac", ".wav", ".aac", ".ogg", ".m4a", ".ape", ".opus", ".wma" };

    static bool Has(string[] exts, string path) => exts.Contains(FS.Path.GetExtension(path).ToLowerInvariant());
    public static bool IsImage(string p) => Has(ImageExts, p);
    public static bool IsVideo(string p) => Has(VideoExts, p);
    public static bool IsAudio(string p) => Has(AudioExts, p);
    public static bool IsMedia(string p) => IsImage(p) || IsVideo(p) || IsAudio(p);

    static void EnsureDir()
    {
        try { FS.Directory.CreateDirectory(TrashDir); } catch { }
    }

    /// <summary>把文件或文件夹移进 TaoZip 回收站，原路径写入同名元数据供放回。</summary>
    public static void MoveToTrash(string path)
    {
        // 2.19 真机反馈：/Android/data、/Android/obb 是系统保护区，安卓 11+ 应用删不动，
        // 先拦下来给人话提示，不再把原始权限错误抛给用户
        if (IsProtectedPath(path))
            throw new UnauthorizedAccessException("该位置受系统保护（Android/data、obb 或其上层），安卓系统限制应用删除，请用系统文件管理器或对应应用处理");
        EnsureDir();
        bool isDir = FS.Directory.Exists(path);
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string dest = FS.Path.Combine(TrashDir, "删_" + stamp + "_" + FS.Path.GetFileName(path).TrimEnd('.'));
        if (isDir) FS.Directory.Move(path, dest);
        else FS.File.Move(path, dest);
        // 元数据尽力而为：搬移成功就是删除成功，元数据写不上也不许把整个删除判失败
        try { FS.File.WriteAllText(dest + MetaExt, path); } catch { }
    }

    /// <summary>受系统保护的路径：/Android 目录本身及 data、obb 子树（media 不受限可正常删）。</summary>
    public static bool IsProtectedPath(string path)
    {
        var p = (path ?? "").Replace('\\', '/').TrimEnd('/');
        if (p.Equals("/storage/emulated/0/Android", StringComparison.OrdinalIgnoreCase)) return true;
        return p.StartsWith("/storage/emulated/0/Android/data", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/storage/emulated/0/Android/obb", StringComparison.OrdinalIgnoreCase);
    }

    public sealed class TrashItem
    {
        public string EntryPath = "";
        public string OriginalPath = "";
        public string Name = "";
        public bool IsDir;
        public long Size;
        public DateTime Time;
    }

    public static List<TrashItem> ListTrash()
    {
        var list = new List<TrashItem>();
        EnsureDir();
        FS.FileSystemInfo[] entries;
        try { entries = new FS.DirectoryInfo(TrashDir).GetFileSystemInfos(); } catch { return list; }
        foreach (var e in entries)
        {
            if (IsMetaName(e.Name)) continue;
            var item = new TrashItem
            {
                EntryPath = e.FullName,
                Name = e.Name,
                IsDir = e is FS.DirectoryInfo,
                Size = e is FS.FileInfo f ? f.Length : 0,
                Time = e.LastWriteTime,
            };
            foreach (var meta in MetaPaths(e.FullName))
            {
                try
                {
                    if (FS.File.Exists(meta)) { item.OriginalPath = FS.File.ReadAllText(meta).Trim(); break; }
                }
                catch { }
            }
            list.Add(item);
        }
        return list.OrderByDescending(i => i.Time).ToList();
    }

    /// <summary>放回原处；原位置被占或目录没了就自动改名/补目录，绝不覆盖已有内容。</summary>
    public static void Restore(TrashItem it, Context? ctx = null)
    {
        var target = string.IsNullOrEmpty(it.OriginalPath)
            ? FS.Path.Combine("/storage/emulated/0", it.Name)
            : it.OriginalPath;
        var parent = FS.Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(parent)) FS.Directory.CreateDirectory(parent);
        if (FS.File.Exists(target) || FS.Directory.Exists(target))
        {
            var name = FS.Path.GetFileName(target);
            target = FS.Path.Combine(parent ?? TrashDir,
                FS.Path.GetFileNameWithoutExtension(name) + "_恢复_" + DateTime.Now.ToString("HHmmss") + FS.Path.GetExtension(name));
        }
        if (it.IsDir) FS.Directory.Move(it.EntryPath, target);
        else FS.File.Move(it.EntryPath, target);
        foreach (var meta in MetaPaths(it.EntryPath))
        {
            try { if (FS.File.Exists(meta)) FS.File.Delete(meta); } catch { }
        }
        // 媒体文件放回后通知媒体库重新收录，相册能立刻看到
        if (ctx != null && !it.IsDir && IsMedia(target))
            global::Android.Media.MediaScannerConnection.ScanFile(ctx, new[] { target }, null, null);
    }

    /// <summary>彻底删除：回收站内容真删，失败抛错不静默。</summary>
    public static void Purge(TrashItem it)
    {
        ForceDelete(it.EntryPath);
        foreach (var meta in MetaPaths(it.EntryPath))
        {
            try { if (FS.File.Exists(meta)) FS.File.Delete(meta); } catch { }
        }
    }

    static bool IsMetaName(string name) =>
        name.EndsWith(MetaExt, StringComparison.Ordinal) || name.EndsWith(LegacyMetaExt, StringComparison.Ordinal);

    static IEnumerable<string> MetaPaths(string entryPath)
    {
        yield return entryPath + MetaExt;
        yield return entryPath + LegacyMetaExt;
    }

    static void ForceDelete(string p)
    {
        if (FS.Directory.Exists(p))
        {
            foreach (var c in FS.Directory.GetFileSystemEntries(p)) ForceDelete(c);
            FS.Directory.Delete(p);
        }
        else if (FS.File.Exists(p)) FS.File.Delete(p);
        if (FS.Directory.Exists(p) || FS.File.Exists(p))
            throw new FS.IOException("没有删干净：" + p);
    }
}

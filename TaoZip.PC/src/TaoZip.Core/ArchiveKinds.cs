using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TaoZip.Core;

/// <summary>
/// 压缩包格式的唯一事实来源 (知常曰明):
/// 界面识别、拖放判断、右键注册都从这里取, 不再各自维护清单。
/// </summary>
public static class ArchiveKinds
{
    /// <summary>右键菜单/默认关联覆盖的扩展名 (单层)。</summary>
    public static readonly string[] RightClickExtensions =
        { ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".xz" };

    /// <summary>可打开的扩展名 (Path.GetExtension 能取到的单层形态; 复合扩展名见 IsArchive)。</summary>
    public static readonly string[] OpenableExtensions =
        { ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz", ".tgz", ".tbz2", ".txz" };

    /// <summary>是否为可打开的压缩包路径 (含 .tar.gz 等复合扩展名)。</summary>
    public static bool IsArchive(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (OpenableExtensions.Contains(ext)) return true;
        return path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".tar.bz2", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".tar.xz", StringComparison.OrdinalIgnoreCase);
    }
}

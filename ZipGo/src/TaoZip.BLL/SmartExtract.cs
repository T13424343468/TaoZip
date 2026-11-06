using TaoZip.Model;

namespace TaoZip.BLL;

/// <summary>智能解压：决定解到当前目录还是以压缩包名命名的子目录。</summary>
public static class SmartExtract
{
    /// <summary>
    /// 返回建议的目标目录。
    /// 顶层有多个文件/文件夹时建议建以压缩包名命名的子目录（Bandizip 同款行为）；
    /// 包内只有一个顶层条目（无论文件还是文件夹）时直接解到当前目录，避免双层嵌套。
    /// </summary>
    public static string ChooseDestDir(string archivePath, ArchiveInfo info, bool smartEnabled)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(archivePath)) ?? ".";
        if (!smartEnabled || info.TopLevelCount <= 1)
            return parent;
        var sub = Path.GetFileNameWithoutExtension(archivePath);
        return Path.Combine(parent, sub);
    }
}

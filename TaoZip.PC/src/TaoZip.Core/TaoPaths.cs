using System;
using System.IO;

namespace TaoZip.Core;

/// <summary>
/// Tao 系列存储规范: 数据与临时文件默认落 D 盘 (D:\TaoZipData), 不占 C 盘系统空间。
/// D 盘不可用或写入失败时兜底回退用户目录, 保证不闪退不报错。
/// </summary>
public static class TaoPaths
{
    private const string DataRootOnD = @"D:\TaoZipData";

    /// <summary>D 盘是否可用 (存在即认为可写, 写入失败仍有兜底)。</summary>
    public static bool DDriveAvailable => Directory.Exists(@"D:\");

    /// <summary>
    /// 应用数据目录: 默认 D:\TaoZipData\[appName]。
    /// 首次运行时把旧 %APPDATA%\[appName] 内的文件迁移过去, 旧设置不丢。
    /// </summary>
    public static string DataDir(string appName)
    {
        string legacy = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appName);
        if (!DDriveAvailable) return EnsureDir(legacy);
        try
        {
            string dir = Path.Combine(DataRootOnD, appName);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                MigrateLegacy(legacy, dir);
            }
            return dir;
        }
        catch
        {
            // 预防性兜底: D 盘建目录/迁移失败时回退旧位置, 绝不因存储问题闪退
        }
        return EnsureDir(legacy);
    }

    private static string EnsureDir(string dir)
    {
        try { Directory.CreateDirectory(dir); } catch { }
        return dir;
    }

    /// <summary>临时工作目录: 默认 D:\TaoZipData\[appName]\temp, D 盘不可用回退系统临时目录。</summary>
    public static string TempDir(string appName)
    {
        if (DDriveAvailable)
        {
            try
            {
                string dir = Path.Combine(DataDir(appName), "temp");
                Directory.CreateDirectory(dir);
                return dir;
            }
            catch
            {
                // 兜底走系统临时目录
            }
        }
        string fallback = Path.Combine(Path.GetTempPath(), "TaoZip");
        try { Directory.CreateDirectory(fallback); } catch { }
        return fallback;
    }

    private static void MigrateLegacy(string legacyDir, string newDir)
    {
        try
        {
            if (!Directory.Exists(legacyDir)) return;
            foreach (string file in Directory.GetFiles(legacyDir))
            {
                string dest = Path.Combine(newDir, Path.GetFileName(file));
                if (!File.Exists(dest)) File.Copy(file, dest);
            }
        }
        catch
        {
            // 迁移失败不阻塞主流程, 应用仍可正常运行
        }
    }
}

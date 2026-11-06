using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TaoZip.PC.App;

/// <summary>
/// 文件关联管理:
/// - 状态读取走 HKCR (HKCU/HKLM 合并视图): 安装器写入的系统级键也能被看到, 勾选状态真实
/// - 写入走 HKCU (无需管理员权限); 与安装器写同名键, 合并视图中用户键优先
/// - ProgID: TaoZip.Archive → 用 TaoZip 打开
/// - 扩展名默认值抢设/恢复 (备份原默认, 可还原)
/// - 右键菜单: 使用TaoZip解压到此处 / 使用TaoZip压缩
/// - 自愈: 已存在但指向不存在 exe 的旧残留键改写为当前程序 (修"旧键占位导致新菜单不生效")
/// </summary>
public static class FileAssoc
{
    /// <summary>右键/关联覆盖的扩展名 (唯一事实来源: ArchiveKinds, 知常曰明)。</summary>
    public static string[] Extensions => TaoZip.Core.ArchiveKinds.RightClickExtensions;

    private const string ProgId = "TaoZip.Archive";
    private const string MenuKey = "shell\\TaoZip.Extract";
    // 压缩登记走正位: HKCR\*\shell 与 HKCR\Directory\shell (SystemFileAssociations 委屈地不保证合并生效)
    private const string CompressKeyFiles = "*\\shell\\TaoZip.Compress";
    private const string CompressKeyDir = "Directory\\shell\\TaoZip.Compress";
    private static string ExePath =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "TaoZip.exe");

    private static RegistryKey Classes =>
        Registry.CurrentUser.OpenSubKey("Software\\Classes", writable: true)
        ?? throw new InvalidOperationException("无法打开用户注册表(Classes)");

    private static string BackupKeyPath => "Software\\TaoZip\\FileAssocBackup";

    /// <summary>注册/更新 ProgID (指向当前 exe 路径)。</summary>
    private static void EnsureProgId()
    {
        var exe = ExePath;
        using (var k = Classes.CreateSubKey(ProgId))
        {
            k.SetValue("", "TaoZip 压缩包文档");
            using var icon = k.CreateSubKey("DefaultIcon");
            icon.SetValue(null, $"\"{exe}\",0");
            using var cmd = k.CreateSubKey("shell\\open\\command");
            cmd.SetValue(null, $"\"{exe}\" \"%1\"");
        }
    }

    /// <summary>当前扩展名的默认打开方式是否为 TaoZip (读合并视图, 含安装器写的键)。</summary>
    public static bool IsDefault(string ext)
    {
        using var k = Registry.ClassesRoot.OpenSubKey(ext);
        return k?.GetValue(null) as string == ProgId;
    }

    /// <summary>右键"解压到此处"菜单是否已添加 (读合并视图, 含安装器写的键)。</summary>
    public static bool HasContextMenu(string ext)
        => Registry.ClassesRoot.OpenSubKey($"SystemFileAssociations\\{ext}\\{MenuKey}") != null;

    public static bool AllContextMenu() => Extensions.All(HasContextMenu);

    /// <summary>设为默认打开方式 (备份原默认值, 可用 RemoveDefault 还原; 重复调用幂等)。</summary>
    public static void SetDefault(string ext)
    {
        EnsureProgId();
        using (var k = Classes.CreateSubKey(ext))
        {
            var old = k.GetValue(null) as string;
            if (old != ProgId)
            {
                using var bk = Registry.CurrentUser.CreateSubKey(BackupKeyPath);
                using var bkExt = bk.CreateSubKey(ext.TrimStart('.'));
                if (old != null) bkExt.SetValue("OldDefault", old);
                else bkExt.SetValue("OldDefault", "");
            }
            k.SetValue(null, ProgId);
        }
    }

    /// <summary>取消默认关联, 还原备份的原默认值 (若无备份则清除)。</summary>
    public static void RemoveDefault(string ext)
    {
        using (var k = Classes.OpenSubKey(ext, writable: true))
        {
            if (k != null && k.GetValue(null) as string == ProgId)
            {
                using var bk = Registry.CurrentUser.OpenSubKey($"{BackupKeyPath}\\{ext.TrimStart('.')}");
                var old = bk?.GetValue("OldDefault") as string;
                if (!string.IsNullOrEmpty(old)) k.SetValue("", old);
                else k.DeleteValue("", throwOnMissingValue: false);
            }
        }
        try { Registry.CurrentUser.DeleteSubKeyTree($"{BackupKeyPath}\\{ext.TrimStart('.')}", false); }
        catch { /* 备份不存在则忽略 */ }
    }

    /// <summary>键存在但其命令指向的 exe 已不存在 (旧残留占位)。读合并视图, 能看到被用户键遮蔽的真实情况。</summary>
    private static bool KeyCommandIsStale(string keyPath)
    {
        try
        {
            using var k = Registry.ClassesRoot.OpenSubKey(keyPath);
            if (k == null) return false;
            using var cmd = k.OpenSubKey("command");
            var exe = TaoZip.Core.AssocCommand.ExePath(cmd?.GetValue(null) as string);
            return exe != null
                   && !exe.Equals(ExePath, StringComparison.OrdinalIgnoreCase)
                   && !File.Exists(exe);
        }
        catch { return false; }
    }

    private static void WriteExtractKey(string ext, string exe)
    {
        using var k = Classes.CreateSubKey($"SystemFileAssociations\\{ext}\\{MenuKey}");
        k.SetValue(null, "使用TaoZip解压到此处");
        k.SetValue("Icon", $"\"{exe}\",0");
        using var cmd = k.CreateSubKey("command");
        cmd.SetValue(null, $"\"{exe}\" /extract-here \"%1\"");
    }

    private static void WriteCompressKey(string keyPath, string exe)
    {
        using var k = Classes.CreateSubKey(keyPath);
        k.SetValue(null, "使用TaoZip压缩");
        k.SetValue("Icon", $"\"{exe}\",0");
        using var cmd = k.CreateSubKey("command");
        cmd.SetValue(null, $"\"{exe}\" /compress-here \"%1\"");
    }

    /// <summary>
    /// 启动自愈: 右键键已存在但指向已删除的旧 exe 时, 改写为当前程序路径。
    /// 旧版本残留的用户级键会遮蔽安装器写的新键, 造成"新关联不生效", 这里在启动时静默修复。
    /// </summary>
    public static void RepairStaleCommands()
    {
        try
        {
            var exe = ExePath;
            foreach (var ext in Extensions)
            {
                if (KeyCommandIsStale($"SystemFileAssociations\\{ext}\\{MenuKey}"))
                    WriteExtractKey(ext, exe);
            }
            foreach (var keyPath in new[] { CompressKeyFiles, CompressKeyDir })
            {
                if (KeyCommandIsStale(keyPath))
                    WriteCompressKey(keyPath, exe);
            }
        }
        catch { /* 自愈失败不影响主功能, 下次启动再试 */ }
    }

    /// <summary>添加/刷新右键"解压到此处"菜单 (全扩展名; 幂等, 重复调用重写为当前路径)。</summary>
    public static void SetContextMenuAll()
    {
        var exe = ExePath;
        foreach (var ext in Extensions) WriteExtractKey(ext, exe);
    }

    /// <summary>移除右键"解压到此处"菜单 (全扩展名)。</summary>
    public static void RemoveContextMenuAll()
    {
        foreach (var ext in Extensions)
        {
            try { Classes.DeleteSubKeyTree($"SystemFileAssociations\\{ext}\\{MenuKey}", false); }
            catch { /* 不存在则忽略 */ }
        }
    }

    /// <summary>右键"使用TaoZip压缩"菜单是否已添加 (任意文件 + 文件夹 两处齐全; 读合并视图)。</summary>
    public static bool HasCompressMenu()
        => Registry.ClassesRoot.OpenSubKey(CompressKeyFiles) != null
           && Registry.ClassesRoot.OpenSubKey(CompressKeyDir) != null;

    /// <summary>添加/刷新右键"使用TaoZip压缩"菜单: 任意文件(*)与文件夹(Directory)均可用; 多选时各进程自行合并; 幂等。</summary>
    public static void SetCompressMenuAll()
    {
        var exe = ExePath;
        foreach (var keyPath in new[] { CompressKeyFiles, CompressKeyDir })
            WriteCompressKey(keyPath, exe);
    }

    /// <summary>移除右键"使用TaoZip压缩"菜单。</summary>
    public static void RemoveCompressMenuAll()
    {
        foreach (var keyPath in new[] { CompressKeyFiles, CompressKeyDir })
        {
            try { Classes.DeleteSubKeyTree(keyPath, false); }
            catch { /* 不存在则忽略 */ }
        }
    }

    /// <summary>通知资源管理器刷新文件关联图标与菜单。</summary>
    public static void RefreshExplorer()
        => SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero); // SHCNE_ASSOCCHANGED

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);
}

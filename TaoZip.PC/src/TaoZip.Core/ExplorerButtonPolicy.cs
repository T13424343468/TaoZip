using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TaoZip.Core;

/// <summary>
/// 文件管理器动态按钮的显隐判定 (2026-11-15 老板令):
/// 选中项含压缩包 → 显示解压; 其他任意文件/文件夹 → 显示压缩; 两者互斥。
/// 真源归一: 压缩包判定一律走 <see cref="ArchiveKinds.IsArchive"/>, 不另立清单。
/// 与 TaoZipShellext (C++ 原生侧) 共用同一份扩展名事实, 该侧清单由 tools/shellext-sync 校验对齐。
/// </summary>
public static class ExplorerButtonPolicy
{
    /// <summary>按钮效果。</summary>
    public enum ButtonKind
    {
        /// <summary>不显示任何按钮。</summary>
        None,

        /// <summary>TaoZip 解压 (选中项至少一个可打开的压缩包, 压缩包优先)。</summary>
        Extract,

        /// <summary>TaoZip 压缩 (选中其他任意文件/文件夹)。</summary>
        Compress
    }

    /// <summary>
    /// 按选中路径集合判定按钮效果。C++ 侧 GetState 按同口径逐条调用同名判定, 行为逐字对齐。
    /// </summary>
    /// <param name="paths">选中项完整路径 (可空集合视为空选区)。</param>
    public static ButtonKind Decide(IEnumerable<string?> paths)
    {
        bool hasArchive = false;
        bool hasOther = false;
        foreach (var p in paths ?? Enumerable.Empty<string?>())
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            if (IsDirectoryPath(p)) { hasOther = true; continue; }
            if (ArchiveKinds.IsArchive(p)) hasArchive = true;
            else hasOther = true;
        }

        if (hasArchive) return ButtonKind.Extract;
        if (hasOther) return ButtonKind.Compress;
        return ButtonKind.None;
    }

    /// <summary>
    /// 目录判定: 以路径形态为准 (存在性检查在 shell UI 路径禁用, 兼容双分隔符与结尾斜杠)。
    /// </summary>
    private static bool IsDirectoryPath(string path)
    {
        var ext = Path.GetExtension(path);
        return string.IsNullOrEmpty(ext);
    }
}

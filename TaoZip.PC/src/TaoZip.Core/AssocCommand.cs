using System;

namespace TaoZip.Core;

/// <summary>
/// 右键菜单命令串解析: 从 "C:\path\TaoZip.exe" /x "%1" 形式的命令串中提取 exe 路径。
/// 供文件关联自愈逻辑判断旧键指向的程序是否还存在。
/// </summary>
public static class AssocCommand
{
    /// <summary>解析命令串首个可执行文件路径 (兼容带引号/不带引号; 解析不出返回 null)。</summary>
    public static string? ExePath(string? commandValue)
    {
        if (string.IsNullOrWhiteSpace(commandValue)) return null;
        var v = commandValue.Trim();
        if (v.StartsWith('"'))
        {
            var end = v.IndexOf('"', 1);
            return end > 0 ? v[1..end] : null;
        }
        var sp = v.IndexOf(' ');
        return sp > 0 ? v[..sp] : v;
    }
}

using System;
using System.IO;
using System.Text.Json;

namespace TaoZip.Core;

/// <summary>
/// 主题设置: 0=水墨(系列默认) 1=天青 2=墨夜, JSON 落盘。
/// 主题属外观项, 任何读写异常都退回水墨默认, 不影响主功能。
/// </summary>
public class ThemeStore
{
    private readonly string _file;

    public ThemeStore(string appDataDir)
    {
        Directory.CreateDirectory(appDataDir);
        _file = Path.Combine(appDataDir, "theme.json");
    }

    /// <summary>读取主题编号, 缺失/损坏/越界一律回水墨(0)。</summary>
    public int Load()
    {
        try
        {
            if (!File.Exists(_file)) return 0;
            var v = JsonSerializer.Deserialize<int>(File.ReadAllText(_file));
            return v is >= 0 and <= 2 ? v : 0;
        }
        catch { return 0; }
    }

    /// <summary>保存主题编号, 越界收敛到水墨; 落盘失败静默(外观项)。</summary>
    public void Save(int themeId)
    {
        if (themeId is < 0 or > 2) themeId = 0;
        try
        {
            File.WriteAllText(_file, JsonSerializer.Serialize(themeId));
        }
        catch { /* 外观设置, 写失败不打扰用户 */ }
    }
}

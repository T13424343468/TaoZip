using System;
using System.IO;
using Microsoft.UI.Windowing;

namespace TaoZip.PC.App;

/// <summary>统一窗口图标: 输出根目录 app.ico 优先, 兜底 Resources\app.ico; 三个窗口共用一份。</summary>
internal static class AppIcon
{
    public static void Apply(AppWindow window)
    {
        foreach (var rel in new[] { "app.ico", Path.Combine("Resources", "app.ico") })
        {
            var p = Path.Combine(AppContext.BaseDirectory, rel);
            if (!File.Exists(p)) continue;
            window.SetIcon(p);
            return;
        }
    }
}

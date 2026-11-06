using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using TaoZip.Core;

namespace TaoZip.PC.App;

/// <summary>应用入口: 右键解压/压缩走进度小窗, 其余进主窗体。</summary>
public partial class App : Application
{
    private Window? _window;

    /// <summary>进程身份: 主窗/解压窗/压缩窗, 全局异常落盘时标明谁崩的 (v79)。</summary>
    internal static string Role = "主窗";

    public App()
    {
        InitializeComponent();
        // v78 全局异常落网: 真机白屏若源于进程崩溃, 任何线程的未捕获异常都先落盘 theme.log
        // v79 升级: 带进程身份与调用栈首段, 一次定位崩点
        Current.UnhandledException += (_, e) =>
            ThemeService.LogEvent($"全局未捕获[{Role}]: {e.Message} | {e.Exception?.GetType().Name}\r\n栈首: {TrimStack(e.Exception?.StackTrace)}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ThemeService.LogEvent($"全局未捕获[{Role}][后台线程]: {(e.ExceptionObject as Exception)?.GetType().Name} {(e.ExceptionObject as Exception)?.Message}\r\n栈首: {TrimStack((e.ExceptionObject as Exception)?.StackTrace)}");
    }

    internal static string TrimStack(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "无";
        s = s.Replace("\r", "").Split('\n').Take(4).Aggregate((a, b) => a + " <= " + b);
        return s.Length <= 600 ? s : s[..600];
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var cli = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (cli.Length >= 2 && string.Equals(cli[0], "/extract-here", StringComparison.OrdinalIgnoreCase))
        {
            Role = "解压窗";
            _window = new ExtractWindow(cli[1]);
        }
        else if (cli.Length >= 2 && string.Equals(cli[0], "/compress-here", StringComparison.OrdinalIgnoreCase))
        {
            Role = "压缩窗";
            _window = new CompressWindow(cli[1]);
        }
        else
        {
            _window = new MainWindow();
        }
        // 进程残留兜底: 本应用单窗即全部, 窗口关闭即退出;
        // 自包含 WinUI 进程可能被框架消息循环/COM 吊住, 显式退出 + 延时强杀双保险。
        _window.Closed += (_, _) =>
        {
            Current.Exit();
            _ = Task.Run(async () =>
            {
                try { await Task.Delay(1000); Environment.Exit(0); } catch { }
            });
        };
        // 主题: 水墨默认自带, 按上次选择应用到当前窗口 (Tao 系列主题)
        ThemeService.Apply(_window, new ThemeStore(AppDir()).Load());
        _window.Activate();
    }

    /// <summary>与 MainWindow.GetAppDataDir 同源: 默认 D:\TaoZipData\TaoZip, D 盘不可用回退 %APPDATA%\TaoZip。</summary>
    internal static string AppDir() => TaoPaths.DataDir("TaoZip");
}

// 崩溃日志随包落盘（2026-11-23 老板令，测试规范 v3.22 五章）：交付包默认带崩溃日志本地落盘——
// 程序崩溃时把异常类型、堆栈、发生时间、版本号写到本地日志文件，随包内建、无需配置；
// 不搞自动上报、不收集任何个人信息、不碰隐私，日志只留在用户自己机器上，供用户主动反馈问题时取用。
// 落点：%LOCALAPPDATA%\TaoZip\crash\crash-yyyyMMdd-HHmmss-fff.log（独立于数据目录，数据盘故障时仍可落盘）。

namespace TaoZip.PC.App;

/// <summary>崩溃日志落盘器：任何路径下写不进都自吞，绝不因落盘失败二次崩溃。</summary>
internal static class CrashLog
{
    /// <summary>崩溃日志目录（健壮性敏感处零中文：路径全 ASCII）。</summary>
    internal static string Dir =>
        System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "TaoZip", "crash");

    /// <summary>写一份崩溃日志；含异常类型、完整堆栈、发生时间、版本号、进程身份。</summary>
    internal static void Write(string role, System.Exception? ex)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Dir);
            var ver = "unknown";
            try { ver = typeof(CrashLog).Assembly.GetName().Version?.ToString() ?? "unknown"; } catch { }
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("==== TaoZip Crash Log ====");
            sb.AppendLine("Time: " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            sb.AppendLine("Version: " + ver);
            sb.AppendLine("Role: " + role);
            sb.AppendLine("ExceptionType: " + (ex?.GetType().FullName ?? "unknown"));
            sb.AppendLine("Message: " + (ex?.Message ?? "none"));
            sb.AppendLine("StackTrace:");
            sb.AppendLine(ex?.StackTrace ?? "none");
            if (ex is System.AggregateException ag)
            {
                foreach (var inner in ag.InnerExceptions)
                {
                    sb.AppendLine("-- Inner " + inner.GetType().FullName + ": " + inner.Message);
                    sb.AppendLine(inner.StackTrace ?? "none");
                }
            }
            else if (ex?.InnerException is { } ie)
            {
                sb.AppendLine("-- Inner " + ie.GetType().FullName + ": " + ie.Message);
                sb.AppendLine(ie.StackTrace ?? "none");
            }
            var name = "crash-" + System.DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".log";
            System.IO.File.WriteAllText(System.IO.Path.Combine(Dir, name), sb.ToString());
        }
        catch
        {
            // 崩溃落盘失败自吞：绝不在崩溃路径上再抛。
        }
    }
}

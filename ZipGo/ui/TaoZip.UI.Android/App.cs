using Android.App;
using Android.OS;
using Android.Runtime;
using Android.Widget;

namespace TaoZip.UI.Android;

/// <summary>
/// 应用入口（2.14）：注册全局异常兜底。任何未捕获异常一律落盘到外部专属目录
/// taozip_crash.log（追加时间与堆栈），绝不让"闪退了但查不到原因"再发生。
/// </summary>
[Application(Label = "TaoZip")]
public class TaoZipApp : Application
{
    public TaoZipApp(IntPtr handle, JniHandleOwnership transfer) : base(handle, transfer) { }

    public override void OnCreate()
    {
        base.OnCreate();
        AndroidEnvironment.UnhandledExceptionRaiser += (s, e) => WriteCrash("NET", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex) WriteCrash("CLR", ex);
        };
    }

    /// <summary>崩溃日志写外部专属目录（无需权限，卸载即清），写不进再落内部目录。
    /// v3.22 崩溃日志随包落盘令（2026-11-23 老板令）：补齐版本号，异常类型与完整堆栈随 ex.ToString() 落盘；
    /// 不自动上报、不收集个人信息，日志只留在用户自己机器上。</summary>
    void WriteCrash(string tag, Exception ex)
    {
        var ver = "unknown";
        try { ver = PackageManager?.GetPackageInfo(PackageName ?? "", 0)?.VersionName ?? "unknown"; } catch { }
        var line = $"\n==== {DateTime.Now:yyyy-MM-dd HH:mm:ss} [{tag}] Version {ver} ====\n{ex}\n";
        try
        {
            var dir = GetExternalFilesDir(null) ?? FilesDir!;
            var path = FSPath(dir, "taozip_crash.log");
            System.IO.File.AppendAllText(path, line);
        }
        catch
        {
            try { System.IO.File.AppendAllText(FSPath(FilesDir!, "taozip_crash.log"), line); } catch { }
        }
        try
        {
            Toast.MakeText(this, new Java.Lang.String("TaoZip 遇到问题已记录日志，请把 taozip_crash.log 发给开发者"), ToastLength.Long)!.Show();
        }
        catch { }
    }

    static string FSPath(Java.IO.File dir, string name) => System.IO.Path.Combine(dir.AbsolutePath, name);
}

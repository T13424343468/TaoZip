using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using TaoZip.BLL;
using TaoZip.Model;

namespace TaoZip.UI.Android;

/// <summary>
/// 压缩解压前台服务（2.14）：大文件压缩/解压交给它保活，切到桌面、锁屏都不中断。
/// 通知栏实时显示每个任务的进度条、百分比与预计剩余时间，点通知直达任务列表。
/// </summary>
[Service(Name = "com.taozip.app.TaskForegroundService", Exported = false)]
public class TaskForegroundService : Service
{
    public const string ChannelId = "taozip_tasks";
    const int NotifyId = 42;

    DateTime _lastNotify = DateTime.MinValue;
    bool _subscribed;

    public static void EnsureChannel(Context ctx)
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;
        var mgr = (NotificationManager)ctx.GetSystemService(NotificationService)!;
        var ch = new NotificationChannel(ChannelId, "压缩解压进度", NotificationImportance.Low)
        { Description = "大文件压缩与解压的后台进度" };
        mgr.CreateNotificationChannel(ch);
    }

    /// <summary>有任务要跑就唤起前台服务：建立通道、拉起保活、补通知权限。</summary>
    public static void Start(Activity a)
    {
        EnsureChannel(a);
        MainActivity.EnsureNotificationPermission(a);
        var ctx = (Context)a;
        a.StartForegroundService(new Intent(ctx, typeof(TaskForegroundService)));
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        StartForeground(NotifyId, BuildNotification("准备中…", 0, indeterminate: true));
        if (!_subscribed)
        {
            MainActivity.Service.TaskUpdated += OnTaskUpdated;
            MainActivity.Service.ProgressReported += OnProgress;
            _subscribed = true;
        }
        Update();
        return StartCommandResult.NotSticky;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _subscribed)
        {
            MainActivity.Service.TaskUpdated -= OnTaskUpdated;
            MainActivity.Service.ProgressReported -= OnProgress;
            _subscribed = false;
        }
        base.Dispose(disposing);
    }

    void OnTaskUpdated(ArchiveTaskRecord r) => Update();
    void OnProgress(ArchiveProgress p) => Update();

    /// <summary>按当前任务队列刷新通知：全空闲即收摊，否则显示运行中任务的进度。</summary>
    void Update()
    {
        try
        {
            // 通知更新节流：进度事件很密，半秒一刷足够顺眼
            if ((DateTime.Now - _lastNotify).TotalMilliseconds < 400) return;
            _lastNotify = DateTime.Now;

            var running = MainActivity.Service.Records
                .Where(r => r.State is TaskState.Queued or TaskState.Running).ToList();
            if (running.Count == 0)
            {
                StopForeground(true);
                StopSelf();
                return;
            }
            // 2.27 根修：队列里可能只有排队中的任务（前一任务刚收尾、新任务还在等并发许可），
            // First 找不到 Running 直接抛异常炸掉主线程——2.26 真机「压缩直接闪退」即此链路，
            // 改 FirstOrDefault 兜底取第一条，通知属锦上添花件，任何异常都不允许拖垮界面
            var cur = running.FirstOrDefault(r => r.State == TaskState.Running) ?? running[0];
            int queued = running.Count(r => r.State == TaskState.Queued);
            var eta = EstimateEtaPublic(cur.Id, cur.ProgressPercent, cur.StartedAt);
            var title = cur.Title + (queued > 0 ? $"（另有 {queued} 项排队）" : "");
            var nm = (NotificationManager)GetSystemService(NotificationService)!;
            nm.Notify(NotifyId, BuildNotification(title, cur.ProgressPercent, indeterminate: false, eta: eta));
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("TaoZip", "任务通知刷新失败：" + ex);
        }
    }

    /// <summary>剩余时间：按"已用时间 × 剩余比例"估算；刚起步或没数据时老实说估算中。</summary>
    internal static string EstimateEtaPublic(string id, int pct, DateTime startedAt)
    {
        if (pct <= 0) return "剩余时间估算中";
        var elapsed = (DateTime.Now - startedAt).TotalSeconds;
        if (elapsed < 5) return "剩余时间估算中";
        var total = elapsed / pct * (100 - pct);
        if (total > 3600) return $"剩余约 {total / 3600:F0} 小时 {(total % 3600) / 60:F0} 分";
        if (total > 60) return $"剩余约 {total / 60:F0} 分 {total % 60:F0} 秒";
        return $"剩余约 {total:F0} 秒";
    }

    Notification BuildNotification(string title, int pct, bool indeterminate, string eta = "")
    {
        var pi = PendingIntent.GetActivity(this, 0, new Intent(this, typeof(TasksActivity)),
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        var text = indeterminate ? "准备中，点按查看任务" : $"{pct}% · {eta} · 点按查看任务";
        var b = new NotificationCompat.Builder(this, ChannelId)
            .SetSmallIcon(Resource.Drawable.ic_bg)
            .SetContentTitle(title)
            .SetContentText(text)
            .SetOngoing(true)
            .SetOnlyAlertOnce(true)
            .SetContentIntent(pi)
            .SetProgress(100, pct, indeterminate);
        return b.Build()!;
    }
}

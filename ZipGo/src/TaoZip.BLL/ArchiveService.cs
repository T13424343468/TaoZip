using TaoZip.DAL;
using TaoZip.Model;

namespace TaoZip.BLL;

/// <summary>
/// 后台任务服务：解压/压缩/测试任务的排队、进度、取消与历史记录。
/// 线程安全；同一时刻最多并发 MaxConcurrency 个任务（默认 1，模拟 Bandizip 的串行队列）。
/// </summary>
public sealed class ArchiveService : IDisposable
{
    private readonly HistoryStore _history;
    private readonly SemaphoreSlim _gate;
    private readonly Dictionary<string, CancellationTokenSource> _cancels = new();
    private readonly object _lock = new();
    private readonly List<ArchiveTaskRecord> _records = new();

    public int MaxConcurrency { get; }
    public event Action<ArchiveTaskRecord>? TaskUpdated;
    public event Action<ArchiveProgress>? ProgressReported;

    public ArchiveService(HistoryStore? history = null, int maxConcurrency = 1)
    {
        _history = history ?? new HistoryStore(Path.Combine(AppContext.BaseDirectory, "taozip_history.json"));
        MaxConcurrency = Math.Max(1, maxConcurrency);
        _gate = new SemaphoreSlim(MaxConcurrency);
    }

    public IReadOnlyList<ArchiveTaskRecord> Records => _records.ToList();
    public IReadOnlyList<HistoryRecord> History => _history.All;

    /// <summary>提交任务并立即返回任务记录（后台排队执行）。</summary>
    public ArchiveTaskRecord Enqueue(TaskKind kind, string title, Action<string, IProgress<ArchiveProgress>, CancellationToken> work)
    {
        var record = new ArchiveTaskRecord
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Kind = kind,
            Title = title,
        };
        lock (_lock) _records.Add(record);
        // 2.27 根修：通知订阅方必须兜底——订阅方（前台服务刷新通知）抛异常绝不允许
        // 把提交任务的调用方（UI 线程）炸出闪退，2.26 真机「压缩直接闪退」即此链路
        try { TaskUpdated?.Invoke(record); }
        catch (Exception notifyEx)
        {
            global::System.Diagnostics.Debug.WriteLine("ArchiveService: TaskUpdated 通知失败：" + notifyEx.Message);
        }

        var cts = new CancellationTokenSource();
        lock (_lock) _cancels[record.Id] = cts;

        _ = Task.Run(async () =>
        {
            await _gate.WaitAsync(cts.Token).ConfigureAwait(false);
            try
            {
                record.State = TaskState.Running;
                NotifyUpdated(record);
                var progress = new Progress<ArchiveProgress>(p =>
                {
                    record.ProgressPercent = p.Percent;
                    NotifyUpdated(record);
                    NotifyProgress(p);
                });
                work(record.Id, progress, cts.Token);
                record.State = TaskState.Done;
                record.ProgressPercent = 100;
                record.Message = "完成";
            }
            catch (OperationCanceledException)
            {
                record.State = TaskState.Cancelled;
                record.Message = "已取消";
            }
            catch (Exception ex)
            {
                record.State = TaskState.Failed;
                record.Message = ex.Message;
            }
            finally
            {
                record.FinishedAt = DateTime.Now;
                // 2.27 根修：并发许可必须最先归还——历史落盘或通知抛异常绝不允许卡死
                // 任务队列（许可泄漏后所有后续任务永远排队），收尾各步独立兜底
                NotifyUpdated(record);
                try { lock (_lock) _cancels.Remove(record.Id); } catch { }
                try
                {
                    _history.Add(new HistoryRecord
                    {
                        Id = record.Id,
                        Kind = record.Kind,
                        ArchivePath = title,
                        DestPath = record.Message ?? "",
                        Success = record.State == TaskState.Done,
                        Message = record.Message,
                    });
                }
                catch (Exception historyEx)
                {
                    // 历史写不进去只丢记录不丢任务状态；落日志可查
                    global::System.Diagnostics.Debug.WriteLine("ArchiveService: 历史记录写入失败：" + historyEx.Message);
                }
                _gate.Release();
            }
        }, CancellationToken.None);

        return record;
    }

    // 2.27 根修：通知订阅方一律兜底——订阅方（界面/前台服务）抛异常绝不允许炸掉任务链路：
    // 在 Enqueue 调用方炸是「压缩直接闪退」，在任务线程炸会把任务误标失败
    private void NotifyUpdated(ArchiveTaskRecord r)
    {
        try { TaskUpdated?.Invoke(r); }
        catch (Exception notifyEx)
        {
            global::System.Diagnostics.Debug.WriteLine("ArchiveService: TaskUpdated 通知失败：" + notifyEx.Message);
        }
    }

    private void NotifyProgress(ArchiveProgress p)
    {
        try { ProgressReported?.Invoke(p); }
        catch (Exception notifyEx)
        {
            global::System.Diagnostics.Debug.WriteLine("ArchiveService: ProgressReported 通知失败：" + notifyEx.Message);
        }
    }

    public bool Cancel(string taskId)
    {
        lock (_lock)
        {
            if (_cancels.TryGetValue(taskId, out var cts))
            {
                cts.Cancel();
                return true;
            }
            return false;
        }
    }

    /// <summary>清掉全部已结束（完成/失败/取消）的任务记录，返回清掉几条；进行中与排队的不动。</summary>
    public int RemoveFinished()
    {
        lock (_lock)
        {
            var dead = _records.Where(r => r.State is TaskState.Done or TaskState.Failed or TaskState.Cancelled).ToList();
            foreach (var r in dead) _records.Remove(r);
            return dead.Count;
        }
    }

    public void ClearHistory() => _history.Clear();

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var cts in _cancels.Values) cts.Dispose();
            _cancels.Clear();
        }
        _gate.Dispose();
    }
}

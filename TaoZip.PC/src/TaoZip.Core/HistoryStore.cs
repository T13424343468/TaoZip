using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TaoZip.Core;

/// <summary>历史记录条目。</summary>
public class HistoryEntry
{
    public DateTime Time { get; set; }
    public string Action { get; set; } = "";     // 压缩 / 解压 / 测试
    public string ArchivePath { get; set; } = "";
    public string DestPath { get; set; } = "";
    public bool Ok { get; set; }
    public string Message { get; set; } = "";
    public string Sources { get; set; } = "";   // v89: 压缩来源 (分号分隔), 解压条目留空; 旧记录无此字段反序列化为空串
}

/// <summary>历史记录: JSON 落盘, 最多保留 500 条。</summary>
public class HistoryStore
{
    private readonly string _file;
    private static readonly JsonSerializerOptions JsonOpt = new() { WriteIndented = true };

    public HistoryStore(string appDataDir)
    {
        Directory.CreateDirectory(appDataDir);
        _file = Path.Combine(appDataDir, "history.json");
    }

    public List<HistoryEntry> Load()
    {
        // v89: 与并发写撞车 (IO 期被撞见) 重试三次再放弃; 真损坏仍空表兜底
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (!File.Exists(_file)) return new List<HistoryEntry>();
            try
            {
                return JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(_file)) ?? new List<HistoryEntry>();
            }
            catch (System.IO.IOException) { System.Threading.Thread.Sleep(40); }
            catch { return new List<HistoryEntry>(); }
        }
        return new List<HistoryEntry>();
    }

    public void Add(string action, string archivePath, string destPath, bool ok, string message = "", string sources = "")
    {
        // v90: 主窗与右键小窗是两个进程, 各自 Load→插→Save 会整体覆盖对方刚写的记录
        // (lost update), 违反"所有操作都有记录"。整段读改写收进跨进程文件锁。
        WithFileLock(() =>
        {
            var list = Load();
            list.Insert(0, new HistoryEntry
            {
                Time = DateTime.Now,
                Action = action,
                ArchivePath = archivePath,
                DestPath = destPath,
                Ok = ok,
                Message = message,
                Sources = sources
            });
            Save(list);
        });
    }

    /// <summary>锁内删除匹配条目, 返回删除数 (v90)。主窗清空/全部删除走这里——
    /// 旧写法"本地列表改完整体回存"会覆盖右键小窗并发写入的记录。</summary>
    public int RemoveAll(Func<HistoryEntry, bool> match)
    {
        var removed = 0;
        WithFileLock(() =>
        {
            var list = Load();
            removed = list.RemoveAll(new Predicate<HistoryEntry>(match));
            Save(list);
        });
        return removed;
    }

    /// <summary>按内容签名删除单条 (v90): 锁内重读盘上表后全字段匹配。
    /// 锁内重读后的实例与调用方本地实例不同源, ReferenceEquals 对不上, 故按内容定身份。</summary>
    public bool RemoveEntry(HistoryEntry entry)
    {
        var removed = false;
        WithFileLock(() =>
        {
            var list = Load();
            var idx = list.FindIndex(h => h.Time == entry.Time && h.Action == entry.Action &&
                h.ArchivePath == entry.ArchivePath && h.DestPath == entry.DestPath &&
                h.Ok == entry.Ok && h.Message == entry.Message && h.Sources == entry.Sources);
            if (idx >= 0) { list.RemoveAt(idx); removed = true; Save(list); }
        });
        return removed;
    }

    /// <summary>跨进程写互斥 (v90): 独占打开 .lock 文件, 拿不到退避重试;
    /// 5 秒仍拿不到则放行执行——宁可极小概率竞争, 也绝不卡死用户的压缩/解压操作。</summary>
    private void WithFileLock(Action body)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            try
            {
                using (new FileStream(_file + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    body();
                }
                return;
            }
            catch (System.IO.IOException)
            {
                if (sw.ElapsedMilliseconds > 5000) { body(); return; }
                System.Threading.Thread.Sleep(25);
            }
        }
    }

    /// <summary>整表回存 (删除单条/清空/清理后调用), 上限 500 条与 Add 同规。</summary>
    public void Save(List<HistoryEntry> list)
    {
        if (list.Count > 500) list.RemoveRange(500, list.Count - 500);
        // v89: 先写临时文件再原子替换 —— 右键小窗与主窗并发写时, 对方绝不读到半截 JSON
        var tmp = _file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(list, JsonOpt));
        File.Move(tmp, _file, overwrite: true);
    }
}

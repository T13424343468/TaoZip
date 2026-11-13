// 单元层: 纯逻辑/单类职责, 不跨环节协作 (历史记录、目录建议、条目列表)
using System;
using System.IO;
using System.Linq;
using Xunit;
using TaoZip.Core;

namespace TaoZip.Core.Tests.Unit;

public class CoreUnitTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "taozip-unit-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly HistoryStore _hist;

    public CoreUnitTests()
    {
        Directory.CreateDirectory(_dir);
        _hist = new HistoryStore(Path.Combine(_dir, "appdata"));
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    // ---- 功能性 ----

    [Fact]
    public void History_记录读写与上限()
    {
        _hist.Add("压缩", "a.zip", "d/", true);
        _hist.Add("解压", "b.zip", "e/", false, "损坏");
        var list = _hist.Load();
        Assert.Equal(2, list.Count);
        Assert.Equal("解压", list[0].Action);
        Assert.False(list[0].Ok);
    }

    [Fact]
    public void History_压缩来源字段读写与旧记录兼容()
    {
        // v89: 压缩条目带来源, 落盘回读一致
        _hist.Add("压缩", "out.zip", "", true, sources: "a.txt;b.txt");
        var list = _hist.Load();
        Assert.Equal("a.txt;b.txt", list[0].Sources);
        // 旧记录 (无 Sources 字段) 反序列化为空串, 不报错
        var legacy = "[{\"Time\":\"2026-11-03T10:00:00\",\"Action\":\"解压\",\"ArchivePath\":\"b.zip\",\"DestPath\":\"e/\",\"Ok\":true,\"Message\":\"\"}]";
        var parsed = global::System.Text.Json.JsonSerializer.Deserialize<global::System.Collections.Generic.List<HistoryEntry>>(legacy);
        Assert.NotNull(parsed);
        Assert.True(parsed.Count > 0);
        Assert.Equal("解压", parsed[0].Action);
        Assert.Equal("", parsed[0].Sources);
    }

    [Fact]
    public void History_超过上限自动截断()
    {
        for (int i = 0; i < 505; i++) _hist.Add("测试", $"f{i}.zip", "d/", true);
        var list = _hist.Load();
        Assert.Equal(500, list.Count); // 上限 500 条, 最新在前
        Assert.Equal("f504.zip", list[0].ArchivePath);
    }

    [Fact]
    public void History_整表回存支持删除与清空()
    {
        _hist.Add("压缩", "a.zip", "d/", true);
        _hist.Add("解压", "b.zip", "e/", true);
        _hist.Add("测试", "c.zip", "f/", true);
        var list = _hist.Load();
        // 删中间一条后回存, 落盘同步
        list.RemoveAll(h => h.ArchivePath == "b.zip");
        _hist.Save(list);
        var reloaded = _hist.Load();
        Assert.Equal(2, reloaded.Count);
        Assert.DoesNotContain(reloaded, h => h.ArchivePath == "b.zip");
        // 清空后落盘为空表
        reloaded.Clear();
        _hist.Save(reloaded);
        Assert.Empty(_hist.Load());
        // 回存超过上限同样截断 (与 Add 同规)
        var bulk = Enumerable.Range(0, 505)
            .Select(i => new HistoryEntry { Time = DateTime.Now, Action = "测试", ArchivePath = $"g{i}.zip", DestPath = "d/", Ok = true })
            .ToList();
        _hist.Save(bulk);
        Assert.Equal(500, _hist.Load().Count);
    }

    [Fact]
    public void History_并发写入零丢失()
    {
        // v90: 主窗与右键小窗是两个进程, 旧 Add (Load→插→Save 无锁) 会被后写者
        // 整体覆盖先写者 —— lost update。现在整段读改写收进跨进程文件锁,
        // 四个独立 store (模拟四进程) 各写 25 条, 100 条一条都不能少。
        var stores = Enumerable.Range(0, 4)
            .Select(_ => new HistoryStore(Path.Combine(_dir, "appdata"))).ToArray();
        var threads = stores.Select((s, k) => new Thread(() =>
        {
            for (var i = 0; i < 25; i++)
                s.Add(k % 2 == 0 ? "解压" : "压缩", $"pkg{k}-{i}.zip", $"d{k}/", true);
        })).ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());
        var final = _hist.Load();
        Assert.Equal(100, final.Count);
        foreach (var k in Enumerable.Range(0, 4))
            Assert.Equal(25, final.Count(h => h.ArchivePath.StartsWith($"pkg{k}-")));
    }

    [Fact]
    public void History_锁内删除单条与并发共存()
    {
        // v90: RemoveEntry 按内容签名在锁内删, 其余记录原位保留
        for (var i = 0; i < 5; i++) _hist.Add("解压", $"x{i}.zip", "d/", true);
        var snapshot = _hist.Load();
        var victim = snapshot.First(h => h.ArchivePath == "x2.zip");
        Assert.True(_hist.RemoveEntry(victim));
        var after = _hist.Load();
        Assert.Equal(4, after.Count);
        Assert.DoesNotContain(after, h => h.ArchivePath == "x2.zip");
        // 再删同一条: 盘上已无, 返回 false 且不动其余
        Assert.False(_hist.RemoveEntry(victim));
        Assert.Equal(4, _hist.Load().Count);
    }

    [Fact]
    public void DefaultDestFor_去全部后缀并自动加序号()
    {
        File.WriteAllText(Path.Combine(_dir, "我的包.tar.gz"), "x");
        var d1 = ArchiveService.DefaultDestFor(Path.Combine(_dir, "我的包.tar.gz"));
        Assert.Equal(Path.Combine(_dir, "我的包"), d1);
        Directory.CreateDirectory(d1);
        var d2 = ArchiveService.DefaultDestFor(Path.Combine(_dir, "我的包.tar.gz"));
        Assert.EndsWith("我的包(2)", d2); // 已存在则加序号, 不覆盖
    }

    [Fact]
    public void ListEntries_列出条目含tar包()
    {
        var md = Path.Combine(_dir, "readme.md");
        File.WriteAllText(md, "# TaoZip");
        var svc = new ArchiveService();
        var zip = Path.Combine(_dir, "list.zip");
        svc.CreateZip(new[] { md }, zip);
        var entries = svc.ListEntries(zip);
        Assert.Single(entries);
        Assert.Equal("readme.md", entries[0].Name);
        Assert.False(entries[0].IsDir);
        Assert.True(entries[0].Size > 0);

        var tgz = Path.Combine(_dir, "list.tar.gz");
        svc.CreateTarGz(new[] { md }, tgz);
        Assert.Single(svc.ListEntries(tgz));
    }

    // ---- 健壮性 ----

    [Fact]
    public void History_文件损坏时兜底不抛出()
    {
        var appData = Path.Combine(_dir, "appdata");
        File.WriteAllText(Path.Combine(appData, "history.json"), "{这不是JSON!!!");
        var list = _hist.Load();       // 损坏文件 → 返回空列表, 不抛
        Assert.Empty(list);
        _hist.Add("压缩", "ok.zip", "d/", true); // 写入后自愈
        Assert.Single(_hist.Load());
    }

    [Fact]
    public void DefaultDestFor_无后缀与纯点名不崩溃()
    {
        var a = ArchiveService.DefaultDestFor(Path.Combine(_dir, "noext"));
        Assert.Equal(Path.Combine(_dir, "noext"), a);
        var b = ArchiveService.DefaultDestFor(Path.Combine(_dir, "..."));
        Assert.StartsWith(_dir, b); // 纯点名不抛出、不越出根目录
    }
}

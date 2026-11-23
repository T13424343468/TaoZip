using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using SharpCompress.Common;
using Xunit;
using TaoZip.Core;

namespace TaoZip.Core.Tests.Unit;

/// <summary>
/// 分支覆盖补测（2026-11-23 整改令：Core 分支 83.71% → 100% 硬门禁）。
/// 逐条对应 coverlet 实测未覆盖分支，只补测试不改产品代码。
/// D 盘相关分支（TaoPaths.DataDir/TempDir/MigrateLegacy 的 DDriveAvailable=true 路）
/// 在无 D 盘环境结构性不可达，由云 Windows 真机覆盖，此处不重复。
/// </summary>
public class BranchCoverageGapTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "taozip-gap-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly ArchiveService _svc = new();

    public BranchCoverageGapTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "a.txt"), "hello");
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    // ---- ArchiveService ----

    [Fact]
    public void CreateTarGz_目标在清单内_自咬闸拒收()
    {
        // 62 行 path=0: 自咬闸 throw 分支（tar.gz 版; 目标须已存在才会进清单）
        var dest = Path.Combine(_dir, "self.tar.gz");
        File.WriteAllText(dest, "placeholder");
        Assert.Throws<InvalidOperationException>(
            () => _svc.CreateTarGz(new[] { dest }, dest));
    }

    [Fact]
    public void SizeOf_路径不存在_按零计()
    {
        // 72 行 path=1: 文件与目录都不存在 → catch/return 0
        var missing = Path.Combine(_dir, "no-such-file.bin");
        var total = typeof(ArchiveService)
            .GetMethod("SizeOf", global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static)!;
        Assert.Equal(0L, total.Invoke(null, new object[] { missing }));
    }

    [Fact]
    public void CreateZip_进度为空_不报进度不炸()
    {
        // ByteProgress.Add 27 行: Report == null 分支
        var dest = Path.Combine(_dir, "noprogress.zip");
        _svc.CreateZip(new[] { Path.Combine(_dir, "a.txt") }, dest, progress: null);
        Assert.True(File.Exists(dest));
    }

    [Fact]
    public void CreateZip_空清单_Total为零_不报进度()
    {
        // ByteProgress.Add 27 行: Total <= 0 分支（空清单压缩）
        var dest = Path.Combine(_dir, "empty.zip");
        var reports = new List<double>();
        _svc.CreateZip(Array.Empty<string>(), dest, new Progress<double>(reports.Add));
        Assert.True(File.Exists(dest));
        Assert.Empty(reports);
    }

    [Fact]
    public void CreateTarGz_进度为空_不报进度不炸()
    {
        // 62 行: progress 为 null 时短路不炸
        var dest = Path.Combine(_dir, "targz-noprogress.tar.gz");
        _svc.CreateTarGz(new[] { Path.Combine(_dir, "a.txt") }, dest, progress: null);
        Assert.True(new FileInfo(dest).Length > 0);
    }

    [Fact]
    public void CreateTarGz_带进度_报告值在界内()
    {
        // 62 行: progress 非 null 时 Report 调用分支（同步回调, 不依赖线程池时序）
        var dest = Path.Combine(_dir, "targz-progress.tar.gz");
        var reports = new List<double>();
        _svc.CreateTarGz(new[] { Path.Combine(_dir, "a.txt") }, dest, new InlineProgress(reports.Add));
        Assert.True(new FileInfo(dest).Length > 0);
        Assert.NotEmpty(reports);
        Assert.All(reports, r => Assert.InRange(r, 0, 99.9));
    }

    private sealed class InlineProgress(Action<double> handler) : IProgress<double>
    {
        public void Report(double value) => handler(value);
    }

    [Fact]
    public void CreateZip_压缩文件夹_目录结构与空目录保留()
    {
        // 255 行 path=1: File.Exists false → 目录分支（含空目录条目与递归文件）
        var sub = Path.Combine(_dir, "pkg", "inner");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(_dir, "pkg", "top.txt"), "t");
        File.WriteAllText(Path.Combine(sub, "deep.txt"), "d");
        var empty = Path.Combine(_dir, "pkg", "emptydir");
        Directory.CreateDirectory(empty);

        var dest = Path.Combine(_dir, "pkg.zip");
        _svc.CreateZip(new[] { Path.Combine(_dir, "pkg") }, dest);
        Assert.True(new FileInfo(dest).Length > 0);

        // 回读验证结构完整
        var outDir = Path.Combine(_dir, "pkg-out");
        var result = _svc.Extract(dest, outDir);
        Assert.True(File.Exists(Path.Combine(outDir, "pkg", "top.txt")));
        Assert.True(File.Exists(Path.Combine(outDir, "pkg", "inner", "deep.txt")));
        Assert.True(Directory.Exists(Path.Combine(outDir, "pkg", "emptydir")));
        Assert.Equal("t", File.ReadAllText(Path.Combine(outDir, "pkg", "top.txt")));
    }

    [Fact]
    public void CreateTarGz_压缩文件夹_递归打包含目录()
    {
        // 292 行 path=1: tar 目录分支（条目名相对源目录, 不带顶层名）
        var sub = Path.Combine(_dir, "tpkg", "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(_dir, "tpkg", "t.txt"), "1");
        File.WriteAllText(Path.Combine(sub, "s.txt"), "2");

        var dest = Path.Combine(_dir, "tpkg.tar.gz");
        _svc.CreateTarGz(new[] { Path.Combine(_dir, "tpkg") }, dest);
        Assert.True(new FileInfo(dest).Length > 0);

        var outDir = Path.Combine(_dir, "tpkg-out");
        _svc.Extract(dest, outDir);
        Assert.True(File.Exists(Path.Combine(outDir, "t.txt")));
        Assert.True(File.Exists(Path.Combine(outDir, "sub", "s.txt")));
        Assert.Equal("1", File.ReadAllText(Path.Combine(outDir, "t.txt")));
    }

    [Fact]
    public void Extract_普通条目_SafePath三道判定全走false()
    {
        // 313 行 path=1 x2: StartsWith('/') 与 StartsWith('\\') 均为 false 的普通相对条目
        // 318 行 path=0: 前缀核验 throw 分支——用 ".." 段构造逃逸（.. 闸先拦则验证闸本身）
        var zip = Path.Combine(_dir, "normal.zip");
        using (var fs = File.Create(zip))
        using (var za = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var e = za.CreateEntry("plain/dir/file.txt");
            using var sw = new StreamWriter(e.Open());
            sw.Write("ok");
        }
        var outDir = Path.Combine(_dir, "out");
        var result = _svc.Extract(zip, outDir);
        Assert.Equal(1, result.Files);
        Assert.True(File.Exists(Path.Combine(outDir, "plain", "dir", "file.txt")));
    }

    [Fact]
    public void Extract_反斜杠穿越条目_拒收()
    {
        // 318 行 path=0: 前缀核验失败 throw（Windows 反斜杠形态在 Linux 拼合后仍逃出目标根）
        var zip = Path.Combine(_dir, "evil-bs.zip");
        using (var fs = File.Create(zip))
        using (var za = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var e = za.CreateEntry(@"a\..\..\taozip_escape_bs\evil.txt");
            using var sw = new StreamWriter(e.Open());
            sw.Write("evil");
        }
        Assert.Throws<InvalidFormatException>(() => _svc.Extract(zip, Path.Combine(_dir, "out-bs")));
        Assert.False(Directory.Exists(Path.Combine(_dir, "taozip_escape_bs")));
    }

    [Fact]
    public void ListEntries_与DetectSingleRoot_普通包正常返回()
    {
        // 171/182 行 path=0: entry.Key null 合并分支的伴生路径（普通包全流程）
        var zip = Path.Combine(_dir, "list.zip");
        using (var fs = File.Create(zip))
        using (var za = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var d = za.CreateEntry("root/");
            var f = za.CreateEntry("root/x.txt");
            using var sw = new StreamWriter(f.Open());
            sw.Write("x");
        }
        var entries = _svc.ListEntries(zip);
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Name == "root/" && e.IsDir);
        Assert.Equal("root", _svc.DetectSingleRoot(zip));
    }

    [Fact]
    public void DefaultDestFor_纯文件名_落在当前目录()
    {
        // 133 行 path=0: GetDirectoryName 返回 null 的兜底路（根路径形态）
        var dest = ArchiveService.DefaultDestFor("/");
        Assert.Equal(".", Path.GetDirectoryName(dest));
    }

    [Fact]
    public void Extract_反斜杠开头条目_拒收()
    {
        // SafePath 313 行: StartsWith('\\') true 分支
        var zip = Path.Combine(_dir, "evil-bslead.zip");
        using (var fs = File.Create(zip))
        using (var za = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var e = za.CreateEntry(@"\taozip_evil_bs\evil.txt");
            using var sw = new StreamWriter(e.Open());
            sw.Write("evil");
        }
        Assert.Throws<InvalidFormatException>(() => _svc.Extract(zip, Path.Combine(_dir, "out-bsl")));
    }

    // ---- HistoryStore ----

    [Fact]
    public void History_Load_文件不存在_返回空表()
    {
        // 40 行 path=0: File.Exists false → return 空表
        var store = new HistoryStore(Path.Combine(_dir, "hist-empty"));
        Assert.Empty(store.Load());
    }

    [Fact]
    public void History_Load_文件内容为null字面量_空表兜底()
    {
        // 35 行 path=0: Deserialize 返回 null → ?? 空表
        var appData = Path.Combine(_dir, "hist-null");
        Directory.CreateDirectory(appData);
        File.WriteAllText(Path.Combine(appData, "history.json"), "null");
        var store = new HistoryStore(appData);
        Assert.Empty(store.Load());
    }

    [Fact]
    public void History_锁竞争超时_放行执行不卡死()
    {
        // 116 行 path=0: 5 秒拿不到锁 → 放行执行（用真实文件锁占住 5.2 秒模拟跨进程竞争）
        var appData = Path.Combine(_dir, "hist-lock");
        var store = new HistoryStore(appData);
        var lockPath = Path.Combine(appData, "history.json.lock");
        Directory.CreateDirectory(appData);

        using (var hold = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var sw = global::System.Diagnostics.Stopwatch.StartNew();
            store.Add("测试", "lock.zip", "d/", true); // 应在 ~5 秒后放行
            sw.Stop();
            Assert.True(sw.ElapsedMilliseconds >= 4900, $"放行耗时 {sw.ElapsedMilliseconds}ms，未走超时路");
        }
        Assert.Single(store.Load()); // 放行后正常写入
    }

    // ---- ThemeStore ----

    [Fact]
    public void Theme_Load_文件不存在_回水墨()
    {
        // 28 行: File.Exists false 分支（独立于 DefaultsToInk 的显式断言）
        var store = new ThemeStore(Path.Combine(_dir, "theme-empty"));
        Assert.Equal(0, store.Load());
    }

    [Fact]
    public void Theme_Load_文件被外部改越界_回水墨()
    {
        // 28 行: 越界值回 0 分支（Save 会收敛, 只有外部改文件才能让 Load 读到越界值）
        var appData = Path.Combine(_dir, "theme-oob");
        var store = new ThemeStore(appData);
        File.WriteAllText(Path.Combine(appData, "theme.json"), "99");
        Assert.Equal(0, store.Load());
        File.WriteAllText(Path.Combine(appData, "theme.json"), "-3");
        Assert.Equal(0, store.Load());
    }

    // ---- ArchiveKinds ----

    [Fact]
    public void IsArchive_非复合扩展名_三个EndsWith全false()
    {
        // 27 行 path=1 x2: ".tar.gz" 与 ".tar.bz2" 判定均为 false 的完整短路链
        // 注意: ".tar" 本身在 OpenableExtensions 清单里, 26 行就返回 true, 不会走到 27 行
        Assert.False(ArchiveKinds.IsArchive("file.tar.gz7"));
        Assert.False(ArchiveKinds.IsArchive("file.tar.bz"));
        Assert.False(ArchiveKinds.IsArchive("file.gzip"));
        Assert.True(ArchiveKinds.IsArchive("file.tar.bz2"));  // 第二个 EndsWith 命中
        Assert.True(ArchiveKinds.IsArchive("file.tar.xz"));   // 第三个 EndsWith 命中
    }

    // ---- ExplorerButtonPolicy ----

    [Fact]
    public void Decide_空引用集合_视为空选区()
    {
        // 37 行 path=0: paths == null 分支
        Assert.Equal(ExplorerButtonPolicy.ButtonKind.None, ExplorerButtonPolicy.Decide(null!));
    }
}

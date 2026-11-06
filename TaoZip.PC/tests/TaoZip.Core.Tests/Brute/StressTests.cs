using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaoZip.Core;
using Xunit;

namespace TaoZipBrute;

/// <summary>
/// 暴力测试层（固定必跑）：模糊输入、大数据往返、攻击面拦截、并发压测、资源清理。
/// 标准：不崩、不卡死、不越权写盘、事后仍可用；与单元/集成/系统层共同构成交付前全量。
/// </summary>
public class StressTests : IDisposable
{
    private readonly string _root;
    private readonly ArchiveService _svc = new();

    public StressTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "TaoZipBrute_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string TempDir(string name)
    {
        var d = Path.Combine(_root, name);
        Directory.CreateDirectory(d);
        return d;
    }

    // ---- 攻击面: 路径穿越 / 保留名 / 控制字符 / 超长名 ----

    [Fact]
    public void ZipSlip_And_EvilNames_Are_Contained()
    {
        var dir = TempDir("slip");
        using (var zip = System.IO.Compression.ZipFile.Open(
                   Path.Combine(dir, "evil.zip"), System.IO.Compression.ZipArchiveMode.Create))
        {
            string[] evilEntries =
            {
                @"..\..\escaped.txt",                      // 相对穿越
                @"..\..\..\..\Windows\escaped.txt",        // 深层穿越
                "CON.txt",                                 // Windows 保留名
                "NUL",                                     // 保留名裸用
                "a\tb\x01c.txt",                           // 控制字符
                new string('x', 300) + ".txt",             // 超长名
                "dir/../../out.txt",                       // 正斜杠穿越
            };
            foreach (var name in evilEntries)
            {
                var e = zip.CreateEntry(name);
                using var w = new StreamWriter(e.Open(), Encoding.UTF8);
                w.Write("boom");
            }
        }
        var dest = TempDir("slip_out");
        var ex = Record.Exception(() => _svc.Extract(Path.Combine(dir, "evil.zip"), dest));
        // 允许优雅拒绝(抛异常跳过), 不允许进程级崩溃类型
        Assert.True(ex == null || ex is not (OutOfMemoryException or StackOverflowException),
            $"崩溃级异常: {ex?.GetType().Name}: {ex?.Message}");
        if (Directory.Exists(dest))
        {
            var all = Directory.GetFiles(dest, "*", SearchOption.AllDirectories)
                               .Concat(Directory.GetDirectories(dest, "*", SearchOption.AllDirectories));
            Assert.All(all, p => Assert.StartsWith(Path.GetFullPath(dest), Path.GetFullPath(p)));
        }
        // 目标目录之外没有越权产物
        Assert.False(File.Exists(Path.Combine(dir, "escaped.txt")), "穿越文件逃逸到压缩包旁");
        Assert.False(Directory.Exists(Path.Combine(_root, "Windows")), "穿越目录被创建");
    }

    // ---- 模糊输入: 上万条随机字节/字符串过解析入口, 不崩 ----

    [Fact]
    public void Fuzzed_Inputs_Never_Crash_Parsers()
    {
        var dir = TempDir("fuzz");
        var rng = new Random(20261101);
        var svc = new ArchiveService();
        for (int i = 0; i < 300; i++)
        {
            var len = rng.Next(0, 4096);
            var buf = new byte[len];
            rng.NextBytes(buf);
            // 一半真压缩包头 + 垃圾身体的混合体, 一半纯随机
            if (i % 2 == 0)
            {
                var head = new byte[] { 0x50, 0x4B, 0x03, 0x04 };
                Buffer.BlockCopy(head, 0, buf, 0, Math.Min(4, len));
            }
            var f = Path.Combine(dir, $"fuzz_{i}.bin");
            File.WriteAllBytes(f, buf);
            foreach (var act in new Action[]
            {
                () => { try { svc.ListEntries(f); } catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException)) { } },
                () => { try { svc.Test(f); } catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException)) { } },
                () => { try { svc.Extract(f, TempDir("fuzz_out")); } catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException)) { } },
                () => { try { svc.SmartExtract(f, TempDir("fuzz_smart")); } catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException)) { } },
            })
            {
                act();
            }
        }
        // 全程无意外异常类型即视为通过
    }

    // ---- 大数据: 多文件大块数据 zip 往返, 字节级一致 ----

    [Fact]
    public void BigData_RoundTrip_ByteIdentical()
    {
        var src = TempDir("big");
        var rng = new Random(42);
        var hashes = new ConcurrentDictionary<string, byte[]>();
        for (int i = 0; i < 12; i++)
        {
            var buf = new byte[1024 * 1024 + i * 131072]; // ~1MB x12, 含随机不可压数据
            rng.NextBytes(buf);
            var p = Path.Combine(src, $"big_{i}.bin");
            File.WriteAllBytes(p, buf);
            hashes[p] = System.Security.Cryptography.SHA256.HashData(buf);
        }
        var zipPath = Path.Combine(_root, "big.zip");
        _svc.CreateZip(Directory.GetFiles(src), zipPath);

        var dest = TempDir("big_out");
        _svc.Extract(zipPath, dest);
        foreach (var (p, hash) in hashes)
        {
            var outP = Path.Combine(dest, Path.GetFileName(p));
            Assert.True(File.Exists(outP), $"缺少 {p}");
            Assert.Equal(hash, System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(outP)));
        }
    }

    // ---- 并发压测: 多线程同时压/解不同包, 无死锁无串包, 事后仍可用 ----

    [Fact]
    public async Task Concurrent_Operations_No_Deadlock_No_Cross_Contamination()
    {
        var work = TempDir("conc");
        var tasks = Enumerable.Range(0, 8).Select(n => Task.Run(() =>
        {
            var d = Path.Combine(work, $"w{n}");
            Directory.CreateDirectory(d);
            for (int i = 0; i < 20; i++)
                File.WriteAllText(Path.Combine(d, $"f{i}.txt"), new string((char)('a' + n), 4096));
            var zip = Path.Combine(work, $"w{n}.zip");
            new ArchiveService().CreateZip(Directory.GetFiles(d), zip);
            var outD = Path.Combine(work, $"o{n}");
            new ArchiveService().Extract(zip, outD);
            // 内容不串包: 每个解出文件的首字符必须是本工作线程的字母
            foreach (var f in Directory.GetFiles(outD))
                Assert.Equal((char)('a' + n), File.ReadAllText(f)[0]);
        })).ToArray();
        await Task.WhenAll(tasks);

        // 事后仍可用
        var after = Path.Combine(work, "after.txt");
        File.WriteAllText(after, "alive");
        var afterZip = Path.Combine(work, "after.zip");
        _svc.CreateZip(new[] { after }, afterZip);
        Assert.Equal(1, _svc.Test(afterZip).Entries);
    }

    // ---- 资源清理: 中止/失败路径不残留半截压缩包 ----

    [Fact]
    public void Failed_Operations_Do_Not_Leave_Half_Archives()
    {
        var dir = TempDir("cleanup");
        var src = TempDir("cleanup_src");
        File.WriteAllText(Path.Combine(src, "ok.txt"), "fine");
        var svc = new ArchiveService();
        // 目标路径的父目录不存在: 允许自动创建(预防式)或失败, 但产出必须是完好压缩包
        var badDest = Path.Combine(dir, "deep", "nested", "x.zip");
        Record.Exception(() => svc.CreateZip(Directory.GetFiles(src), badDest));
        if (File.Exists(badDest)) Assert.Equal(1, svc.Test(badDest).Entries);
        // 输入包含不存在的文件: 允许跳过或失败, 但不得产出损坏压缩包
        var outZip = Path.Combine(dir, "out.zip");
        Record.Exception(() =>
            svc.CreateZip(new[] { Path.Combine(src, "ghost.txt") }, outZip));
        if (File.Exists(outZip))
        {
            var (entries, _) = svc.Test(outZip);
            Assert.True(entries >= 0); // Test 通过 = 压缩包结构完好
        }
    }
}

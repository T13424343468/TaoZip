// 安全防护回归 (2026-11-14 审查红1红2): ZipSlip 盘符/根路径穿越 + 压缩源目标重合数据丢失
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using SharpCompress.Common;
using Xunit;
using TaoZip.Core;

namespace TaoZip.Core.Tests.Integration;

public class ZipSlipGuardTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "taozip-slip-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly ArchiveService _svc = new();

    public ZipSlipGuardTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "a.txt"), "hello");
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    /// <summary>构造含指定条目名的 zip (条目名完全可控, 模拟恶意包)。</summary>
    private string MakeEvilZip(string entryName)
    {
        var zip = Path.Combine(_dir, "evil-" + Guid.NewGuid().ToString("N")[..8] + ".zip");
        using var fs = File.Create(zip);
        using var zipArchive = new ZipArchive(fs, ZipArchiveMode.Create);
        var e = zipArchive.CreateEntry(entryName);
        using var sw = new StreamWriter(e.Open());
        sw.Write("evil");
        return zip;
    }

    [Fact]
    public void Extract_根路径条目_拒收()
    {
        // /abs 形态在 Windows 与 Linux 都是 rooted 路径, 双平台都必须拒收
        var zip = MakeEvilZip("/taozip_evil/evil.txt");
        var outDir = Path.Combine(_dir, "out1");
        Assert.Throws<InvalidFormatException>(() => _svc.Extract(zip, outDir));
        Assert.False(Directory.Exists(Path.Combine(_dir, "taozip_evil")));
    }

    [Fact]
    public void Extract_盘符条目_Windows拒收()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return; // Linux 下盘符不是 rooted 路径, 由前缀兜底闸保证不外逃
        var zip = MakeEvilZip("C:/taozip_evil/evil.txt");
        Assert.Throws<InvalidFormatException>(() => _svc.Extract(zip, Path.Combine(_dir, "out2")));
        var zip2 = MakeEvilZip("C:taozip_evil");
        Assert.Throws<InvalidFormatException>(() => _svc.Extract(zip2, Path.Combine(_dir, "out3")));
        Assert.False(Directory.Exists(@"C:\taozip_evil"));
    }

    [Fact]
    public void Extract_常规条目_仍正常解压()
    {
        var zip = Path.Combine(_dir, "ok.zip");
        _svc.CreateZip(new[] { Path.Combine(_dir, "a.txt") }, zip);
        var outDir = Path.Combine(_dir, "out4");
        var r = _svc.Extract(zip, outDir);
        Assert.Equal(1, r.Files);
        Assert.Equal("hello", File.ReadAllText(Path.Combine(outDir, "a.txt")));
    }

    [Fact]
    public void CreateZip_目标在待压缩清单中_拒绝并不动源()
    {
        var src = Path.Combine(_dir, "victim.txt");
        File.WriteAllText(src, "valuable data");
        Assert.Throws<InvalidOperationException>(() => _svc.CreateZip(new[] { src }, src));
        Assert.Equal("valuable data", File.ReadAllText(src)); // 源文件一个字节都不能少
    }

    [Fact]
    public void CreateZip_多源含目标_拒绝并不动源()
    {
        var src1 = Path.Combine(_dir, "s1.txt");
        var src2 = Path.Combine(_dir, "s2.txt");
        var dest = Path.Combine(_dir, "s2.txt"); // 目标恰好是第二个源
        File.WriteAllText(src1, "one");
        File.WriteAllText(src2, "two");
        Assert.Throws<InvalidOperationException>(() => _svc.CreateZip(new[] { src1, src2 }, dest));
        Assert.Equal("one", File.ReadAllText(src1));
        Assert.Equal("two", File.ReadAllText(src2));
    }

    [Fact]
    public void CreateZip_正常压缩_不受兜底闸影响()
    {
        var dest = Path.Combine(_dir, "good.zip");
        _svc.CreateZip(new[] { Path.Combine(_dir, "a.txt") }, dest);
        Assert.True(File.Exists(dest));
        Assert.Contains(".zip", Directory.GetFiles(_dir).Select(Path.GetExtension));
    }
}

using System.IO;
using System.IO.Compression;
using System.Linq;
using TaoZip.Core;
using Xunit;

namespace TaoZip.Core.Tests;

/// <summary>压缩进度回调与目标自排除: 右键压缩的底层保障。</summary>
public class CompressionProgressTests : IDisposable
{
    private readonly string _dir;
    private readonly ArchiveService _svc = new();

    public CompressionProgressTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "taozip-cptest-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string MakeFile(string name, int bytes)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllBytes(p, new byte[bytes]);
        return p;
    }

    [Fact]
    public void CreateZip_报告进度且最终到100附近()
    {
        var f1 = MakeFile("a.bin", 512 * 1024);
        var f2 = MakeFile("b.bin", 256 * 1024);
        var dest = Path.Combine(_dir, "out.zip");
        var reports = new List<double>();
        // Progress<T> 是异步上报, 测试里改用同步收集器
        _svc.CreateZip(new[] { f1, f2 }, dest, new SyncProgress(reports.Add));
        Assert.True(File.Exists(dest));
        Assert.NotEmpty(reports);
        Assert.All(reports, p => Assert.InRange(p, 0, 99.9)); // 过程值封顶 99.9, 完成由界面置 100
        Assert.Equal(reports.Max(), reports.Last()); // 单调不减的最后一个即最大值
    }

    private sealed class SyncProgress : IProgress<double>
    {
        private readonly Action<double> _sink;
        public SyncProgress(Action<double> sink) => _sink = sink;
        public void Report(double value) => _sink(value);
    }

    [Fact]
    public void CreateZip_多文件与文件夹混合都能进包()
    {
        var f1 = MakeFile("a.txt", 1024);
        var sub = Path.Combine(_dir, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllBytes(Path.Combine(sub, "inner.txt"), new byte[2048]);
        var dest = Path.Combine(_dir, "mix.zip");
        _svc.CreateZip(new[] { f1, sub }, dest);
        using var zip = ZipFile.OpenRead(dest);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("a.txt", names);
        Assert.Contains("sub/", names);
        Assert.Contains("sub/inner.txt", names);
    }

    [Fact]
    public void CreateZip_目标在来源目录内时不会把自己压进去()
    {
        var src = Path.Combine(_dir, "folder");
        Directory.CreateDirectory(src);
        File.WriteAllBytes(Path.Combine(src, "x.txt"), new byte[4096]);
        // 目标压缩包放在来源文件夹内部 (自定义位置场景)
        var dest = Path.Combine(src, "self.zip");
        _svc.CreateZip(new[] { src }, dest);
        using var zip = ZipFile.OpenRead(dest);
        Assert.DoesNotContain(zip.Entries, e => e.FullName.EndsWith("self.zip", global::System.StringComparison.OrdinalIgnoreCase));
        Assert.Contains(zip.Entries, e => e.FullName.EndsWith("x.txt", global::System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreateZip_不存在的路径整体跳过不抛()
    {
        var f1 = MakeFile("ok.txt", 128);
        var dest = Path.Combine(_dir, "skip.zip");
        _svc.CreateZip(new[] { f1, Path.Combine(_dir, "不存在.txt") }, dest);
        Assert.True(File.Exists(dest));
        using var zip = ZipFile.OpenRead(dest);
        Assert.Single(zip.Entries);
    }
}

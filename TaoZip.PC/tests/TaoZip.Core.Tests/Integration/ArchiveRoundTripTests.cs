// 集成层: 跨环节协作 (压缩→落盘→解压→回读、进度回调贯穿核心链路)
using System;
using System.IO;
using System.Linq;
using Xunit;
using TaoZip.Core;

namespace TaoZip.Core.Tests.Integration;

public class ArchiveRoundTripTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "taozip-int-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly ArchiveService _svc = new();

    public ArchiveRoundTripTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "你好.txt"), "你好，道压缩！" + new string('X', 5000));
        File.WriteAllText(Path.Combine(_dir, "readme.md"), "# TaoZip");
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    // ---- 功能性 ----

    [Fact]
    public void Zip_RoundTrip_中文文件名不乱码()
    {
        var zip = Path.Combine(_dir, "t.zip");
        _svc.CreateZip(new[] { Path.Combine(_dir, "你好.txt"), Path.Combine(_dir, "readme.md") }, zip);
        var outDir = Path.Combine(_dir, "out");
        var r = _svc.Extract(zip, outDir);
        Assert.Equal(2, r.Files);
        Assert.Equal("你好，道压缩！" + new string('X', 5000), File.ReadAllText(Path.Combine(outDir, "你好.txt")));
        Assert.Equal("# TaoZip", File.ReadAllText(Path.Combine(outDir, "readme.md")));
    }

    [Fact]
    public void TarGz_RoundTrip()
    {
        var tgz = Path.Combine(_dir, "t.tar.gz");
        _svc.CreateTarGz(new[] { Path.Combine(_dir, "readme.md") }, tgz);
        var r = _svc.Extract(tgz, Path.Combine(_dir, "out2"));
        Assert.Equal(1, r.Files);
        Assert.Equal("# TaoZip", File.ReadAllText(Path.Combine(_dir, "out2", "readme.md")));
    }

    [Fact]
    public void Zip_压缩文件夹含子目录()
    {
        var sub = Path.Combine(_dir, "proj", "src");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "a.cs"), "class A {}");
        var zip = Path.Combine(_dir, "proj.zip");
        _svc.CreateZip(new[] { Path.Combine(_dir, "proj") }, zip);
        var r = _svc.Extract(zip, Path.Combine(_dir, "out3"));
        Assert.Equal(1, r.Files);
        Assert.Equal("class A {}", File.ReadAllText(Path.Combine(_dir, "out3", "proj", "src", "a.cs")));
    }

    [Fact]
    public void Zip_空子目录解压后保留()
    {
        var src = Path.Combine(_dir, "withempty", "sub");
        Directory.CreateDirectory(src);
        Directory.CreateDirectory(Path.Combine(_dir, "withempty", "empty"));
        File.WriteAllText(Path.Combine(src, "f.txt"), "x");
        var zip = Path.Combine(_dir, "empty.zip");
        _svc.CreateZip(new[] { Path.Combine(_dir, "withempty") }, zip);
        var outDir = Path.Combine(_dir, "outempty");
        _svc.Extract(zip, outDir);
        Assert.True(Directory.Exists(Path.Combine(outDir, "withempty", "empty")), "空目录未保留");
        Assert.True(File.Exists(Path.Combine(outDir, "withempty", "sub", "f.txt")));
    }

    [Fact]
    public void Test_完好包返回统计()
    {
        var zip = Path.Combine(_dir, "t2.zip");
        _svc.CreateZip(new[] { Path.Combine(_dir, "readme.md") }, zip);
        var (n, b) = _svc.Test(zip);
        Assert.Equal(1, n);
        Assert.True(b > 0);
    }

    // ---- 健壮性 ----

    [Fact]
    public void Progress_回调始终在0到100之间且有限()
    {
        var sub = Path.Combine(_dir, "big", "deep", "er");
        Directory.CreateDirectory(sub);
        var bytes = new byte[3 << 20]; // 3MB, 覆盖 1MB 分块的多次进度
        new Random(42).NextBytes(bytes);
        File.WriteAllBytes(Path.Combine(sub, "data.bin"), bytes);

        var zip = Path.Combine(_dir, "big.zip");
        _svc.CreateZip(new[] { Path.Combine(_dir, "big") }, zip);

        // 同步记录器: Progress<T> 依赖同步上下文, 测试里改用直调记录, 避免异步竞态假失败
        var vals = new global::System.Collections.Concurrent.ConcurrentQueue<double>();
        _svc.Extract(zip, Path.Combine(_dir, "outp"), new InlineProgress(vals));

        Assert.True(vals.Count > 0, "进度回调一次都没触发");
        double last = -1;
        foreach (var v in vals)
        {
            Assert.True(double.IsFinite(v) && v >= 0 && v <= 100, $"进度越界: {v}");
            Assert.True(v >= last - 0.01, $"进度回跳: {last} -> {v}");
            last = v;
        }
        Assert.True(vals.Last() <= 100.0);
    }

    private sealed class InlineProgress(global::System.Collections.Concurrent.ConcurrentQueue<double> q) : IProgress<double>
    {
        public void Report(double value) => q.Enqueue(value);
    }
}

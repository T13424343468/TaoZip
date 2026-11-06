using System;
using System.IO;
using TaoZip.Core;
using Xunit;

namespace TaoZip.Core.Tests.Unit;

/// <summary>Tao 系列存储规范: 数据/临时目录默认 D 盘, D 盘不可用兜底回退不闪退。</summary>
public class TaoPathsTests
{
    [Fact]
    public void DataDir_Always_ReturnsWritableDirectoryUnderAppName()
    {
        var dir = TaoPaths.DataDir("TaoZipTests");
        Assert.False(string.IsNullOrWhiteSpace(dir));
        Assert.Equal("TaoZipTests", Path.GetFileName(dir));
        Assert.True(Directory.Exists(dir));
        // 目录必须可写 (D 盘与回退路径都要求可写)
        var probe = Path.Combine(dir, ".probe");
        File.WriteAllText(probe, "ok");
        Assert.Equal("ok", File.ReadAllText(probe));
        File.Delete(probe);
    }

    [Fact]
    public void TempDir_Always_ExistsAndWritable()
    {
        var dir = TaoPaths.TempDir("TaoZipTests");
        Assert.False(string.IsNullOrWhiteSpace(dir));
        Assert.True(Directory.Exists(dir));
        var probe = Path.Combine(dir, ".probe");
        File.WriteAllText(probe, "ok");
        File.Delete(probe);
    }

    [Fact]
    public void DataDir_FallbackPath_MatchesLegacyLocation_WhenNoDDrive()
    {
        // 沙箱(Linux)无 D 盘: 应回退用户目录, 不抛异常
        if (TaoPaths.DDriveAvailable) return; // 真有 D 盘的环境此断言不适用
        var dir = TaoPaths.DataDir("TaoZipTests");
        var expectedBase = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaoZipTests");
        Assert.Equal(Path.GetFullPath(expectedBase), Path.GetFullPath(dir));
    }
}

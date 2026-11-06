using TaoZip.Core;
using Xunit;

namespace TaoZip.Core.Tests.Unit;

/// <summary>格式事实来源: 界面识别与右键注册共用, 边界必须钉死。</summary>
public class ArchiveKindsTests
{
    [Theory]
    [InlineData("a.zip")]
    [InlineData("b.7z")]
    [InlineData("c.rar")]
    [InlineData("d.tar")]
    [InlineData("e.tgz")]
    [InlineData("f.tbz2")]
    [InlineData("g.txz")]
    public void IsArchive_SingleExtension_True(string path) => Assert.True(ArchiveKinds.IsArchive(path));

    [Theory]
    [InlineData("x.tar.gz")]
    [InlineData("x.tar.bz2")]
    [InlineData("x.tar.xz")]
    public void IsArchive_CompoundExtension_True(string path) => Assert.True(ArchiveKinds.IsArchive(path));

    [Theory]
    [InlineData("a.txt")]
    [InlineData("a.docx")]
    [InlineData("无扩展名")]
    [InlineData("a.gzip")]
    public void IsArchive_NonArchive_False(string path) => Assert.False(ArchiveKinds.IsArchive(path));

    [Fact]
    public void IsArchive_CaseInsensitive()
        => Assert.True(ArchiveKinds.IsArchive("PHOTO.ZIP"));

    [Fact]
    public void RightClickExtensions_ExactlyEightSingleLayer()
    {
        Assert.Equal(8, ArchiveKinds.RightClickExtensions.Length);
        Assert.All(ArchiveKinds.RightClickExtensions, e => Assert.StartsWith(".", e));
    }
}

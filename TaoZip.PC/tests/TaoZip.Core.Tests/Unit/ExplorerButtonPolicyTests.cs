using System;
using System.Collections.Generic;
using TaoZip.Core;
using Xunit;

namespace TaoZip.Core.Tests.Unit;

/// <summary>
/// 文件管理器动态按钮显隐判定 (2026-11-15 老板令) 的回归断言:
/// C++ 侧 TaoZipShellext 的 GetState 与本判定逐字同口径, 这里钉死行为。
/// </summary>
public class ExplorerButtonPolicyTests
{
    [Fact]
    public void ArchiveSelected_ShowsExtract()
        => Assert.Equal(ExplorerButtonPolicy.ButtonKind.Extract, ExplorerButtonPolicy.Decide(new[] { "a.zip" }));

    [Fact]
    public void ArchiveCompoundExtension_ShowsExtract()
        => Assert.Equal(ExplorerButtonPolicy.ButtonKind.Extract, ExplorerButtonPolicy.Decide(new[] { "x.tar.gz" }));

    [Fact]
    public void PlainFile_ShowsCompress()
        => Assert.Equal(ExplorerButtonPolicy.ButtonKind.Compress, ExplorerButtonPolicy.Decide(new[] { "a.txt" }));

    [Fact]
    public void Folder_ShowsCompress()
        => Assert.Equal(ExplorerButtonPolicy.ButtonKind.Compress, ExplorerButtonPolicy.Decide(new[] { @"D:\资料\素材" }));

    [Fact]
    public void MixedArchiveAndOther_ArchiveWins()
        => Assert.Equal(ExplorerButtonPolicy.ButtonKind.Extract, ExplorerButtonPolicy.Decide(new[] { "a.txt", "b.zip" }));

    [Fact]
    public void EmptySelection_ShowsNone()
        => Assert.Equal(ExplorerButtonPolicy.ButtonKind.None, ExplorerButtonPolicy.Decide(Array.Empty<string>()));

    [Fact]
    public void NullEntries_Skipped()
        => Assert.Equal(ExplorerButtonPolicy.ButtonKind.None, ExplorerButtonPolicy.Decide(new string?[] { null, "" }));

    [Fact]
    public void ArchiveWithBlankEntries_StillExtract()
        => Assert.Equal(ExplorerButtonPolicy.ButtonKind.Extract, ExplorerButtonPolicy.Decide(new string?[] { " ", "c.7z" }));

    [Fact]
    public void CaseInsensitive_Archive()
        => Assert.Equal(ExplorerButtonPolicy.ButtonKind.Extract, ExplorerButtonPolicy.Decide(new[] { "PHOTO.ZIP" }));

    [Fact]
    public void PathWithNoExtension_IsFolderLike_ShowsCompress()
        => Assert.Equal(ExplorerButtonPolicy.ButtonKind.Compress, ExplorerButtonPolicy.Decide(new[] { "README" }));
}

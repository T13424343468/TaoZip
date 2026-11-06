using System;
using TaoZip.Core;
using Xunit;

namespace TaoZip.Core.Tests.Unit;

/// <summary>右键命令串解析: 自愈逻辑依赖它判断旧键指向的 exe 是否还存在, 边界必须齐全。</summary>
public class AssocCommandTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\TaoZip\\TaoZip.exe\" \"%1\"", "C:\\Program Files\\TaoZip\\TaoZip.exe")]
    [InlineData("\"D:\\Program Files\\TaoZip\\TaoZip.exe\" /extract-here \"%1\"", "D:\\Program Files\\TaoZip\\TaoZip.exe")]
    [InlineData("C:\\TaoZip\\TaoZip.exe /compress-here \"%1\"", "C:\\TaoZip\\TaoZip.exe")]
    [InlineData("C:\\TaoZip\\TaoZip.exe", "C:\\TaoZip\\TaoZip.exe")]
    public void ExePath_ValidCommand_ReturnsExe(string command, string expected)
        => Assert.Equal(expected, AssocCommand.ExePath(command));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"未闭合")]
    public void ExePath_InvalidCommand_ReturnsNull(string? command)
        => Assert.Null(AssocCommand.ExePath(command));
}

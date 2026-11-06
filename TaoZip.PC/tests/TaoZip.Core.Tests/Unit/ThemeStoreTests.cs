using System;
using System.IO;
using TaoZip.Core;
using Xunit;

namespace TaoZip.Core.Tests.Unit;

/// <summary>主题设置：水墨为系列默认(0)，存取保真、损坏回退。</summary>
public class ThemeStoreTests : IDisposable
{
    private readonly string _dir;

    public ThemeStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "TaoZipTheme_" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void DefaultsToInk()
    {
        Assert.Equal(0, new ThemeStore(_dir).Load());
    }

    [Fact]
    public void RoundTripsAllThreeThemes()
    {
        var store = new ThemeStore(_dir);
        foreach (var id in new[] { 2, 1, 0 })
        {
            store.Save(id);
            Assert.Equal(id, store.Load());
        }
    }

    [Fact]
    public void OutOfRange_ConvergesToInk()
    {
        var store = new ThemeStore(_dir);
        store.Save(99);
        Assert.Equal(0, store.Load());
        store.Save(-1);
        Assert.Equal(0, store.Load());
    }

    [Fact]
    public void CorruptFile_FallsBackToInk()
    {
        var store = new ThemeStore(_dir);
        store.Save(2);
        File.WriteAllText(Path.Combine(_dir, "theme.json"), "{not json");
        Assert.Equal(0, store.Load());
    }
}

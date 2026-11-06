// 系统层: 端到端全链路 + 故障注入 + 交付物/构建链完整性检查
using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using TaoZip.Core;

namespace TaoZip.Core.Tests.System;

public class E2EAndDeliveryTests : IDisposable
{
    static E2EAndDeliveryTests() => Encoding.RegisterProvider(global::System.Text.CodePagesEncodingProvider.Instance);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "taozip-e2e-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly ArchiveService _svc = new();

    public E2EAndDeliveryTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    /// <summary>从测试程序集位置向上定位仓库根目录 (含 build.bat)。</summary>
    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "build.bat"))) d = d.Parent!;
        return d?.FullName ?? throw new InvalidOperationException("未找到仓库根目录");
    }

    // ---- 功能性: 端到端全链路 ----

    [Fact]
    public void 端到端_多级中文目录树全链路()
    {
        // 真实规模: 三级中文目录树, 20 个文件, 内容各异
        var src = Path.Combine(_dir, "项目 资料", "版本 一", "构建 产物");
        Directory.CreateDirectory(src);
        for (int i = 0; i < 20; i++)
            File.WriteAllText(Path.Combine(src, $"文件 {i:00}.txt"), $"内容-{i}-" + new string('字', i * 100));

        var zip = Path.Combine(_dir, "打包 测试.zip");
        _svc.CreateZip(new[] { Path.Combine(_dir, "项目 资料") }, zip);
        Assert.True(new FileInfo(zip).Length > 0);

        // 删源 → 智能解压 → 逐文件字节校验
        Directory.Delete(Path.Combine(_dir, "项目 资料"), true);
        var dest = Path.Combine(_dir, "输出 目录");
        _svc.SmartExtract(zip, dest);
        for (int i = 0; i < 20; i++)
        {
            var p = Path.Combine(dest, "项目 资料", "版本 一", "构建 产物", $"文件 {i:00}.txt");
            Assert.True(File.Exists(p), $"缺失: {p}");
            Assert.Equal($"内容-{i}-" + new string('字', i * 100), File.ReadAllText(p));
        }

        // 完整性测试 + 条目列表与历史落盘贯穿 (条目统计含 3 个目录条目: 20 文件 + 3 目录)
        var (n, b) = _svc.Test(zip);
        Assert.Equal(23, n);
        Assert.True(b > 0);
        Assert.Equal(20, _svc.ListEntries(zip).Count(e => !e.IsDir));

        var hist = new HistoryStore(Path.Combine(_dir, "appdata"));
        hist.Add("解压", zip, dest, true);
        Assert.Single(hist.Load());
    }

    // ---- 健壮性: 故障注入 ----

    [Fact]
    public void 故障注入_损坏压缩包可诊断不悬挂()
    {
        var md = Path.Combine(_dir, "readme.md");
        File.WriteAllText(md, "# TaoZip");
        var zip = Path.Combine(_dir, "good.zip");
        _svc.CreateZip(new[] { md }, zip);

        // 中段截断注入
        var raw = File.ReadAllBytes(zip);
        var bad = Path.Combine(_dir, "bad.zip");
        File.WriteAllBytes(bad, raw.Take(raw.Length / 2).ToArray());
        Assert.ThrowsAny<Exception>(() => _svc.Extract(bad, Path.Combine(_dir, "o1")));
        Assert.ThrowsAny<Exception>(() => _svc.Test(bad));

        // 纯垃圾注入
        var junk = Path.Combine(_dir, "junk.zip");
        File.WriteAllBytes(junk, Encoding.ASCII.GetBytes("THIS-IS-NOT-AN-ARCHIVE-FILE-AT-ALL-1234567890"));
        Assert.ThrowsAny<Exception>(() => _svc.Extract(junk, Path.Combine(_dir, "o2")));

        // 空文件注入
        var empty = Path.Combine(_dir, "empty.zip");
        File.WriteAllBytes(empty, Array.Empty<byte>());
        Assert.ThrowsAny<Exception>(() => _svc.Extract(empty, Path.Combine(_dir, "o3")));
    }

    [Fact]
    public void 故障注入_路径穿越拒绝()
    {
        var evil = Path.Combine(_dir, "evil.zip");
        using (var fs = File.Create(evil))
        using (var zip = new global::System.IO.Compression.ZipArchive(fs, global::System.IO.Compression.ZipArchiveMode.Create))
        {
            var e = zip.CreateEntry("../evil.txt");
            using var es = e.Open();
            es.Write(new byte[] { 1 }, 0, 1);
        }
        Assert.ThrowsAny<Exception>(() => _svc.Extract(evil, Path.Combine(_dir, "out5")));
        Assert.False(File.Exists(Path.Combine(_dir, "evil.txt")), "穿越文件竟被写出");
    }

    // ---- 交付物/构建链完整性检查 ----

    [Fact]
    public void 交付链_build脚本非破坏性产出与编码合规()
    {
        var root = RepoRoot();
        var bat = Path.Combine(root, "build.bat");
        Assert.True(File.Exists(bat));

        // .bat 必须 GBK 可解码且 CRLF
        var raw = File.ReadAllBytes(bat);
        var text = Encoding.GetEncoding("GB18030").GetString(raw);
        Assert.True(!text.Contains('\ufffd'), "build.bat 不是合法 GBK 文本");
        Assert.True(raw.Count(b => b == 0x0D) > 0 && text.Contains('\n'), "build.bat 缺 CRLF");

        // 非破坏性 out 时序: 先建 out.new, 全部成功才替换
        Assert.Contains("out.new", text);
        Assert.Contains("ren out.new out", text);

        // 打包脚本关键自证: 版本行、代码页、离线工具、日志落盘
        var ps1 = File.ReadAllText(Path.Combine(root, "tools", "msi", "package.ps1"), Encoding.UTF8);
        Assert.Contains("打包脚本版本:", ps1);
        Assert.Contains("Codepage=\"936\"", ps1);
        Assert.Contains("-OutDir", ps1);
        Assert.True(File.Exists(Path.Combine(root, "tools", "msi", "wix", "wix.exe")), "离线 WiX 工具缺失");
    }
}

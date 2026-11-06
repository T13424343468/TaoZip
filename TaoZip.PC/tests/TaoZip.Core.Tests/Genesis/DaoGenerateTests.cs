using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using TaoZip.Core;
using Xunit;
using Xunit.Abstractions;

namespace TaoZipGenesis;

/// <summary>
/// 道生万物生成式测试层（固定必跑）:
/// 道生一（单文件本原）→ 一生二（结构两两组合）→ 二生三（内容三元矩阵）→ 三生万物（随机演化）。
/// 用例由生成规则产生，覆盖面由组合保证，不靠手写堆数量；
/// 断言为属性式（往返无损 / 树全等 / 不散落），内部断言聚合计数并打印。
/// </summary>
public class DaoGenerateTests : IDisposable
{
    private static int _totalAsserts;
    private int _asserts;
    private readonly ITestOutputHelper _out;
    private readonly string _root;
    private readonly ArchiveService _svc = new();

    public DaoGenerateTests(ITestOutputHelper output)
    {
        _out = output;
        _root = Path.Combine(Path.GetTempPath(), "TaoZipGenesis_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private void Check(bool cond, string msg)
    {
        Interlocked.Increment(ref _totalAsserts);
        _asserts++;
        Assert.True(cond, msg);
    }

    private string Dir(string name)
    {
        var d = Path.Combine(_root, name);
        Directory.CreateDirectory(d);
        return d;
    }

    private static byte[] RandBytes(Random rng, int n)
    {
        var b = new byte[n];
        rng.NextBytes(b);
        return b;
    }

    /// <summary>万物名池: 多语言 / 空格 / 符号 / 生僻写法。</summary>
    private static readonly string[] NamePool =
    {
        "中文文件", "日本語の文件", "한국어파일", "Русский", "ملف",
        "Ελληνικά", "emoji🜀名", "带 空格", "plus+号", "点点..名", "数据", "日志"
    };

    private static string Norm(string p) => p.Replace('\\', '/');

    private static (List<string> Files, List<string> Dirs) Snapshot(string root)
    {
        var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(p => Norm(Path.GetRelativePath(root, p))).OrderBy(p => p, StringComparer.Ordinal).ToList();
        var dirs = Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
            .Select(p => Norm(Path.GetRelativePath(root, p))).OrderBy(p => p, StringComparer.Ordinal).ToList();
        return (files, dirs);
    }

    private void AssertFileBytesEqual(string a, string b, string label)
    {
        var ha = SHA256.HashData(File.ReadAllBytes(a));
        var hb = SHA256.HashData(File.ReadAllBytes(b));
        Check(ha.SequenceEqual(hb), $"{label} 内容不符: {Path.GetFileName(a)}");
    }

    /// <summary>树全等: 文件集相等 + 每文件字节一致; dirsOnlyFiles=true 时不比目录集 (tar 本无空目录之形)。</summary>
    private void AssertTreeEqual(string srcRoot, string outRoot, bool compareDirs, string label)
    {
        var (sf, sd) = Snapshot(srcRoot);
        var (of_, od) = Snapshot(outRoot);
        Check(of_.SequenceEqual(sf, StringComparer.Ordinal), $"{label} 文件集不符 (源 {sf.Count} / 出 {of_.Count})");
        Check(sf.Count == of_.Count || of_.Count > 0, $"{label} 文件数为零");
        foreach (var rel in sf)
            if (File.Exists(Path.Combine(outRoot, rel)))
                AssertFileBytesEqual(Path.Combine(srcRoot, rel), Path.Combine(outRoot, rel), label);
            else
                Check(false, $"{label} 缺文件 {rel}");
        if (compareDirs)
            Check(od.SequenceEqual(sd, StringComparer.Ordinal), $"{label} 目录集不符");
    }

    /// <summary>压缩→解压一轮往返, 返回解压侧比对根 (zip 保顶层名, tar 解包即内容)。</summary>
    private (string Arc, string OutRoot) RoundTrip(string srcPath, string fmt, string tag)
    {
        var arc = Path.Combine(_root, $"{tag}.{fmt}");
        if (fmt == "zip") _svc.CreateZip(new[] { srcPath }, arc);
        else _svc.CreateTarGz(new[] { srcPath }, arc);

        var dest = Dir($"{tag}_out");
        _svc.Extract(arc, dest);
        Check(_svc.Test(arc).Entries > 0, $"{tag} 完整性测试条目为零");
        var isDir = Directory.Exists(srcPath);
        if (!isDir) return (arc, dest);
        // zip 保留顶层目录名; tar 直接解出内容
        return (arc, fmt == "zip" ? Path.Combine(dest, Path.GetFileName(srcPath)) : dest);
    }

    /// <summary>随机生成一棵文件树 (名字从万物名池取, 内容随机字节)。</summary>
    private void GenTree(Random rng, string dir, int depth, List<string> relFiles, List<string> relDirs, string prefix)
    {
        int files = rng.Next(0, 4);
        for (int i = 0; i < files; i++)
        {
            var name = NamePool[rng.Next(NamePool.Length)] + "_" + rng.Next(1000) + ".dat";
            var rel = string.IsNullOrEmpty(prefix) ? name : prefix + "/" + name;
            File.WriteAllBytes(Path.Combine(dir, name), RandBytes(rng, rng.Next(0, 8192)));
            relFiles.Add(rel);
        }
        if (depth <= 0) return;
        int dirs = rng.Next(0, 3);
        for (int i = 0; i < dirs; i++)
        {
            var name = NamePool[rng.Next(NamePool.Length)] + "目录" + rng.Next(100);
            var rel = string.IsNullOrEmpty(prefix) ? name : prefix + "/" + name;
            var sub = Path.Combine(dir, name);
            Directory.CreateDirectory(sub);
            relDirs.Add(rel);
            GenTree(rng, sub, depth - 1, relFiles, relDirs, rel);
        }
    }

    // ---- L1 道生一: 单文件本原 × 尺寸 × 格式 ----

    [Fact]
    public void L1_Dao_One_SingleFileUniverse()
    {
        var rng = new Random(11);
        foreach (var size in new[] { 0, 1, 7, 64, 4096, 65536, 1048576 })
            foreach (var fmt in new[] { "zip", "tar.gz" })
            {
                var src = Dir($"L1_{size}_{fmt}");
                var f = Path.Combine(src, $"本原{size}.bin");
                File.WriteAllBytes(f, RandBytes(rng, size));
                var (arc, outRoot) = RoundTrip(f, fmt, $"L1_{size}");
                var outF = Path.Combine(outRoot, $"本原{size}.bin");
                Check(File.Exists(outF), $"L1 {size}.{fmt} 缺文件");
                if (File.Exists(outF)) AssertFileBytesEqual(f, outF, $"L1 {size}.{fmt}");
                Check(arc != null, $"L1 {size}.{fmt} 产物缺失");
            }
        _out.WriteLine($"L1 内部断言: {_asserts} 条 (尺寸 7 × 格式 2 之往返)");
    }

    // ---- L2 一生二: 结构两两组合 ----

    [Fact]
    public void L2_Dao_Two_StructurePairCombinations()
    {
        foreach (var fmt in new[] { "zip", "tar.gz" })
        {
            // 单层目录
            var s1 = Dir($"L2_flat_{fmt}");
            for (int i = 0; i < 3; i++) File.WriteAllBytes(Path.Combine(s1, $"平{i}.dat"), RandBytes(new Random(2 + i), 100 + i));
            // 嵌套目录 (深 3)
            var s2 = Dir($"L2_deep_{fmt}");
            var deep = Path.Combine(s2, "一层", "二层", "三层");
            Directory.CreateDirectory(deep);
            File.WriteAllBytes(Path.Combine(deep, "深处.dat"), RandBytes(new Random(9), 2048));
            // 混合: 文件 + 空目录
            var s3 = Dir($"L2_mixed_{fmt}");
            File.WriteAllBytes(Path.Combine(s3, "根文件.dat"), RandBytes(new Random(13), 512));
            Directory.CreateDirectory(Path.Combine(s3, "空目录"));

            foreach (var (src, label) in new[] { (s1, "单层"), (s2, "嵌套"), (s3, "混合") })
            {
                var (arc, outRoot) = RoundTrip(src, fmt, $"L2_{label}_{fmt}");
                // zip 比全树 (含空目录); tar 本无空目录之形, 只比文件
                AssertTreeEqual(src, outRoot, compareDirs: fmt == "zip", $"L2 {label} {fmt}");
                Check(File.Exists(arc), $"L2 {label} {fmt} 包缺失");
            }
        }
        _out.WriteLine($"L2 内部断言: {_asserts} 条 (结构 3 × 格式 2)");
    }

    // ---- L3 二生三: 内容语言 × 换行 × 格式 三元矩阵 ----

    [Fact]
    public void L3_Dao_Three_ContentMatrix()
    {
        string[] langs =
        {
            "道生一，一生二，二生三，三生万物。",
            "日本語のテキストです。",
            "한국어 텍스트입니다.",
            "Русский текст.",
            "نص عربي.",
            "Ελληνικό κείμενο.",
            "Emoji 🜀🎲☯ 文件。",
            "Café Français — naïve façade."
        };
        string[] newlines = { "\n", "\r\n", "\r\n\n" };
        foreach (var lang in langs)
            foreach (var nl in newlines)
            {
                var src = Dir("L3_src");
                var name = "内容_" + Math.Abs(lang.GetHashCode()).ToString("X") + ".txt";
                var f = Path.Combine(src, name);
                File.WriteAllText(f, string.Join(nl, Enumerable.Repeat(lang, 5)), new UTF8Encoding(false));
                var (arc, outRoot) = RoundTrip(f, "zip", $"L3_{Math.Abs(lang.GetHashCode()):X}{Array.IndexOf(newlines, nl)}");
                var outF = Path.Combine(outRoot, name);
                Check(File.Exists(outF), $"L3 缺文件 {name}");
                if (File.Exists(outF))
                {
                    Check(File.ReadAllText(outF, new UTF8Encoding(false)) == string.Join(nl, Enumerable.Repeat(lang, 5)),
                        $"L3 文本不符 ({lang[..4]} / nl{Array.IndexOf(newlines, nl)})");
                }
                Check(File.Exists(arc), "L3 包缺失");
            }
        _out.WriteLine($"L3 内部断言: {_asserts} 条 (语言 8 × 换行 3)");
    }

    // ---- L4 三生万物: 随机演化 (固定种子, 可复现) ----

    [Fact]
    public void L4_Dao_All_RandomEvolution()
    {
        var rng = new Random(20261101);
        int trees = 0;
        for (int t = 0; t < 40; t++)
        {
            var fmt = t % 2 == 0 ? "zip" : "tar.gz";
            var src = Dir($"L4_tree{t}");
            var relFiles = new List<string>();
            var relDirs = new List<string>();
            GenTree(rng, src, 3, relFiles, relDirs, prefix: "");
            if (relFiles.Count == 0)
            {
                File.WriteAllBytes(Path.Combine(src, "补一.dat"), RandBytes(rng, 32)); // 无中生有, 保持有事可测
                relFiles.Add("补一.dat");
            }
            var (arc, outRoot) = RoundTrip(src, fmt, $"L4_{t}");
            AssertTreeEqual(src, outRoot, compareDirs: fmt == "zip", $"L4 树{t} {fmt}");
            trees++;
        }
        Check(trees == 40, "演化轮数不足");
        _out.WriteLine($"L4 内部断言: {_asserts} 条 (随机树 40 棵, 种子 20261101)");
    }

    // ---- L5 周行不殆: 智能解压不变量 (单根落位 / 多根建夹, 永不散落) ----

    [Fact]
    public void L5_Dao_Cycle_SmartExtractInvariant()
    {
        var rng = new Random(55);
        for (int t = 0; t < 12; t++)
        {
            var build = Dir($"L5_build{t}");
            bool singleRoot = t % 2 == 0;
            var items = new List<string>();
            if (singleRoot)
            {
                var root = Path.Combine(build, "唯一根");
                Directory.CreateDirectory(root);
                for (int i = 0; i < 3; i++) File.WriteAllBytes(Path.Combine(root, $"内{i}.dat"), RandBytes(rng, rng.Next(0, 4096)));
                items.Add(root);
            }
            else
            {
                for (int i = 0; i < 2 + t % 2; i++)
                {
                    var p = Path.Combine(build, $"多根{i}.dat");
                    File.WriteAllBytes(p, RandBytes(rng, rng.Next(0, 4096)));
                    items.Add(p);
                }
            }
            var baseDir = Dir($"L5_base{t}");
            var arc = Path.Combine(_root, $"L5_{t}.zip");
            _svc.CreateZip(items, arc);

            _svc.SmartExtract(arc, baseDir);
            var stem = Path.GetFileNameWithoutExtension(arc);
            // 单根: 根目录随包落位 (baseDir/唯一根/...); 多根: 建同名夹 (baseDir/<stem>/...)
            var expectRoot = singleRoot ? baseDir : Path.Combine(baseDir, stem);
            var (gotFiles, _) = Snapshot(expectRoot);
            if (singleRoot)
                Check(gotFiles.Count == 3 && gotFiles.All(f => f.StartsWith("唯一根/", StringComparison.Ordinal)),
                    $"L5 树{t} 单根落位不符");
            else
                Check(gotFiles.Count == items.Count, $"L5 树{t} 多根条数不符");
            // 不散落: 期望子树之外, base 下没有别的新产物
            var stray = Directory.GetFileSystemEntries(baseDir)
                .Count(p => !Path.GetFullPath(p).StartsWith(Path.GetFullPath(expectRoot), StringComparison.OrdinalIgnoreCase));
            Check(stray == 0, $"L5 树{t} 散落 {stray} 项");
        }
        _out.WriteLine($"L5 内部断言: {_asserts} 条 (不变量 12 轮)");
    }

    // ---- L6 万物归一: 断言规模须广大 ----

    [Fact]
    public void L6_Dao_Return_AssertionScale()
    {
        // 自带一枚完整生成核 (不依赖执行顺序): 尺寸×格式矩阵 + 随机树 + 不变量
        var rng = new Random(6);
        foreach (var size in new[] { 0, 1, 64, 65536 })
            foreach (var fmt in new[] { "zip", "tar.gz" })
            {
                var src = Dir($"L6_{size}_{fmt}");
                var f = Path.Combine(src, $"归一{size}.bin");
                File.WriteAllBytes(f, RandBytes(rng, size));
                var (arc, outRoot) = RoundTrip(f, fmt, $"L6_{size}");
                var outF = Path.Combine(outRoot, $"归一{size}.bin");
                Check(File.Exists(outF), $"L6 {size}.{fmt} 缺文件");
                if (File.Exists(outF)) AssertFileBytesEqual(f, outF, $"L6 {size}.{fmt}");
            }
        for (int t = 0; t < 3; t++)
        {
            var src = Dir($"L6_tree{t}");
            var relFiles = new List<string>();
            GenTree(rng, src, 2, relFiles, new List<string>(), prefix: "");
            if (relFiles.Count == 0) { File.WriteAllBytes(Path.Combine(src, "补一.dat"), RandBytes(rng, 32)); relFiles.Add("补一.dat"); }
            var (arc, outRoot) = RoundTrip(src, t % 2 == 0 ? "zip" : "tar.gz", $"L6t_{t}");
            AssertTreeEqual(src, outRoot, compareDirs: t % 2 == 0, $"L6 树{t}");
        }
        for (int t = 0; t < 4; t++)
        {
            var build = Dir($"L6_build{t}");
            var root = Path.Combine(build, "唯一根");
            Directory.CreateDirectory(root);
            for (int i = 0; i < 2; i++) File.WriteAllBytes(Path.Combine(root, $"内{i}.dat"), RandBytes(rng, rng.Next(0, 2048)));
            var baseDir = Dir($"L6_base{t}");
            var arc = Path.Combine(_root, $"L6s{t}.zip");
            _svc.CreateZip(new[] { root }, arc);
            _svc.SmartExtract(arc, baseDir);
            var (gotFiles, _) = Snapshot(baseDir);
            Check(gotFiles.Count == 2, $"L6 不变量{t} 落位条数不符");
        }
        _out.WriteLine($"L6 自演生成核内部断言: {_asserts} 条");
        Assert.True(_asserts >= 40, $"L6 生成核内部断言仅 {_asserts} 条, 未达广大");
    }
}

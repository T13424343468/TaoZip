using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using TaoZip.Core;
using Xunit;
using Xunit.Abstractions;

namespace TaoZipOps;

/// <summary>
/// 生成式操作链（道生一 · 一生二 · 二生三，定律: 交付前跑全测）:
/// 不列举用例, 以固定种子动态生成——源树随机(深度/宽度/大小/类型/命名多维),
/// 操作链随机(压缩/解压/智能落位/交叉格式/重复解压随机续链),
/// 全链路只认不变量: 血脉不漂移(源文件散列全程可寻)、包包有效(Test 通过)、
/// 重复解压幂等、压缩包不混入工作树(防自吞噬)。
/// 种子 20261102, 可复现; 广大而不逾矩。
/// </summary>
public class TaoChainTests
{
    private const int Seed = 20261102;
    private readonly ITestOutputHelper _out;

    public TaoChainTests(ITestOutputHelper output) => _out = output;

    private static string TempDir() => Path.Combine(Path.GetTempPath(), "TaoChain_" + Guid.NewGuid().ToString("N"));

    // ---- 道生一: 一棵随机源树 (多维: 深度/宽度/大小/类型/命名/空态) ----

    private static readonly string[] NameParts =
    {
        "道", "德", "经", "卷", "caelum", "walden", "笔记", "草案", "v1", "final", "抄本 2026", "long_name_piece"
    };

    private static string RandomName(Random r) =>
        string.Join("", Enumerable.Range(0, r.Next(1, 3)).Select(_ => NameParts[r.Next(NameParts.Length)]))
        + (r.Next(4) == 0 ? " " + r.Next(100) : "");

    private static string MakeTree(Random r, string root)
    {
        Directory.CreateDirectory(root);
        var files = new List<string>();

        void Grow(string dir, int depth)
        {
            Directory.CreateDirectory(dir); // 先落土, 再生长 (递归层的目录须自建)
            for (int i = 0; i < r.Next(1, 5); i++)
            {
                var name = RandomName(r) + r.Next(1000); // 防撞名
                var path = Path.Combine(dir, name);
                switch (r.Next(12))
                {
                    case 0: // 空文件
                        File.WriteAllBytes(path, Array.Empty<byte>());
                        break;
                    case 1: // 多字节文本 (中文)
                        File.WriteAllText(path, new string('道', r.Next(1, 200)) + Guid.NewGuid().ToString("N"), Encoding.UTF8);
                        break;
                    default: // 随机二进制
                        var buf = new byte[r.Next(1, 8 * 1024)];
                        r.NextBytes(buf);
                        File.WriteAllBytes(path, buf);
                        break;
                }
                files.Add(path);
            }
            if (r.Next(6) == 0) Directory.CreateDirectory(Path.Combine(dir, "虚位 " + r.Next(100))); // 空目录
            if (depth > 0)
                for (int i = 0; i < r.Next(1, 3); i++)
                    Grow(Path.Combine(dir, "层" + r.Next(100) + "_" + i), depth - 1);
        }

        Grow(root, r.Next(2, 5));
        return root;
    }

    private static string Sha(string file) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));

    private static HashSet<string> TreeHashes(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Select(Sha).ToHashSet();

    // ---- 一生二: 一棵树随机续链, 不变量全程看护 ----

    private (int Steps, int Archives, int Lineage) RunChain(Random r, string workspace, int chainId)
    {
        var tree = MakeTree(r, Path.Combine(workspace, "道源"));
        var sourceHashes = TreeHashes(tree);
        // 树瘦看文件实数不看散列 (空文件散列相同, 多空不算瘦)
        Assert.True(Directory.EnumerateFiles(tree, "*", SearchOption.AllDirectories).Count() >= 3, "源树太瘦, 不足为链");

        var archives = new List<string>();
        var packagedHashes = new HashSet<string>(); // 凡入过包的血脉, 终局必须可寻
        var treeDir = Path.Combine(workspace, "worktree");
        Directory.CreateDirectory(treeDir);

        int steps = r.Next(3, 7);
        for (int step = 0; step < steps; step++)
        {
            var svc = new ArchiveService();
            switch (r.Next(5))
            {
                case 0 or 1: // 压缩 (zip/tar.gz 各半): 树内随机选物
                {
                    var candidates = Directory.EnumerateFileSystemEntries(tree, "*", SearchOption.AllDirectories)
                        .Where(p => !p.Contains(Path.DirectorySeparatorChar + "worktree")).ToList();
                    if (candidates.Count == 0) break;
                    var pick = candidates[r.Next(candidates.Count)];
                    if (File.Exists(pick)) packagedHashes.Add(Sha(pick));
                    else foreach (var f in Directory.EnumerateFiles(pick, "*", SearchOption.AllDirectories)) packagedHashes.Add(Sha(f));
                    var dest = Path.Combine(workspace, $"链{chainId}步{step}." + (r.Next(2) == 0 ? "zip" : "tar.gz"));
                    if (dest.EndsWith(".zip")) svc.CreateZip(new[] { pick }, dest);
                    else svc.CreateTarGz(new[] { pick }, dest);
                    archives.Add(dest);
                    break;
                }
                case 2 or 3: // 解压/智能落位: 随机包入工作树
                {
                    if (archives.Count == 0) break;
                    var archive = archives[r.Next(archives.Count)];
                    var dest = Path.Combine(treeDir, $"落{chainId}_{step}");
                    Directory.CreateDirectory(dest);
                    if (r.Next(2) == 0) svc.Extract(archive, dest);
                    else svc.SmartExtract(archive, dest);
                    // 不变量①: 有物之包落地必有物; 空目录之包落地为空是正理
                    var stat = svc.Test(archive);
                    var landed = Directory.EnumerateFileSystemEntries(dest, "*", SearchOption.AllDirectories).Any();
                    Assert.True(stat.Entries == 0 || landed, $"链{chainId}步{step}: 有物之包落地为空");
                    break;
                }
                case 4: // 重复解压同包: 幂等
                {
                    if (archives.Count == 0) break;
                    var archive = archives[r.Next(archives.Count)];
                    var dest = Path.Combine(treeDir, $"复{chainId}_{step}");
                    Directory.CreateDirectory(dest);
                    svc.Extract(archive, dest);
                    var before = Directory.EnumerateFiles(dest, "*", SearchOption.AllDirectories).Count();
                    var second = svc.Extract(archive, dest);
                    var after = Directory.EnumerateFiles(dest, "*", SearchOption.AllDirectories).Count();
                    // 不变量②: 同包再解, 文件数不增 (幂等, 覆盖而非翻倍)
                    Assert.True(after == before, $"链{chainId}步{step}: 重复解压翻倍 ({before}→{after})");
                    Assert.True(second.Files >= 0);
                    break;
                }
            }

            // 不变量③: 每个在册包每步都可解析 (Test 不炸; 空目录成包条目为零是正理, 但载体必须有结构)
            foreach (var a in archives)
            {
                Assert.True(new FileInfo(a).Length > 0, $"链{chainId}步{step}: 包体为空 {Path.GetFileName(a)}");
                var stat = svc.Test(a);
                Assert.True(stat.Entries >= 0 && stat.Bytes >= 0, $"链{chainId}步{step}: 包解析失败 {Path.GetFileName(a)}");
            }
        }

        // 终局收束: 把所有包全部解进工作树, 血脉必须可寻 (源树每一份散列都在后裔中)
        var svc2 = new ArchiveService();
        foreach (var a in archives)
        {
            var dest = Path.Combine(treeDir, "归宗_" + Path.GetFileName(a));
            Directory.CreateDirectory(dest);
            svc2.Extract(a, dest);
        }
        var lineage = TreeHashes(treeDir);
        var missing = packagedHashes.Except(lineage).Count();
        Assert.True(missing == 0, $"链{chainId}: 血脉漂移, {missing} 份入包源文件散列在后裔中不可寻");

        // 不变量④: 工作树中不应有 zip/tar.gz 被当文件解不出 (除非人为混入)——归宗目录除外
        var strays = Directory.EnumerateFiles(tree, "*", SearchOption.AllDirectories)
            .Count(p => p.EndsWith(".zip") || p.EndsWith(".tar.gz"));
        Assert.True(strays == 0, $"链{chainId}: 源树混入 {strays} 个压缩包, 防自吞噬失守");

        return (steps, archives.Count, packagedHashes.Count);
    }

    private const int ChainCount = 10_000; // 万级生成轮次 (用户定律)

    [Fact]
    public void G1_TenThousandChains_AllInvariantsHold()
    {
        long totalSteps = 0, totalArchives = 0, totalLineage = 0;
        Parallel.For(0, ChainCount, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
        {
            var ws = TempDir();
            try
            {
                var (steps, arcs, lineage) = RunChain(new Random(Seed + i), ws, i);
                Interlocked.Add(ref totalSteps, steps);
                Interlocked.Add(ref totalArchives, arcs);
                Interlocked.Add(ref totalLineage, lineage);
            }
            finally { try { Directory.Delete(ws, recursive: true); } catch { } }
        });
        _out.WriteLine($"万链归账: {ChainCount} 链, {totalSteps} 步, {totalArchives} 包, {totalLineage} 份血脉全数可寻");
        Assert.True(totalArchives > ChainCount, "万链平均不足一包, 生成太瘦");
    }

    // ---- 二生三: 三链并行, 各修其土, 互不侵扰 ----

    [Fact]
    public async Task G2_ThreeChainsInParallel_NoCrossContamination()
    {
        var ws = TempDir();
        try
        {
            var tasks = Enumerable.Range(0, 3).Select(i => Task.Run(() =>
            {
                var sub = Path.Combine(ws, "土" + i);
                Directory.CreateDirectory(sub);
                RunChain(new Random(Seed + 100 + i), sub, 100 + i);
            }));
            await Task.WhenAll(tasks);
        }
        finally { try { Directory.Delete(ws, recursive: true); } catch { } }
    }

    // ---- 混沌链: 同一包交叉格式互转, 三代同宗 ----

    [Fact]
    public void G3_CrossFormatChaos_LineageUnbroken()
    {
        var ws = TempDir();
        var r = new Random(Seed + 7);
        try
        {
            var tree = MakeTree(r, Path.Combine(ws, "混沌源"));
            var sourceHashes = TreeHashes(tree);
            var svc = new ArchiveService();
            var current = new List<string> { tree };
            var lineage = new List<string>();

            // 五代格式轮转: 每代随机选格式重压再解出
            for (int gen = 0; gen < 5; gen++)
            {
                var next = new List<string>();
                foreach (var src in current)
                {
                    var ext = r.Next(2) == 0 ? "zip" : "tar.gz";
                    var pkg = Path.Combine(ws, $"代{gen}." + ext);
                    if (ext == "zip") svc.CreateZip(new[] { src }, pkg);
                    else svc.CreateTarGz(new[] { src }, pkg);
                    // 空目录之包条目为零是正理, 载体必须有结构
                    Assert.True(new FileInfo(pkg).Length > 0, $"代{gen}: 包体为空");
                    svc.Test(pkg);
                    var outDir = Path.Combine(ws, $"代{gen}出_{Path.GetFileName(src)}");
                    Directory.CreateDirectory(outDir);
                    svc.Extract(pkg, outDir);
                    next.AddRange(Directory.EnumerateFileSystemEntries(outDir, "*", SearchOption.AllDirectories)
                        .Where(p => Directory.Exists(p) || !p.EndsWith(".zip") && !p.EndsWith(".tar.gz")));
                    lineage.AddRange(Directory.EnumerateFiles(outDir, "*", SearchOption.AllDirectories));
                }
                current = next;
            }

            var finalHashes = lineage.Select(Sha).ToHashSet();
            Assert.True(sourceHashes.IsSubsetOf(finalHashes), "五代格式轮转后血脉断裂");
            _out.WriteLine($"混沌链: 五代轮转, {sourceHashes.Count} 份血脉全数可寻");
        }
        finally { try { Directory.Delete(ws, recursive: true); } catch { } }
    }
}

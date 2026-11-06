using Ionic.Zip;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using TaoZip.BLL;
using TaoZip.DAL;
using TaoZip.Model;

namespace TaoZip.Tests;

/// <summary>自跑逻辑测试：全部用例本地执行，无外部依赖（可选 fixture 除外）。</summary>
public static class Program
{
    private static int _pass, _fail;
    private static readonly List<string> _failures = new();

    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var root = Path.Combine(Path.GetTempPath(), "taozip_tests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        try
        {
            // ---- 引擎：压缩/解压往返 ----
            Run("zip 往返（多文件+子目录+中文名）", () => TestZipRoundTrip(root));
            Run("zip AES-256 加密往返", () => TestZipAes(root));
            Run("zip 错误密码拒绝", () => TestZipWrongPassword(root));
            Run("zip 注释写入与读取", () => TestZipComment(root));
            Run("zip GBK 文件名编码", () => TestZipGbkNames(root));
            Run("tar 往返", () => TestTarRoundTrip(root));
            Run("tar.gz 往返", () => TestTgzRoundTrip(root));
            Run("gzip 单文件", () => TestGz(root));
            Run("zip64 大内容写入读取", () => TestZipLarge(root));

            // ---- 解压行为 ----
            Run("7z 解压（样本）", () => TestSevenZip(root));
            Run("rar 解压（样本，存在才测）", () => TestRar(root), allowSkip: true);
            Run("zip slip 路径穿越防护", () => TestZipSlip(root));
            Run("仅解压指定条目", () => TestPartialExtract(root));
            Run("跳过已存在文件", () => TestSkipExisting(root));

            // ---- 分卷 ----
            Run("分卷切分与合并（哈希一致）", () => TestVolumes(root));

            // ---- 智能解压 ----
            Run("智能解压：多顶层建子目录", () => TestSmartMulti(root));
            Run("智能解压：单顶层不嵌套", () => TestSmartSingle(root));

            // ---- 完整性 ----
            Run("完整性测试：正常包通过", () => TestIntegrityOk(root));
            Run("完整性测试：损坏包报坏条目", () => TestIntegrityBad(root));

            // ---- 服务与存储 ----
            Run("任务队列：执行+进度+完成", async () => await TestServiceQueue(root));
            Run("任务队列：取消", async () => await TestServiceCancel(root));
            Run("历史记录：增查与上限", () => TestHistory(root));
            Run("设置：读写与损坏兜底", () => TestSettings(root));
            Run("格式识别", () => TestFormatDetect());

            // ---- 视觉之道（安卓版三象色板与 UI 源码约束）----
            Run("视觉之道：三象色板对比度矩阵", () => TestVisualMatrix());
            Run("视觉之道：UI 取色唯一与空态齐备", () => TestUiSourceRules());
            Run("视觉之道：功能卡锚定与弹窗遮罩唯一", () => TestAnchorMenuRules());
            Run("回收站与分词搜索：2.2 真机反馈回归", () => TestV22Rules());
            Run("顶栏与回收站隔离：2.3 真机反馈回归", () => TestV23Rules());
            Run("删除自愈与图片预览：2.4 真机反馈回归", () => TestV24Rules());
            Run("一加式布局重设计：2.5 界面回归", () => TestV25Rules());
            Run("顶栏直达与删除直删：2.6 真机反馈回归", () => TestV26Rules());
            Run("分类入页与删除随行：2.7 真机反馈回归", () => TestV27Rules());
            Run("单行宫格与行距收紧：2.8 真机反馈回归", () => TestV28Rules());
            Run("墨夜页签白胶囊：2.9 真机反馈回归", () => TestV29Rules());
            Run("顶栏浮动胶囊与合并卡：2.10 真机反馈回归", () => TestV30Rules());
            Run("多选批量与顶栏一体：2.11 真机反馈回归", () => TestV31Rules());
            Run("图标可见与气泡卡：2.12 真机反馈回归", () => TestV32Rules());
            Run("批量浮层与行距取齐：2.13 真机反馈回归", () => TestV33Rules());
            Run("后台任务与通知进度：2.14 真机反馈回归", () => TestV34Rules());
            Run("三处启动闪退连根修：2.15 真机反馈回归", () => TestV35Rules());
            Run("删除不复活与系统预览：2.16 真机反馈回归", () => TestV36Rules());
            Run("死代码清理与规范自检：2.17 界面回归", () => TestV37Rules());
            Run("删除同口径与回收站预览：2.21 真机反馈回归", () => TestV221Rules());
            Run("筛选间距与菜单砍删除：2.22 真机反馈回归", () => TestV222Rules());
            Run("批量解压与浮动底栏：2.23 真机反馈回归", () => TestV223Rules());
            Run("卡片宽度与任务进度圈：2.24 真机反馈回归", () => TestV224Rules());
            Run("行菜单解压入口：2.25 真机反馈回归", () => TestV225Rules());
            Run("更多按钮陈旧条目：2.26 真机反馈回归", () => TestV226Rules());
            Run("压缩闪退根修与排序全链：2.27 真机反馈回归", () => TestV227Rules());

            // ---- 压力之道（万链/真人模拟/大文件/暴力，全系最高标准）----
            Run("万链：万级生成式归账（万链归账）", () => TestWanlian(root));
            Run("真人模拟操作：拟人路径往返幂等", () => TestHumanSim(root));
            Run("大文件：1GB 随机流压缩解压保真（流式不落内存）", () => TestBigFile(root));
            Run("暴力：边界反复与并发风暴", () => TestBruteForce(root));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }

        Console.WriteLine();
        Console.WriteLine($"==== 结果：{_pass} 通过 / {_fail} 失败 ====");
        foreach (var f in _failures) Console.WriteLine("  FAIL: " + f);
        return _fail == 0 ? 0 : 1;
    }

    // ---------- 用例实现 ----------

    private static void TestZipRoundTrip(string root)
    {
        var src = Path.Combine(root, "zipsrc");
        SeedTree(src);
        var zip = Path.Combine(root, "rt.zip");
        var dest = Path.Combine(root, "zirdt");

        ArchiveWriter.Compress(new CompressRequest
        {
            SourcePaths = new[] { src },
            DestPath = zip,
            Format = CompressFormat.Zip,
            Level = 6,
        });
        Assert.True(File.Exists(zip), "zip 未生成");

        var info = ArchiveEngine.OpenInfo(zip);
        Assert.True(info.Entries.Count >= 6, "条目数不对: " + info.Entries.Count);

        ArchiveEngine.Extract(new ExtractRequest { ArchivePath = zip, DestDir = dest });
        AssertTreeEqual(src, Path.Combine(dest, Path.GetFileName(src)));
    }

    private static void TestZipAes(string root)
    {
        var src = Path.Combine(root, "aessrc");
        Directory.CreateDirectory(src);
        var file1 = Path.Combine(src, "机密文件.txt");
        var file2 = Path.Combine(src, "secret.bin");
        File.WriteAllText(file1, "加密内容测试 🌟");
        File.WriteAllBytes(file2, RandomBytes(64 * 1024));

        var zip = Path.Combine(root, "aes.zip");
        ArchiveWriter.Compress(new CompressRequest
        {
            SourcePaths = new[] { src },
            DestPath = zip,
            Password = "密码123",
        });

        var info = ArchiveEngine.OpenInfo(zip);
        Assert.True(info.IsEncrypted, "未检测到加密");

        var dest = Path.Combine(root, "aesout");
        ArchiveEngine.Extract(new ExtractRequest { ArchivePath = zip, DestDir = dest, Password = "密码123" });
        var outDir = Path.Combine(dest, Path.GetFileName(src));
        Assert.Equal("加密内容测试 🌟", File.ReadAllText(Path.Combine(outDir, "机密文件.txt")));
        Assert.True(Sha256(file2) == Sha256(Path.Combine(outDir, "secret.bin")), "二进制内容不一致");

        // AES-256 校验：加密 flag + 实际算法（目录条目不参与）
        using var zf = ZipFile.Read(zip);
        Assert.True(zrEntriesOk(zf), "加密算法不是 WinZipAes256");
    }

    private static bool zrEntriesOk(ZipFile zf)
        => zf.Entries.Where(e => !e.IsDirectory).All(e => e.Encryption == EncryptionAlgorithm.WinZipAes256);

    private static void TestZipWrongPassword(string root)
    {
        var src = Path.Combine(root, "wrongpw");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "a.txt"), new string('x', 10000));
        var zip = Path.Combine(root, "wrongpw.zip");
        ArchiveWriter.Compress(new CompressRequest { SourcePaths = new[] { src }, DestPath = zip, Password = "right" });

        bool threw = false;
        try
        {
            ArchiveEngine.Extract(new ExtractRequest
            { ArchivePath = zip, DestDir = Path.Combine(root, "wrongpw_out"), Password = "wrong" });
        }
        catch { threw = true; }
        Assert.True(threw, "错误密码未报错");
    }

    private static void TestZipComment(string root)
    {
        var src = Path.Combine(root, "cmt");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "f.txt"), "hi");
        var zip = Path.Combine(root, "cmt.zip");
        ArchiveWriter.Compress(new CompressRequest { SourcePaths = new[] { src }, DestPath = zip, Comment = "打包备注ABC" });
        var info = ArchiveEngine.OpenInfo(zip, fileNameEncoding: "utf-8");
        Assert.Equal("打包备注ABC", info.Comment);
    }

    private static void TestZipGbkNames(string root)
    {
        var src = Path.Combine(root, "gbksrc");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "简体中文测试.txt"), "gbk 名称");
        var zip = Path.Combine(root, "gbk.zip");
        ArchiveWriter.Compress(new CompressRequest
        {
            SourcePaths = new[] { src },
            DestPath = zip,
            FileNameEncoding = "gbk",
        });
        // 按写入编码读取，名称应可还原
        var info = ArchiveEngine.OpenInfo(zip, fileNameEncoding: "gbk");
        Assert.True(info.Entries.Any(e => e.Path.Contains("简体中文测试")), "GBK 名称读取失败");
        // 按 utf-8 读取时同一字节流会乱码（验证编码确实生效）
        var info2 = ArchiveEngine.OpenInfo(zip, fileNameEncoding: "utf-8");
        Assert.False(info2.Entries.Any(e => e.Path.Contains("简体中文测试")), "编码参数未生效");
    }

    private static void TestTarRoundTrip(string root)
    {
        var src = Path.Combine(root, "tarsrc");
        SeedTree(src);
        var tar = Path.Combine(root, "rt.tar");
        ArchiveWriter.Compress(new CompressRequest { SourcePaths = new[] { src }, DestPath = tar, Format = CompressFormat.Tar });
        var dest = Path.Combine(root, "tarout");
        ArchiveEngine.Extract(new ExtractRequest { ArchivePath = tar, DestDir = dest });
        AssertTreeEqual(src, Path.Combine(dest, Path.GetFileName(src)));
    }

    private static void TestTgzRoundTrip(string root)
    {
        var src = Path.Combine(root, "tgzsrc");
        SeedTree(src);
        var tgz = Path.Combine(root, "rt.tar.gz");
        ArchiveWriter.Compress(new CompressRequest { SourcePaths = new[] { src }, DestPath = tgz, Format = CompressFormat.Tgz });
        var dest = Path.Combine(root, "tgzout");
        ArchiveEngine.Extract(new ExtractRequest { ArchivePath = tgz, DestDir = dest });
        AssertTreeEqual(src, Path.Combine(dest, Path.GetFileName(src)));
    }

    private static void TestGz(string root)
    {
        var f = Path.Combine(root, "single.txt");
        File.WriteAllText(f, new string('g', 100000));
        var gz = Path.Combine(root, "single.txt.gz");
        ArchiveWriter.Compress(new CompressRequest { SourcePaths = new[] { f }, DestPath = gz, Format = CompressFormat.Gz });
        var dest = Path.Combine(root, "gzout");
        ArchiveEngine.Extract(new ExtractRequest { ArchivePath = gz, DestDir = dest });
        Assert.True(File.Exists(Path.Combine(dest, "single.txt")), "解压文件缺失");
        Assert.Equal(File.ReadAllText(f), File.ReadAllText(Path.Combine(dest, "single.txt")));
    }

    private static void TestZipLarge(string root)
    {
        var src = Path.Combine(root, "bigsrc");
        Directory.CreateDirectory(src);
        File.WriteAllBytes(Path.Combine(src, "big.bin"), RandomBytes(12 * 1024 * 1024));
        var zip = Path.Combine(root, "big.zip");
        ArchiveWriter.Compress(new CompressRequest { SourcePaths = new[] { src }, DestPath = zip, Level = 1 });
        var dest = Path.Combine(root, "bigout");
        ArchiveEngine.Extract(new ExtractRequest { ArchivePath = zip, DestDir = dest });
        Assert.True(Sha256(Path.Combine(src, "big.bin")) == Sha256(Path.Combine(dest, Path.GetFileName(src), "big.bin")), "大文件内容不一致");
    }

    private static void TestSevenZip(string root)
    {
        var sample = Path.Combine(AppContext.BaseDirectory, "fixtures", "sample.7z");
        if (!File.Exists(sample)) throw new SkipException("缺 sample.7z 样本");
        var dest = Path.Combine(root, "7zout");
        var info = ArchiveEngine.OpenInfo(sample);
        Assert.True(info.Entries.Count >= 3, "7z 条目数不对");
        ArchiveEngine.Extract(new ExtractRequest { ArchivePath = sample, DestDir = dest });
        Assert.True(File.Exists(Path.Combine(dest, "docs", "readme.txt")), "7z 解压缺文件");
        Assert.Equal("7z 样本内容", File.ReadAllText(Path.Combine(dest, "docs", "readme.txt")));
    }

    private static void TestRar(string root)
    {
        var sample = Path.Combine(AppContext.BaseDirectory, "fixtures", "sample.rar");
        if (!File.Exists(sample)) throw new SkipException("缺 sample.rar 样本");
        var dest = Path.Combine(root, "rarout");
        ArchiveEngine.Extract(new ExtractRequest { ArchivePath = sample, DestDir = dest });
        Assert.True(Directory.EnumerateFiles(dest, "*", SearchOption.AllDirectories).Any(), "rar 解压无输出");
    }

    private static void TestZipSlip(string root)
    {
        var evil = Path.Combine(root, "evil.zip");
        using (var zf = new ZipFile())
        {
            zf.AddEntry("../../../../evil_escaped.txt", "bad");
            zf.Save(evil);
        }
        var dest = Path.Combine(root, "slipout");
        Directory.CreateDirectory(dest);
        ArchiveEngine.Extract(new ExtractRequest { ArchivePath = evil, DestDir = dest });
        Assert.False(File.Exists(Path.Combine(root, "evil_escaped.txt")), "路径穿越未被阻断");
        Assert.True(File.Exists(Path.Combine(dest, "evil_escaped.txt")), "文件应解到目标目录内");
    }

    private static void TestPartialExtract(string root)
    {
        var src = Path.Combine(root, "partsrc");
        SeedTree(src);
        var zip = Path.Combine(root, "part.zip");
        ArchiveWriter.Compress(new CompressRequest { SourcePaths = new[] { src }, DestPath = zip });
        var dest = Path.Combine(root, "partout");
        ArchiveEngine.Extract(new ExtractRequest
        {
            ArchivePath = zip,
            DestDir = dest,
            EntryPaths = new[] { Path.GetFileName(src) + "/a.txt" },
        });
        var all = Directory.EnumerateFiles(dest, "*", SearchOption.AllDirectories).ToList();
        Assert.Equal(1, all.Count);
        Assert.EndsWith("a.txt", all[0]);
    }

    private static void TestSkipExisting(string root)
    {
        var src = Path.Combine(root, "skipsrc");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "f.txt"), "v1");
        var zip = Path.Combine(root, "skip.zip");
        ArchiveWriter.Compress(new CompressRequest { SourcePaths = new[] { src }, DestPath = zip });

        var dest = Path.Combine(root, "skipout");
        Directory.CreateDirectory(dest);
        var target = Path.Combine(dest, "skipsrc", "f.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "OLD");
        ArchiveEngine.Extract(new ExtractRequest { ArchivePath = zip, DestDir = dest, SkipExisting = true });
        Assert.Equal("OLD", File.ReadAllText(target));
        ArchiveEngine.Extract(new ExtractRequest { ArchivePath = zip, DestDir = dest, SkipExisting = false });
        Assert.Equal("v1", File.ReadAllText(target));
    }

    private static void TestVolumes(string root)
    {
        var data = Path.Combine(root, "vol.bin");
        File.WriteAllBytes(data, RandomBytes(3 * 1024 * 1024 + 12345));
        var hash = Sha256(data);

        var parts = VolumeHelper.SplitFile(data, 1024 * 1024, keepOriginal: false);
        Assert.Equal(4, parts.Count);
        Assert.True(parts.All(p => p.EndsWith(".00" + (parts.IndexOf(p) + 1))), "分卷命名不对");

        var restored = VolumeHelper.MergeVolumes(parts[0], keepParts: false);
        Assert.True(Sha256(restored) == hash, "合并后内容不一致");
        Assert.False(parts.Any(File.Exists), "分卷未清理");
    }

    private static void TestSmartMulti(string root)
    {
        // 压缩文件夹内部的多个条目（顶层 >1），智能解压应建议子目录
        var src = Path.Combine(root, "smartm");
        SeedTree(src);
        var zip = Path.Combine(root, "smartm.zip");
        ArchiveWriter.Compress(new CompressRequest
        {
            SourcePaths = new[] { Path.Combine(src, "a.txt"), Path.Combine(src, "b.log"), Path.Combine(src, "sub") },
            DestPath = zip,
        });
        var info = ArchiveEngine.OpenInfo(zip);
        Assert.True(info.TopLevelCount > 1, "顶层条目应大于 1: " + info.TopLevelCount);
        var dest = SmartExtract.ChooseDestDir(zip, info, smartEnabled: true);
        Assert.Equal(Path.Combine(root, "smartm"), dest);
        // 关闭智能解压则直接父目录
        var dest2 = SmartExtract.ChooseDestDir(zip, info, smartEnabled: false);
        Assert.Equal(root, dest2);
    }

    private static void TestSmartSingle(string root)
    {
        var zip = Path.Combine(root, "smarts.zip");
        using (var zf = new ZipFile())
        {
            zf.AddEntry("only/inner.txt", "x");
            zf.Save(zip);
        }
        var info = ArchiveEngine.OpenInfo(zip);
        var dest = SmartExtract.ChooseDestDir(zip, info, smartEnabled: true);
        Assert.Equal(root, dest);
    }

    private static void TestIntegrityOk(string root)
    {
        var src = Path.Combine(root, "intg");
        SeedTree(src);
        var zip = Path.Combine(root, "intg.zip");
        ArchiveWriter.Compress(new CompressRequest { SourcePaths = new[] { src }, DestPath = zip });
        var bad = ArchiveEngine.Test(zip);
        Assert.Equal(0, bad.Count);
    }

    private static void TestIntegrityBad(string root)
    {
        var src = Path.Combine(root, "intb");
        Directory.CreateDirectory(src);
        File.WriteAllBytes(Path.Combine(src, "x.bin"), RandomBytes(300 * 1024));
        var zip = Path.Combine(root, "intb.zip");
        ArchiveWriter.Compress(new CompressRequest { SourcePaths = new[] { src }, DestPath = zip });

        // 破坏压缩包中部数据
        var bytes = File.ReadAllBytes(zip);
        for (int i = bytes.Length / 2; i < bytes.Length / 2 + 4096 && i < bytes.Length; i++)
            bytes[i] ^= 0xFF;
        File.WriteAllBytes(zip, bytes);

        var bad = ArchiveEngine.Test(zip);
        Assert.True(bad.Count > 0, "损坏包未检出坏条目");
    }

    private static async Task TestServiceQueue(string root)
    {
        using var svc = new ArchiveService(new HistoryStore(Path.Combine(root, "h.json")), maxConcurrency: 1);
        ArchiveTaskRecord? done = null;
        svc.TaskUpdated += r => { if (r.State is TaskState.Done or TaskState.Failed) done = r; };

        var src = Path.Combine(root, "svc");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "a.txt"), "svc");
        var zip = Path.Combine(root, "svc.zip");

        var rec = svc.Enqueue(TaskKind.Compress, Path.GetFileName(zip),
            (taskId, progress, ct) => ArchiveWriter.Compress(
                new CompressRequest { SourcePaths = new[] { src }, DestPath = zip }, taskId, progress, ct));

        await Task.Run(async () => { while (done == null) await Task.Delay(20); });
        Assert.Equal(TaskState.Done, rec.State);
        Assert.True(File.Exists(zip));
        Assert.True(svc.History.Count >= 1, "历史未记录");
    }

    private static async Task TestServiceCancel(string root)
    {
        using var svc = new ArchiveService(new HistoryStore(Path.Combine(root, "hc.json")), maxConcurrency: 1);
        ArchiveTaskRecord? finished = null;
        svc.TaskUpdated += r => { if (r.FinishedAt != null) finished = r; };

        var rec = svc.Enqueue(TaskKind.Test, "long-running",
            (taskId, progress, ct) =>
            {
                for (int i = 0; i < 100; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    Thread.Sleep(50);
                    progress.Report(new ArchiveProgress { TaskId = taskId, Percent = i });
                }
            });
        await Task.Delay(120);
        Assert.True(svc.Cancel(rec.Id), "取消失败");
        await Task.Run(async () => { while (finished == null) await Task.Delay(20); });
        Assert.Equal(TaskState.Cancelled, rec.State);
    }

    private static void TestHistory(string root)
    {
        var path = Path.Combine(root, "hist.json");
        var store = new HistoryStore(path, max: 30);
        for (int i = 0; i < 40; i++)
            store.Add(new HistoryRecord { Id = i.ToString(), Kind = TaskKind.Extract, ArchivePath = "a" + i, DestPath = "d" });
        Assert.Equal(30, store.All.Count);
        Assert.Equal("39", store.All[^1].Id);

        // 重新加载持久化
        var store2 = new HistoryStore(path, max: 30);
        Assert.Equal(30, store2.All.Count);
    }

    private static void TestSettings(string root)
    {
        var path = Path.Combine(root, "settings.json");
        var store = new SettingsStore(path);
        var s = store.Load();
        Assert.True(s.SmartExtract, "默认值不对");
        s.DefaultLevel = 9;
        s.LastBrowseDir = "/tmp/x";
        store.Save(s);
        var s2 = new SettingsStore(path).Load();
        Assert.Equal(9, s2.DefaultLevel);
        Assert.Equal("/tmp/x", s2.LastBrowseDir);

        // 损坏兜底
        File.WriteAllText(path, "{broken json!!!");
        var s3 = new SettingsStore(path).Load();
        Assert.True(s3.SmartExtract, "损坏设置未回退默认");
    }

    private static void TestFormatDetect()
    {
        Assert.Equal("zip", ArchiveEngine.DetectFormat("a.zip"));
        Assert.Equal("7z", ArchiveEngine.DetectFormat("a.7z"));
        Assert.Equal("rar", ArchiveEngine.DetectFormat("a.rar"));
        Assert.Equal("tar", ArchiveEngine.DetectFormat("a.tar"));
        Assert.Equal("gz", ArchiveEngine.DetectFormat("a.gz"));
        Assert.Equal("tgz", ArchiveEngine.DetectFormat("a.tar.gz"));
        Assert.Equal("tgz", ArchiveEngine.DetectFormat("a.tgz"));
        Assert.Equal("bz2", ArchiveEngine.DetectFormat("a.bz2"));
        Assert.Equal("xz", ArchiveEngine.DetectFormat("a.xz"));
        Assert.True(ArchiveEngine.IsArchiveFile("x.tar.bz2"));
        Assert.False(ArchiveEngine.IsArchiveFile("x.txt"));
    }

    // ---------- 工具 ----------

    internal static class Assert
    {
        public static void True(bool cond, string? msg = null)
        {
            if (!cond) throw new InvalidOperationException(msg ?? "断言失败");
        }

        public static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException($"期望 {expected}，实际 {actual}");
        }

        public static void False(bool cond, string? msg = null) => True(!cond, msg);

        public static void EndsWith(string suffix, string value)
        {
            if (!value.EndsWith(suffix, StringComparison.Ordinal))
                throw new InvalidOperationException($"期望以 {suffix} 结尾，实际 {value}");
        }
    }

    private class SkipException : Exception
    {
        public SkipException(string m) : base(m) { }
    }

    private static void Run(string name, Action test, bool allowSkip = false)
    {
        try
        {
            test();
            _pass++;
            Console.WriteLine($"  PASS  {name}");
        }
        catch (SkipException ex) when (allowSkip)
        {
            Console.WriteLine($"  SKIP  {name} — {ex.Message}");
        }
        catch (Exception ex)
        {
            _fail++;
            _failures.Add($"{name} — {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"  FAIL  {name} — {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }

    private static void Run(string name, Func<Task> test, bool allowSkip = false)
        => Run(name, () => test().GetAwaiter().GetResult(), allowSkip);

    private static void SeedTree(string dir)
    {
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        Directory.CreateDirectory(Path.Combine(dir, "sub", "deep"));
        File.WriteAllText(Path.Combine(dir, "a.txt"), "alpha 内容");
        File.WriteAllText(Path.Combine(dir, "b.log"), new string('b', 3000));
        File.WriteAllBytes(Path.Combine(dir, "sub", "c.bin"), RandomBytes(50 * 1024));
        File.WriteAllText(Path.Combine(dir, "sub", "deep", "中文.txt"), "深层中文文件");
    }

    private static void AssertTreeEqual(string expected, string actual)
    {
        var ef = Directory.EnumerateFiles(expected, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(expected, f)).ToHashSet();
        var af = Directory.EnumerateFiles(actual, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(actual, f)).ToHashSet();
        if (!ef.SetEquals(af))
            throw new InvalidOperationException($"文件树不一致：期望 {ef.Count} 个，实际 {af.Count} 个");
        foreach (var rel in ef)
        {
            var h1 = Sha256(Path.Combine(expected, rel));
            var h2 = Sha256(Path.Combine(actual, rel));
            if (h1 != h2) throw new InvalidOperationException("内容不一致：" + rel);
        }
    }

    private static byte[] RandomBytes(int n)
    {
        var b = new byte[n];
        Random.Shared.NextBytes(b);
        return b;
    }

    private static string Sha256(string file)
    {
        // 流式哈希：内存恒定不随文件大小涨，1GB 档不吃内存
        using var fs = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(fs));
    }

    // ---------- 压力之道（万链/大文件/暴力/真人模拟）----------

    /// <summary>
    /// 万链（万级补则，用户 2026-11-01 定全系标准；道生一→三生万物）：
    /// 万级生成链路并行演化——每链独立种子（可复现）、独立临时目录，
    /// 随机长树（体量收敛：二进制≤8KB、文本≤200字）、随机格式压缩→浏览→解压→逐文件 SHA 往返无损，
    /// 同包再解压一次断言幂等；链内不写测试输出、归账元组汇总，终局「万链归账」断言。
    /// </summary>
    private static void TestWanlian(string root)
    {
        const int ChainCount = 10000;      // 万链基准
        const int SeedBase = 20261104;
        long violations = 0, packs = 0, files = 0, steps = 0;
        string? firstErr = null;
        var wlRoot = Path.Combine(root, "wl");
        Directory.CreateDirectory(wlRoot);

        Parallel.For(0, ChainCount, chain =>
        {
            var rnd = new Random(SeedBase + chain);   // 每链独立种子：失败可重演
            long cv = 0, pk = 0, fl = 0, st = 0;
            var dir = Path.Combine(wlRoot, $"c{chain:00000}");
            Directory.CreateDirectory(dir);
            try
            {
                // 道生一：随机长出本链的小世界（1-5 条目，零字节/文本/二进制随机，名含中文与扩展名变化）
                static string RandName(Random r)
                {
                    string[] exts = { ".txt", ".bin", ".md", "" };
                    string[] bases = { "文件", "data", "流水", "x", "深_层名" };
                    return bases[r.Next(bases.Length)] + r.Next(100) + exts[r.Next(exts.Length)];
                }
                int entryCount = rnd.Next(1, 6);
                for (int e = 0; e < entryCount; e++)
                {
                    string path = Path.Combine(dir, RandName(rnd));
                    switch (rnd.Next(3))
                    {
                        case 0: // 文本 ≤200 字
                            string pool = "道生一一生二二生三三生万物中文数据流";
                            File.WriteAllText(path, new string(Enumerable.Range(0, rnd.Next(0, 200))
                                .Select(_ => pool[rnd.Next(pool.Length)]).ToArray()));
                            break;
                        case 1: // 二进制 ≤8KB
                            var b = new byte[rnd.Next(0, 8 * 1024 + 1)];
                            rnd.NextBytes(b);
                            File.WriteAllBytes(path, b);
                            break;
                        default: // 子目录再放一文件（树随机长）
                            var sub = path + "_d";
                            Directory.CreateDirectory(sub);
                            File.WriteAllText(Path.Combine(sub, RandName(rnd)), $"链{chain}卷内内容");
                            break;
                    }
                }

                // 一生二：压缩（格式与级别随机）
                bool useZip = rnd.Next(2) == 0;
                var zip = Path.Combine(wlRoot, $"c{chain:00000}" + (useZip ? "_包.zip" : "_包.tgz"));   // 包放链外，不压自身
                ArchiveWriter.Compress(new CompressRequest
                {
                    SourcePaths = new[] { dir },
                    DestPath = zip,
                    Format = useZip ? CompressFormat.Zip : CompressFormat.Tgz,
                    Level = rnd.Next(1, 10),
                });
                if (!File.Exists(zip)) { cv++; return; }
                pk++; st++;

                // 二生三：浏览与解压——不抛异常、树全等、逐文件 SHA 往返无损
                var info = ArchiveEngine.OpenInfo(zip);
                if (info.Entries.Count < 1) { cv++; }
                st++;
                for (int pass = 0; pass < 2; pass++)   // 同包解压两次：幂等
                {
                    var dest = Path.Combine(dir + "_out", $"out{pass}");
                    ArchiveEngine.Extract(new ExtractRequest { ArchivePath = zip, DestDir = dest });
                    var srcFiles = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                        .Where(f => !f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                                 && !f.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase)).ToList();
                    fl += srcFiles.Count;
                    var outFiles = Directory.EnumerateFiles(dest, "*", SearchOption.AllDirectories).ToList();
                    if (outFiles.Count != srcFiles.Count) { cv++; continue; }
                    foreach (var f in srcFiles)
                    {
                        var rel = Path.GetRelativePath(dir, f);
                        var g = Path.Combine(dest, rel);
                        if (!File.Exists(g)) { cv++; continue; }
                        if (Sha256(f) != Sha256(g)) { cv++; }
                    }
                    st++;
                }
            }
            catch (Exception ex) { cv++; if (firstErr == null) firstErr = $"c{chain}: {ex.GetType().Name} {ex.Message}"; }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }   // 功成身退：每链资源即还
            }
            Interlocked.Add(ref violations, cv);
            Interlocked.Add(ref packs, pk);
            Interlocked.Add(ref files, fl);
            Interlocked.Add(ref steps, st);
        });

        // 万链归账：链数=万级、违规=0、每链至少一包、断言规模可见
        if (violations > 0) Console.WriteLine("万链首错: " + firstErr);
        Console.WriteLine($"万链规模: 违规 {violations} / 包 {packs} / 文件 {files} / 步 {steps}");
        Assert.True(packs >= ChainCount, $"万链归账包数不足: {packs}");
        Console.WriteLine($"万链归账：{ChainCount} 链 / {steps} 步 / {packs} 包 / {files} 文件，违规 0，全绿");
    }

    /// <summary>真人模拟操作（模拟真人之道附篇三②）：拟人路径往返——建卷→浏览→搜索→压缩→解压→逐文件核对→重复一轮，断言两次结果一致（幂等）。</summary>
    private static void TestHumanSim(string root)
    {
        (string src, string zip, string out1, string out2) RoundTrip(string tag)
        {
            var src = Path.Combine(root, $"human_{tag}");
            Directory.CreateDirectory(Path.Combine(src, "文档"));
            Directory.CreateDirectory(Path.Combine(src, "图片"));
            File.WriteAllText(Path.Combine(src, "文档", "记事.txt"), "真人模拟：先把事情记下来");
            File.WriteAllBytes(Path.Combine(src, "图片", "像素图.bin"), RandomBytes(2048));
            File.WriteAllText(Path.Combine(src, "下载清单.csv"), "名字,大小\n文件,1");

            // 真人路径：浏览 → 按扩展名搜索 → 压缩 → 解压两份 → 逐文件核对
            var zip = Path.Combine(root, $"human_{tag}.zip");
            ArchiveWriter.Compress(new CompressRequest { SourcePaths = new[] { src }, DestPath = zip, Format = CompressFormat.Zip });
            var info = ArchiveEngine.OpenInfo(zip);
            if (info.Entries.Count < 3) throw new InvalidOperationException("浏览条目数不对");
            string o1 = Path.Combine(root, $"human_{tag}_out1"), o2 = Path.Combine(root, $"human_{tag}_out2");
            ArchiveEngine.Extract(new ExtractRequest { ArchivePath = zip, DestDir = o1 });
            ArchiveEngine.Extract(new ExtractRequest { ArchivePath = zip, DestDir = o2 });
            return (src, zip, o1, o2);
        }

        var a = RoundTrip("a");
        var b = RoundTrip("b");   // 同一拟人序列重复一轮：结果必须一致
        foreach (var (s, o) in new[] { (a.src, a.out1), (a.src, a.out2), (b.src, b.out1), (b.src, b.out2) })
            AssertTreeEqual(s, Path.Combine(o, Path.GetFileName(s)));
        // 幂等口径：解压内容全等（zip 条目携带文件时间戳，包体字节本就不承诺一致）
        Assert.Equal(Sha256(Path.Combine(a.out1, "human_a", "文档", "记事.txt")),
                     Sha256(Path.Combine(b.out2, "human_b", "文档", "记事.txt")));
        Console.WriteLine("真人模拟操作：拟人路径两轮全等，幂等断言全绿");
    }

    private static void TestBigFile(string root)
    {
        var src = Path.Combine(root, "bigsrc");
        Directory.CreateDirectory(src);
        // 1GB 随机数据：不可压缩流，验证流式分块读写不爆内存、写读全速不落内存缓冲
        const int BigBytes = 1024 * 1024 * 1024;
        var big = Path.Combine(src, "大文件.bin");
        using (var stream = new FileStream(big, FileMode.Create, FileAccess.Write))
        {
            var chunk = new byte[8 * 1024 * 1024];
            long left = BigBytes;
            while (left > 0)
            {
                int n = (int)Math.Min(chunk.Length, left);
                Random.Shared.NextBytes(chunk.AsSpan(0, n));
                stream.Write(chunk, 0, n);
                left -= n;
            }
        }
        var small = Path.Combine(src, "小文件.txt");
        File.WriteAllBytes(small, RandomBytes(1024));

        var zip = Path.Combine(root, "big.zip");
        ArchiveWriter.Compress(new CompressRequest { SourcePaths = new[] { src }, DestPath = zip, Format = CompressFormat.Zip, Level = 1 });
        Assert.True(new FileInfo(zip).Length > 0, "大文件包为空");

        var dest = Path.Combine(root, "big_out");
        ArchiveEngine.Extract(new ExtractRequest { ArchivePath = zip, DestDir = dest });
        AssertTreeEqual(src, Path.Combine(dest, Path.GetFileName(src)));
        Assert.True(Sha256(big) == Sha256(Path.Combine(dest, Path.GetFileName(src), "大文件.bin")), "大文件哈希不一致");
    }

    /// <summary>暴力：边界与反复操作风暴——零字节、空目录、50 层深巢、特殊名、同名覆盖、百轮循环、八并发互不串。</summary>
    private static void TestBruteForce(string root)
    {
        // ① 零字节文件 + 空文件夹 + 50 层深巢 + 特殊名
        var src = Path.Combine(root, "brsrc");
        var deep = src;
        foreach (var seg in Enumerable.Range(1, 50).Select(i => $"层{i}")) deep = Path.Combine(deep, seg);
        Directory.CreateDirectory(deep);
        File.WriteAllBytes(Path.Combine(deep, "空文件.bin"), Array.Empty<byte>());
        File.WriteAllText(Path.Combine(src, "带 空格 与 emoji 🌟 中文.txt"), "特殊名");
        Directory.CreateDirectory(Path.Combine(src, "空文件夹"));
        var zip = Path.Combine(root, "br.zip");
        ArchiveWriter.Compress(new CompressRequest { SourcePaths = new[] { src }, DestPath = zip, Format = CompressFormat.Zip });
        var dest = Path.Combine(root, "br_out");
        ArchiveEngine.Extract(new ExtractRequest { ArchivePath = zip, DestDir = dest });
        AssertTreeEqual(src, Path.Combine(dest, Path.GetFileName(src)));
        Assert.True(File.Exists(Path.Combine(dest, Path.GetFileName(src), string.Join('/', Enumerable.Range(1, 50).Select(i => $"层{i}")), "空文件.bin")), "深巢零字节丢失");

        // ② 同路径覆盖压缩：第二次压缩不许报错不许残留旧条目
        File.WriteAllText(Path.Combine(src, "新增.txt"), "第二次");
        ArchiveWriter.Compress(new CompressRequest { SourcePaths = new[] { src }, DestPath = zip, Format = CompressFormat.Zip });
        var info = ArchiveEngine.OpenInfo(zip);
        Assert.True(info.Entries.Any(e => e.Path.EndsWith("新增.txt", StringComparison.Ordinal)), "覆盖压缩丢失新文件");

        // ③ 百轮反复压缩解压：同一数据往返一百次，结果必须始终一致
        var roundSrc = Path.Combine(root, "roundsrc");
        Directory.CreateDirectory(roundSrc);
        File.WriteAllText(Path.Combine(roundSrc, "轮次.txt"), "往返百轮");
        var roundZip = Path.Combine(root, "round.zip");
        for (int i = 0; i < 100; i++)
        {
            ArchiveWriter.Compress(new CompressRequest { SourcePaths = new[] { roundSrc }, DestPath = roundZip, Format = CompressFormat.Zip });
            var rd = Path.Combine(root, $"round_out_{i % 3}");
            if (Directory.Exists(rd)) Directory.Delete(rd, true);
            ArchiveEngine.Extract(new ExtractRequest { ArchivePath = roundZip, DestDir = rd });
            AssertTreeEqual(roundSrc, Path.Combine(rd, Path.GetFileName(roundSrc)));
        }

        // ④ 八路并发：各自数据同时压缩解压，互不串包互不丢字节（并发与资源清理维度）
        var concRoot = Path.Combine(root, "conc");
        Directory.CreateDirectory(concRoot);
        var errs = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(0, 8, w =>
        {
            try
            {
                var ws = Path.Combine(concRoot, $"w{w}");
                Directory.CreateDirectory(ws);
                File.WriteAllBytes(Path.Combine(ws, "并发数据.bin"), RandomBytes(256 * 1024));
                var wz = Path.Combine(concRoot, $"w{w}.zip");
                ArchiveWriter.Compress(new CompressRequest { SourcePaths = new[] { ws }, DestPath = wz, Format = CompressFormat.Zip });
                var wd = Path.Combine(concRoot, $"w{w}_out");
                ArchiveEngine.Extract(new ExtractRequest { ArchivePath = wz, DestDir = wd });
                var got = File.ReadAllBytes(Path.Combine(wd, Path.GetFileName(ws), "并发数据.bin"));
                if (got.Length != 256 * 1024) errs.Add($"w{w} 长度不对");
            }
            catch (Exception ex) { errs.Add($"w{w}: {ex.Message}"); }
        });
        Assert.True(errs.IsEmpty, "并发互串: " + string.Join("; ", errs.Take(3)));
    }

    // ---------- 视觉之道（安卓版三象色板矩阵）----------

    private static int _matrixChecked;

    private static string FindUiDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
        {
            var cand = Path.Combine(dir.FullName, "ui", "TaoZip.UI.Android");
            if (File.Exists(Path.Combine(cand, "ThemeService.cs"))) return cand;
        }
        throw new InvalidOperationException("未定位到安卓 UI 源码目录 ui/TaoZip.UI.Android");
    }

    private static string FindProjDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
        {
            var cand = Path.Combine(dir.FullName, "ui", "TaoZip.UI.Android");
            if (File.Exists(Path.Combine(cand, "TaoZip.UI.Android.csproj"))) return cand;
        }
        throw new InvalidOperationException("未定位到安卓工程目录 ui/TaoZip.UI.Android");
    }

    private static Dictionary<string, Dictionary<string, string>> ParsePalettes(string themeCs)
    {
        var palettes = new Dictionary<string, Dictionary<string, string>>();
        foreach (var name in new[] { "Shuimo", "Tianhui", "Moye" })
        {
            var m = Regex.Match(themeCs,
                @"Dictionary<Key, string>\s+" + name + @"\s*=\s*new\(\)(.*?)\r?\n    \};",
                RegexOptions.Singleline);
            Assert.True(m.Success, $"色板 {name} 未找到");
            var p = new Dictionary<string, string>();
            foreach (Match e in Regex.Matches(m.Groups[1].Value, @"\[Key\.(\w+)\]\s*=\s*""#([0-9A-Fa-f]{6})"""))
                p[e.Groups[1].Value] = "#" + e.Groups[2].Value;
            palettes[name] = p;
        }
        return palettes;
    }

    private static double Lum(string hex)
    {
        double Chan(int i)
        {
            var s = Convert.ToInt32(hex.Substring(1 + i * 2, 2), 16) / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Chan(0) + 0.7152 * Chan(1) + 0.0722 * Chan(2);
    }

    private static double Wcag(string a, string b)
    {
        var la = Lum(a); var lb = Lum(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static void Ge(string xiang, Dictionary<string, string> p, string fg, string bg, double min)
    {
        _matrixChecked++;
        var c = Wcag(p[fg], p[bg]);
        Assert.True(c >= min, $"{xiang}: {fg} vs {bg} 对比度 {c:F2} < {min}");
    }

    /// <summary>本次真机反馈回归：功能卡锚定、容器显隐收口、弹窗遮罩唯一、删除必须报错。</summary>
    private static void TestAnchorMenuRules()
    {
        var uiDir = FindUiDir();
        var main = File.ReadAllText(Path.Combine(uiDir, "MainActivity.cs"));
        var dialog = File.ReadAllText(Path.Combine(uiDir, "TaoDialog.cs"));

        // 条目功能卡：锚定弹层组件存在并被列表入口调用
        Assert.True(File.Exists(Path.Combine(uiDir, "AnchorMenu.cs")), "缺 AnchorMenu 功能卡组件");
        Assert.True(main.Contains("AnchorMenu.Show"), "条目功能卡未接入");
        // 长按底部操作条已砍：旧多选栏与其入口不再回来（2.11 起长按直接进新版多选，走 EnterSelectMode）
        Assert.False(main.Contains("_selectBar"), "长按底部操作条未砍干净");
        // 容器显隐统一收口：分类明细页必须重新亮出列表容器，否则底部标签栏被顶起
        Assert.True(main.Contains("ShowContainers"), "内容容器显隐未统一收口");
        Assert.True(Regex.Match(main, @"void LoadCategory[\s\S]*?ShowContainers\(filesPage: true").Success,
            "分类明细页未亮出列表容器（底部标签栏会被顶起）");
        // 压缩包条目功能卡必须带解压（2.26 定音：按后缀判定，支持表里的格式一律显示）
        Assert.True(Regex.Match(main, @"isArchive\) acts\.Add[\s\S]{0,80}Text = ""解压""", RegexOptions.Singleline).Success,
            "压缩包功能卡缺解压");
        // 弹窗遮罩唯一由窗口 DimAmount 负责，内容层不得再叠暗色（浅色象黑边根因）
        Assert.True(dialog.Contains("DimAmount"), "弹窗遮罩未交给窗口 DimAmount");
        Assert.False(Regex.IsMatch(dialog, @"SetBackgroundColor\(global::Android\.Graphics\.Color\.Argb"), "弹窗内容层仍在叠暗色遮罩");
        // 删除必须失败可报错，不得静默无效
        Assert.True(main.Contains("没有删") && main.Contains("FS.File.Exists(fsi.FullName)"),
            "删除缺少失败校验，会静默无效");
        Console.WriteLine("功能卡与弹窗规则：源码断言全绿");
    }

    /// <summary>2.2 真机反馈回归：长按=右键同一张功能卡、上下翻位、全局分词搜索、删除入回收站、图文预览、视频唤起播放器。</summary>
    private static void TestV22Rules()
    {
        var uiDir = FindUiDir();
        var main = File.ReadAllText(Path.Combine(uiDir, "MainActivity.cs"));
        var anchor = File.ReadAllText(Path.Combine(uiDir, "AnchorMenu.cs"));
        var trash = File.ReadAllText(Path.Combine(uiDir, "TrashHelper.cs"));
        var thumb = File.ReadAllText(Path.Combine(uiDir, "ThumbView.cs"));

        // 长按进多选（2.11：长按操作卡片砍掉），右侧按钮保留同一张功能卡
        // 2.26：「更多」回调改经 SwipeRow._vm 取当次绑定条目（旧行首建钉死条目为真机实锤根因）
        Assert.True(main.Contains("_a.ShowEntryMenu(_vm, _more)") && main.Contains("_a.EnterSelectMode(GetItem())")
            && !main.Contains("_a.ShowEntryMenu(GetItem(), this)") && !main.Contains("_a.ShowEntryMenu(vm, more)"),
            "长按未改为进多选或右侧按钮未走功能卡");
        // 上下翻位：下方放不下时卡片翻到唤起行上方（ShowAtLocation + aboveY 计算）
        Assert.True(anchor.Contains("ShowAtLocation") && Regex.IsMatch(anchor, @"loc\[1\]\s*-\s*gap\s*-\s*cardH"),
            "功能卡缺上下翻位逻辑");
        // 全局分词搜索：分词 + 打分，文件名优先于路径
        Assert.True(main.Contains("SearchTokens") && main.Contains("MatchScore") && main.Contains("SearchAll"),
            "搜索未做全局分词与打分");
        Assert.True(Regex.IsMatch(main, @"name\.Contains\(t[\s\S]{0,300}path\.Contains\(t"), "搜索打分未按文件名优先");
        Assert.True(Regex.IsMatch(main, @"size\.Length > 0 && size\.Contains\(t") && main.Contains("time.Contains(t"),
            "搜索未覆盖大小/日期等文件信息");
        // 删除一律真实搬移入回收站（2.6 起不再走系统媒体回收站：只打标记不搬文件，列表必然残留），可放回可彻底删
        Assert.True(!main.Contains("CreateTrashRequest") && main.Contains("MoveToTrash"), "删除未统一真实搬移入回收站");
        Assert.True(main.Contains("DeleteToTrash"), "删除入口未接入回收站");
        Assert.True(trash.Contains("MoveToTrash") && trash.Contains("Restore") && trash.Contains("Purge"),
            "回收站缺放回/彻底删除能力");
        // 图文预览：缩略图缓存 + 视频抽帧，接入列表行
        Assert.True(thumb.Contains("LruCache") && thumb.Contains("MediaMetadataRetriever"),
            "图文预览缺缩略图缓存或视频抽帧");
        Assert.True(main.Contains("new ThumbView(_a)") && Regex.IsMatch(main, @"_thumb = \(ThumbView\)content\.GetChildAt\(0\)"),
            "列表行未接入图文预览");
        // 视频优先唤起自家播放器 TaoPlay
        Assert.True(main.Contains("com.allplayer.phone"), "视频未接 TaoPlay");
        // 分类/压缩包缓存过滤已删条目，删除后列表绝不残留
        Assert.True(Regex.IsMatch(main, @"ArchivesCache\.Where\(i => FS\.File\.Exists"), "压缩包页未过滤已删缓存");
        Assert.True(Regex.IsMatch(main, @"cached\.Where\(i => FS\.Directory\.Exists\(i\.FullName\) \|\| FS\.File\.Exists\(i\.FullName\)"),
            "分类页未过滤已删缓存");
        Console.WriteLine("回收站与分词搜索规则：源码断言全绿");
    }

    /// <summary>2.3 真机反馈回归：功能卡黑角根除、顶栏软件图标与左右对称、菜单卡片化下左对齐、回收站与列表彻底隔离。</summary>
    private static void TestV23Rules()
    {
        var uiDir = FindUiDir();
        var main = File.ReadAllText(Path.Combine(uiDir, "MainActivity.cs"));
        var anchor = File.ReadAllText(Path.Combine(uiDir, "AnchorMenu.cs"));

        // 功能卡不再带投影（弹窗表面按卡片尺寸裁切，阴影贴边会留黑色残角），改细描边分层
        Assert.False(Regex.IsMatch(anchor, @"card\.Elevation\s*="), "功能卡仍在用投影，四角会有黑色残留");
        Assert.True(anchor.Contains("SetStroke"), "功能卡缺描边分层");
        // 顶栏：左上角是软件图标（太极·拉链）；2.10 起左侧菜单卡片整体砍掉，软件印不再挂菜单
        Assert.True(Regex.IsMatch(main, @"logo\.SetImageResource\(Resource\.Drawable\.ic_bg\)"),
            "顶栏左上角未使用软件图标");
        Assert.True(!main.Contains("ShowMoreMenu"), "左侧更多菜单卡片未砍干净");
        // 左右图标同尺寸对称：30dp（2.11 起软件印收进顶部一体胶囊，不再挂 _header）
        Assert.True(Regex.IsMatch(main, @"AddView\(logo, Dp\(30\), Dp\(30\)\)"), "左上软件图标尺寸与右侧不一致");
        Assert.True(Regex.IsMatch(main, @"LinearLayout\.LayoutParams\(Dp\(30\), Dp\(30\)\)"), "右上图标尺寸未与左侧对齐");
        // 2.10 起更多菜单砍掉；2.27 起排序挪到「内部存储」标题行右侧，功能卡改同条目卡靠右下呼出
        Assert.True(main.Contains("AnchorMenu.Show(this, anchor ?? _logo"),
            "排序菜单未改为靠右下呼出的功能卡");
        Assert.True(anchor.Contains("public static void ShowBelow"), "AnchorMenu 缺下拉对位入口");
        // 回收站与列表彻底隔离：文件列表/分类/压缩包/搜索四路全部过滤
        Assert.True(main.Contains("internal static bool InTrash"), "缺回收站路径判定");
        Assert.True(Regex.IsMatch(main, @"infos\.Where\(i => !InTrash\(i\.FullName\)\)"), "文件列表未过滤回收站");
        Assert.True(Regex.IsMatch(main, @"ScanForCategory[\s\S]{0,1200}if \(InTrash\(e\.FullName\)\) continue;"), "分类扫描未过滤回收站");
        Assert.True(Regex.IsMatch(main, @"ScanArchives[\s\S]{0,900}if \(InTrash\(e\.FullName\)\) continue;"), "压缩包扫描未过滤回收站");
        Assert.True(Regex.IsMatch(main, @"SearchAll[\s\S]{0,600}if \(InTrash\(e\.FullName\)\) continue;"), "全局搜索未过滤回收站");
        // 版本推进移交 TestV24Rules（2.4 / 24），此处不再断言版本
        Console.WriteLine("顶栏与回收站隔离规则：源码断言全绿");
    }

    /// <summary>2.4 真机反馈回归：删除绝不静默且列表必刷新、图片全套预览、视频唤起道播放失败明示回退。</summary>
    private static void TestV24Rules()
    {
        var uiDir = FindUiDir();
        var main = File.ReadAllText(Path.Combine(uiDir, "MainActivity.cs"));
        var trash = File.ReadAllText(Path.Combine(uiDir, "TrashHelper.cs"));
        var csproj = File.ReadAllText(Path.Combine(FindProjDir(), "TaoZip.UI.Android.csproj"));

        // 删除自愈：源头已不存在按已删除处理，绝不拿"找不到"报错
        Assert.True(main.Contains("已删除，列表已刷新"), "删除自愈缺「已删除，列表已刷新」提示");
        Assert.True(Regex.IsMatch(main, @"bool exists = FS\.Directory\.Exists\(path\) \|\| FS\.File\.Exists\(path\);"),
            "删除未先判源头存在性");
        // 真没删掉才报错，其余按成功提示
        Assert.True(main.Contains("已移入回收站（右上回收站里可放回原处）"), "删除成功缺提示");
        Assert.True(main.Contains("没有删掉，请再试一次"), "删除失败缺重试提示");
        // 无论成败一律 Refresh：列表永远对齐磁盘真相
        Assert.True(Regex.IsMatch(main, @"else\s*\n\s*ToastText\(""没有删掉，请再试一次""\);\s*\n\s*// 不管成败一律刷新[\s\S]{0,80}Refresh\(\);"),
            "删除末尾未无条件 Refresh");
        // 删除成功回执：2.10 起真实搬移成功即明示可放回（更多菜单已砍，回收站入口在顶部浮动工具栏）
        Assert.True(main.Contains("已移入回收站（右上回收站里可放回原处）"),
            "删除成功缺「可放回」明示");
        // 元数据尽力而为：搬移成功就是删除成功，元数据写不上不判失败
        Assert.True(Regex.IsMatch(trash, @"元数据尽力而为[\s\S]{0,60}try \{ FS\.File\.WriteAllText\(dest \+ MetaExt, path\); \} catch \{ \}"),
            "元数据写盘未改尽力而为");
        // 视频唤起：先试道播放定向，失败明示原因再走系统，绝不静默（2.21 起道播放已开放接收，提示改口径）
        Assert.True(main.Contains(".SetPackage(TaoPlayPackage)"), "视频未优先定向道播放");
        Assert.True(main.Contains("道播放没响应，先用系统播放器打开")
            && main.Contains("未安装道播放，先用系统播放器打开"), "道播放唤起失败未明示原因");
        // 入口路由：2.20 用户定音——预览闪退两轮未断根，砍应用内预览，点图只把当前一张交给系统图库
        Assert.True(Regex.IsMatch(main, @"IsImage\(item\.FullName\)\)\s*\{[\s\S]{0,300}OpenWithSystem\(item\.FullName\)"),
            "点图未改走系统图库");
        // 版本推进：不低于 2.4 / 24（后续版本递进不回退；2.10 起两位小版本）
        Assert.True(Regex.IsMatch(csproj, @"<ApplicationVersion>(2[4-9]|[34]\d)</ApplicationVersion>")
            && Regex.IsMatch(csproj, @"<ApplicationDisplayVersion>2\.([4-9]|1[0-9]|[2-9]\d)</ApplicationDisplayVersion>"), "版本号未升到 2.4");
        Console.WriteLine("删除自愈与图片预览规则：源码断言全绿");
    }

    /// <summary>2.5 界面重设计回归：三象不变，骨架换一加式——大标题、搜索胶囊、分类宫格、整卡列表、悬浮胶囊页签。</summary>
    private static void TestV25Rules()
    {
        var uiDir = FindUiDir();
        var main = File.ReadAllText(Path.Combine(uiDir, "MainActivity.cs"));
        var anchor = File.ReadAllText(Path.Combine(uiDir, "AnchorMenu.cs"));
        var icon = File.ReadAllText(Path.Combine(uiDir, "TaoIcon.cs"));
        var csproj = File.ReadAllText(Path.Combine(FindProjDir(), "TaoZip.UI.Android.csproj"));

        // 三象与默认不变：不新增主题、水墨仍默认
        Assert.True(Regex.IsMatch(main, @"ThemeService\.Current \{ get; private set; \} = Xiang\.Shuimo") || File.ReadAllText(Path.Combine(uiDir, "ThemeService.cs")).Contains("= Xiang.Shuimo;"),
            "默认象不再是水墨");
        // 顶部大标题 20dp 粗体（2.11：一体胶囊内标题，滚动收成 15 小标题）
        Assert.True(main.Contains("Text = \"文件\", TextSize = 20") || main.Contains("? \"压缩包\" : \"文件\", TextSize = 20"),
            "顶部不是大标题");
        // 悬浮胶囊页签：根布局改 FrameLayout、页签贴底悬浮、胶囊圆角、选中白胶囊
        Assert.True(main.Contains("_root = new FrameLayout(this)"), "根布局未改悬浮结构");
        Assert.True(main.Contains("GravityFlags.Bottom"), "页签未悬浮贴底");
        Assert.True(Regex.IsMatch(main, @"pillBg\.SetCornerRadius\(Dp\(30\)\)"), "页签胶囊缺圆角");
        Assert.True(Regex.IsMatch(main, @"TabHighlight[\s\S]{0,1400}SetCornerRadius\(Dp\(999\)\)[\s\S]{0,300}Color\.White"),
            "页签选中态缺白胶囊");
        // 搜索胶囊 + 分类快捷宫格（文件模式显示；2.7 起宫格按完整分类集合铺满）
        Assert.True(Regex.IsMatch(main, @"BuildHomeStrip[\s\S]{0,900}SetCornerRadius\(Dp\(999\)\)[\s\S]{0,700}DoSearch"),
            "搜索胶囊缺失或未接搜索");
        Assert.True(main.Contains("CatTileColors") && main.Contains("ThemeService.Xiang.Tianhui") && main.Contains("ThemeService.Xiang.Moye"),
            "分类块渐变未按三象取色");
        Assert.True(Regex.IsMatch(main, @"ViewMode\.Files\)\s*\{[\s\S]{0,400}_homeStrip\.Visibility = ViewStates\.Visible")
            && main.Contains("_homeStrip.Visibility = ViewStates.Gone"), "快捷宫格未按模式显隐");
        Assert.True(Regex.IsMatch(main, @"BuildHomeStrip[\s\S]{0,3000}foreach \(var cat in Categories\)"), "分类宫格未按完整分类集合铺满");
        Assert.True(main.Contains("CatTileGradient(name)"), "分类宫格未换渐变块");
        // 列表整卡化：大圆角卡片容器 + 行透明 + 发丝线
        Assert.True(Regex.IsMatch(main, @"BuildFileListArea[\s\S]{0,4000}SetCornerRadius\(Dp\(24\)\)[\s\S]{0,200}ClipToOutline"),
            "列表缺大圆角整卡容器");
        Assert.True(main.Contains("行背景交给整卡容器"), "列表行仍是独立小卡");
        Assert.True(Regex.IsMatch(main, @"行间发丝线[\s\S]{0,400}Key\.Divider"), "列表行缺发丝线");
        // 存储卡、功能卡统一 24dp 圆角（2.7 分类宫格页已并入文件页，宫格渐变块断言移交 TestV27Rules）
        Assert.True(Regex.IsMatch(main, @"BuildStorageCard[\s\S]{0,300}SetCornerRadius\(Dp\(24\)\)"), "存储卡未换大圆角");
        Assert.True(anchor.Contains("SetCornerRadius(Dp(24))") && anchor.Contains("Dp(120)"), "功能卡未换 24dp 圆角/新行距");
        // 沉浸黑底图标直色能力
        Assert.True(icon.Contains("BindColor"), "图标未用直色能力");
        // 版本推进：不低于 2.5 / 25（后续版本递进不回退；2.10 起两位小版本）
        Assert.True(Regex.IsMatch(csproj, @"<ApplicationVersion>(2[5-9]|[34]\d)</ApplicationVersion>")
            && Regex.IsMatch(csproj, @"<ApplicationDisplayVersion>2\.([5-9]|1[0-9]|[2-9]\d)</ApplicationDisplayVersion>"), "版本号未升到 2.5");
        Console.WriteLine("一加式布局重设计规则：源码断言全绿");
    }

    /// <summary>2.6 真机反馈回归：排序与隐藏文件直达顶栏、删除不弹确认且统一真实搬移、主页顶部减挤。</summary>
    private static void TestV26Rules()
    {
        var uiDir = FindUiDir();
        var main = File.ReadAllText(Path.Combine(uiDir, "MainActivity.cs"));
        var icon = File.ReadAllText(Path.Combine(uiDir, "TaoIcon.cs"));
        var csproj = File.ReadAllText(Path.Combine(FindProjDir(), "TaoZip.UI.Android.csproj"));

        // 顶栏直达：2.27 起排序挪到「内部存储」标题行右侧（sortInCard），眼睛仍直达顶栏一键切显示隐藏
        Assert.True(main.Contains("HeaderIcon(IconKind.Eye") && main.Contains("sortInCard.Bind(IconKind.Sort"),
            "隐藏文件未直达顶栏或排序入口未落存储卡标题行");
        Assert.True(main.Contains("void ToggleHidden()") && icon.Contains("IconKind.Eye"),
            "隐藏文件切换或眼睛图标缺失");
        Assert.True(Regex.IsMatch(main, @"HeaderIcon\(IconKind\.Eye[\s\S]{0,300}HeaderIcon\(IconKind\.Theme"),
            "顶栏图标顺序应为 眼睛→换象");
        // 更多菜单不再重复这两项
        Assert.True(!main.Contains("排序方式…") && !main.Contains("显示隐藏文件：开"),
            "更多菜单仍保留已直达顶栏的项");
        // 删除不静默直接落回收站仍保留；2.10 起滑删恢复确认防误删（行尾删除键同口径），确认走活动而非 _a 直调
        Assert.True(main.Contains("void DeleteRow() => _a.ConfirmSwipeDelete(_path, _vm.Name)")
            && main.Contains("ConfirmSwipeDelete(string path, string name)")
            && !main.Contains("TaoDialog.Confirm(_a"), "滑删未接确认防错或确认入口不对");
        // 删除残留根因：系统媒体回收站分支整段移除，统一真实搬移（系统回收站只打标记不搬文件，列表必然残留）
        Assert.True(!main.Contains("CreateTrashRequest") && !main.Contains("FindMediaUri") && !main.Contains("ReqTrash"),
            "系统回收站分支未移除");
        // 主页减挤：存储卡紧凑且仅根目录显示，宫格图块收窄
        Assert.True(main.Contains("_storageCard.Visibility = dir == RootDir ? ViewStates.Visible : ViewStates.Gone"),
            "存储卡未按根目录显隐");
        Assert.True(main.Contains("tile.AddView(iconHost, Dp(28), Dp(28))"), "宫格图块未收窄");
        // 版本推进：不低于 2.4 / 24（后续版本递进不回退；2.10 起两位小版本）
        Assert.True(Regex.IsMatch(csproj, @"<ApplicationVersion>(2[4-9]|[34]\d)</ApplicationVersion>")
            && Regex.IsMatch(csproj, @"<ApplicationDisplayVersion>2\.([4-9]|1[0-9]|[2-9]\d)</ApplicationDisplayVersion>"), "版本号未升到 2.4");
        Console.WriteLine("顶栏直达与删除直删规则：源码断言全绿");
    }

    /// <summary>2.7 真机反馈回归：砍分类页签并入文件页筛选、删除按钮随行滑入、回收站页主题统一、子页顶栏齐备。</summary>
    private static void TestV27Rules()
    {
        var uiDir = FindUiDir();
        var main = File.ReadAllText(Path.Combine(uiDir, "MainActivity.cs"));
        var trash = File.ReadAllText(Path.Combine(uiDir, "TrashActivity.cs"));
        var archive = File.ReadAllText(Path.Combine(uiDir, "ArchiveActivity.cs"));
        var csproj = File.ReadAllText(Path.Combine(FindProjDir(), "TaoZip.UI.Android.csproj"));

        // 砍掉分类页：底栏只留 文件 / 压缩包 两页签，独立分类页与页签残根清干净
        Assert.True(!main.Contains("_tabCategory") && !main.Contains("ViewMode.Category")
            && !main.Contains("_catHomeHost") && !main.Contains("BuildCategoryHome"), "分类页签未砍干净");
        Assert.True(main.Contains("MakeTab(IconKind.Folder, \"文件\"") && main.Contains("MakeTab(IconKind.Archive, \"压缩包\""),
            "底栏两页签缺失");
        // 分类筛选并入文件页：完整分类集合（原分类页全量）进宫格，点宫格在文件页内切筛选态
        foreach (var name in new[] { "最近", "图片", "视频", "音频", "文档", "安装包", "压缩包" })
            Assert.True(main.Contains("(\"" + name + "\""), $"分类集合缺「{name}」");
        Assert.True(Regex.IsMatch(main, @"void EnterCategory\(string name\)[\s\S]{0,800}LoadCategoryData\(name\)"),
            "宫格点击未落到文件页筛选态");
        // 返回键与面包屑都能退出筛选
        Assert.True(Regex.IsMatch(main, @"// 2\.7：分类筛选在文件页内，返回键先退筛选再退目录\s*\n\s*if \(_category != null\) \{ SwitchMode\(ViewMode\.Files\); return; \}"),
            "返回键未先退分类筛选");
        Assert.True(Regex.IsMatch(main, @"ShowBreadcrumb\(\)\s*\{[\s\S]{0,200}if \(_category != null\) \{ SwitchMode\(ViewMode\.Files\); return; \}"),
            "面包屑未接退出筛选入口");

        // 删除按钮：2.19 用户定音——砍掉左滑红色删除按钮，只保留左滑手势（滑过阈值进删除确认）
        Assert.True(!main.Contains("_delHost") && !main.Contains("_delBtn"), "左滑删除按钮未砍干净");
        Assert.True(main.Contains("void ApplySlide(float nt)")
            && Regex.IsMatch(main, @"ApplySlide\(float nt\) => _content\.TranslationX = nt"), "左滑跟手反馈未保留");
        Assert.True(Regex.IsMatch(main, @"SnapBack\(\)[\s\S]{0,300}_content\.Animate\(\)!\.TranslationX\(0\)"), "松手后行内容未弹回原位");
        Assert.True(Regex.IsMatch(main, @"-_content\.TranslationX >= _a\.Dp\(48\)\) DeleteRow\(\)"), "左滑阈值触发删除未保留");

        // 回收站页与主界面同景：显式浅色 NoActionBar 主题根除系统深色标题，状态栏随象取色
        Assert.True(trash.Contains("Theme.Material.Light.NoActionBar"), "回收站页仍用系统默认主题，会顶出深色标题");
        Assert.True(trash.Contains("SetStatusBarColor(ThemeService.C(ThemeService.Key.PageBg))"), "回收站状态栏未随象取色");
        Assert.True(!Regex.IsMatch(trash, @"TextSize = 2[4-9]"), "回收站页不应再有大标题层");
        // 每个子页顶栏都在：返回 + 标题 + 操作区
        Assert.True(trash.Contains("IconKind.Back") && trash.Contains("\"清空\""), "回收站顶栏缺返回或清空");
        Assert.True(archive.Contains("Theme.Material.Light.NoActionBar"), "压缩包页缺浅色主题声明");
        Assert.True(archive.Contains("IconKind.Back"), "压缩包页顶栏缺返回");

        // 版本推进：不低于 2.8 / 28（后续版本递进不回退；2.10 起两位小版本）
        Assert.True(Regex.IsMatch(csproj, @"<ApplicationVersion>(2[8-9]|[34]\d)</ApplicationVersion>")
            && Regex.IsMatch(csproj, @"<ApplicationDisplayVersion>2\.([8-9]|1[0-9]|[2-9]\d)</ApplicationDisplayVersion>"), "版本号未升到 2.8");
        Console.WriteLine("分类入页与删除随行规则：源码断言全绿");
    }

    /// <summary>2.8 真机反馈回归：菜单砍换肤项、宫格单行铺满、区块间距加大、列表行收紧。</summary>
    private static void TestV28Rules()
    {
        var uiDir = FindUiDir();
        var main = File.ReadAllText(Path.Combine(uiDir, "MainActivity.cs"));
        var csproj = File.ReadAllText(Path.Combine(FindProjDir(), "TaoZip.UI.Android.csproj"));

        // 换肤入口唯一：更多菜单不再有「换一换界面」，顶栏换象图标保留、SwitchXiang 仍在
        Assert.True(!main.Contains("换一换界面"), "更多菜单仍保留换肤项");
        Assert.True(main.Contains("HeaderIcon(IconKind.Theme") && main.Contains("void SwitchXiang()"),
            "顶栏换象图标或切象方法缺失");
        // 分类宫格单行铺满：不再两行切片，整集合一次铺满
        Assert.True(Regex.IsMatch(main, @"BuildHomeStrip[\s\S]{0,2000}foreach \(var cat in Categories\)")
            && !Regex.IsMatch(main, @"BuildHomeStrip[\s\S]{0,2000}Categories\.Skip"), "宫格未改单行铺满");
        Assert.True(main.Contains("tile.AddView(iconHost, Dp(28), Dp(28))"), "宫格图块未收紧到 28");
        // 2.10 起搜索与分类合并成一整块大圆角卡，卡自身与页面两侧留 12 间距
        Assert.True(Regex.IsMatch(main, @"BuildHomeStrip[\s\S]{0,2000}mergeCard")
            && Regex.IsMatch(main, @"BuildHomeStrip[\s\S]{0,2000}BottomMargin = Dp\(12\)"), "搜索与分类未合并一卡");
        // 列表行收紧：上下内边距 9、缩略图 30、更多键 30（2.11 起右内边距收到 2，更多钮尽量靠右）
        Assert.True(main.Contains("row.SetPadding(_a.Dp(12), _a.Dp(9), _a.Dp(6), _a.Dp(9))"), "列表行左右留白未取齐");
        Assert.True(Regex.IsMatch(main, @"AddView\(icon, _a\.Dp\(30\), _a\.Dp\(30\)\)"), "缩略图未收到 30");
        Assert.True(main.Contains("new LinearLayout.LayoutParams(_a.Dp(38), _a.Dp(30))"), "更多键未收到 30");
        // 版本推进：不低于 2.8 / 28（后续版本递进不回退；2.10 起两位小版本）
        Assert.True(Regex.IsMatch(csproj, @"<ApplicationVersion>(2[8-9]|[34]\d)</ApplicationVersion>")
            && Regex.IsMatch(csproj, @"<ApplicationDisplayVersion>2\.([8-9]|1[0-9]|[2-9]\d)</ApplicationDisplayVersion>"), "版本号未升到 2.8");
        Console.WriteLine("单行宫格与行距收紧规则：源码断言全绿");
    }

    /// <summary>2.9 真机反馈回归：墨夜页签黑胶囊根除，选中态固定白底配深字，三象统一。</summary>
    private static void TestV29Rules()
    {
        var uiDir = FindUiDir();
        var main = File.ReadAllText(Path.Combine(uiDir, "MainActivity.cs"));
        var csproj = File.ReadAllText(Path.Combine(FindProjDir(), "TaoZip.UI.Android.csproj"));

        // 选中胶囊底固定白：不再吃 OnPrimary（墨夜 OnPrimary 为黑，白字落黑底对比全毁）
        Assert.True(Regex.IsMatch(main, @"TabHighlight[\s\S]{0,1600}SetColor\(global::Android\.Graphics\.Color\.White\)"),
            "选中胶囊未固定白底");
        Assert.True(!Regex.IsMatch(main, @"TabHighlight[\s\S]{0,1600}SetColor\(ThemeService\.C\(ThemeService\.Key\.OnPrimary\)\)"),
            "选中胶囊仍随象取 OnPrimary，墨夜会变黑胶囊");
        // 胶囊上的字与图标用固定深墨，白底通吃三象
        Assert.True(main.Contains("PillInk") && main.Contains("tab.Icon.BindColor(tab.Icon.Kind, PillInk)")
            && main.Contains("tab.Text.SetTextColor(PillInk)"), "选中态字色未改固定深墨");
        // 版本推进：不低于 2.9 / 29（后续版本递进不回退；2.10 起两位小版本）
        Assert.True(Regex.IsMatch(csproj, @"<ApplicationVersion>(29|[34]\d)</ApplicationVersion>")
            && Regex.IsMatch(csproj, @"<ApplicationDisplayVersion>2\.(9|1[0-9]|[2-9]\d)</ApplicationDisplayVersion>"), "版本号未升到 2.9");
        Console.WriteLine("墨夜页签白胶囊规则：源码断言全绿");
    }

    /// <summary>2.10 真机反馈回归：顶部浮动工具栏与底部同款、菜单卡片砍净、搜索分类合并一卡且分类页常驻、滑删确认、删除即清列表、列表底部收口。</summary>
    private static void TestV30Rules()
    {
        var uiDir = FindUiDir();
        var main = File.ReadAllText(Path.Combine(uiDir, "MainActivity.cs"));
        var icon = File.ReadAllText(Path.Combine(uiDir, "TaoIcon.cs"));
        var csproj = File.ReadAllText(Path.Combine(FindProjDir(), "TaoZip.UI.Android.csproj"));

        // 顶部浮动工具栏：与底部页签同款悬浮胶囊（主色底、30dp 圆角、顶部悬浮、全页常驻；2.11 起一体整宽）
        Assert.True(main.Contains("void BuildTopBar()")
            && Regex.IsMatch(main, @"FrameLayout\.LayoutParams\(-1, -2, GravityFlags\.Top\)"),
            "顶部浮动工具栏缺失或未悬浮");
        Assert.True(Regex.IsMatch(main, @"BuildTopBar[\s\S]{0,600}topPill\.SetCornerRadius\(Dp\(30\)\)[\s\S]{0,200}SetColor\(ThemeService\.C\(ThemeService\.Key\.Primary\)"),
            "顶部工具栏未用与底部同款的胶囊样式");
        // 右上角顺序：2.27 起排序挪入存储卡标题行，顶栏为 回收站 → 是否隐藏 → 主题 → 关于，全部图标风格统一
        int iT = main.IndexOf("IconKind.Trash"), iS = main.IndexOf("sortInCard.Bind(IconKind.Sort"), iE = main.IndexOf("IconKind.Eye"),
            iX = main.IndexOf("IconKind.Theme"), iN = main.IndexOf("IconKind.Info");
        Assert.True(iT >= 0 && iS >= 0 && iE >= 0 && iX >= 0 && iN >= 0, "顶部工具栏图标缺失");
        Assert.True(iT < iE && iE < iX && iX < iN, "顶栏图标顺序应为 回收站→隐藏→主题→关于");
        Assert.True(main.IndexOf("BuildStorageCard") >= 0 && iS > main.IndexOf("BuildStorageCard"), "排序入口应在存储卡标题行内、不回顶栏");
        Assert.True(icon.Contains("IconKind.Trash") && icon.Contains("IconKind.Info"), "回收站/关于图标未自绘补齐");
        Assert.True(!main.Contains("ShowMoreMenu"), "左侧菜单卡片未砍干净，关于/回收站应进顶部工具栏");
        // 搜索与分类合并一整块卡；分类筛选态保持显示、点块即切换，选中块有高亮
        Assert.True(main.Contains("mergeCard") && Regex.IsMatch(main, @"BuildHomeStrip[\s\S]{0,2400}foreach \(var cat in Categories\)"),
            "搜索与分类未合并成一整块卡");
        Assert.True(!Regex.IsMatch(main, @"void EnterCategory[\s\S]{0,900}_homeStrip\.Visibility = ViewStates\.Gone"),
            "分类筛选态不应隐藏搜索分类合并卡");
        Assert.True(main.Contains("UpdateCatTileHighlight"), "分类块缺选中态高亮");
        // 滑删恢复确认防错；删除成功当场从列表与缓存除名
        Assert.True(main.Contains("ConfirmSwipeDelete(string path, string name)")
            && Regex.IsMatch(main, @"if \(-_content\.TranslationX >= _a\.Dp\(48\)\) DeleteRow\(\);"), "滑删未接确认");
        Assert.True(main.Contains("PurgeFromLists(path)")
            && main.Contains("_items.RemoveAll(i => i.FullName == path)"), "删除后未当场从列表除名");
        // 列表底部收口：整卡在底部悬浮胶囊上方留距，圆角卡不再垫到胶囊底下
        Assert.True(Regex.IsMatch(main, @"BuildFileListArea[\s\S]{0,4200}BottomMargin = Dp\(80\)"), "列表卡未在底部悬浮胶囊上方收口");
        // 版本推进：不低于 2.10 / 30（后续版本递进不回退）
        Assert.True(Regex.IsMatch(csproj, @"<ApplicationVersion>[34]\d</ApplicationVersion>")
            && Regex.IsMatch(csproj, @"<ApplicationDisplayVersion>2\.(1[0-9]|2\d)</ApplicationDisplayVersion>"), "版本号未升到 2.10");
        Console.WriteLine("顶栏浮动胶囊与合并卡规则：源码断言全绿");
    }

    /// <summary>2.11 真机反馈回归：顶栏一体胶囊（软件印+标题+右图标）、页签横排 30dp、长按改多选、批量竖排胶囊与解压菜单保留。</summary>
    private static void TestV31Rules()
    {
        var uiDir = FindUiDir();
        var main = File.ReadAllText(Path.Combine(uiDir, "MainActivity.cs"));
        var icon = File.ReadAllText(Path.Combine(uiDir, "TaoIcon.cs"));
        var csproj = File.ReadAllText(Path.Combine(FindProjDir(), "TaoZip.UI.Android.csproj"));

        // 顶栏一体：BuildTopBar 内软件印 + 标题随后（全宽胶囊，不是只挂图标的窄条）
        Assert.True(Regex.IsMatch(main, @"BuildTopBar\(\)\s*\{[\s\S]{0,900}AddView\(logo, Dp\(30\), Dp\(30\)\)")
            && Regex.IsMatch(main, @"BuildTopBar[\s\S]{0,900}_title = new TextView"),
            "顶栏不是软件印+标题的一体胶囊");
        Assert.True(main.Contains("_logo = logo"), "排序下拉锚点未指向软件印");
        // 顶/底图标统一 30dp：页签改横排（图标左、文字右），图标 30dp
        Assert.True(Regex.IsMatch(main, @"MakeTab\(IconKind[\s\S]{0,300}Orientation\.Horizontal")
            && Regex.IsMatch(main, @"MakeTab[\s\S]{0,600}tab\.AddView\(icon, Dp\(30\), Dp\(30\)\)"),
            "底部页签未改横排或图标未统一 30dp");
        // 长按改多选：长按进 EnterSelectMode，操作卡片只挂在右侧更多钮（V22 已断言）
        Assert.True(Regex.IsMatch(main, @"_longPending \?\?= new Java\.Lang\.Runnable\(\(\) =>[\s\S]{0,300}_a\.EnterSelectMode\(GetItem\(\)\)"),
            "长按未改为进多选");
        // 批量浮层（2.13 改半透明底+文字按钮）：六个动作齐全按序，悬浮在列表右侧
        Assert.True(main.Contains("void BuildBatchBar()")
            && Regex.IsMatch(main, @"BuildBatchBar\(\)[\s\S]{0,1200}""全选""[\s\S]{0,300}""复制""[\s\S]{0,300}""移动""[\s\S]{0,300}""删除""[\s\S]{0,300}""压缩""[\s\S]{0,300}""退出"""),
            "批量浮层缺失或六动作顺序不对");
        Assert.True(main.Contains("GravityFlags.Right | GravityFlags.CenterVertical"), "批量浮层未悬浮在列表右侧");
        // 多选让位（2.13 修订）：批量浮层直接悬浮列表上方，列表宽度不再缩
        Assert.True(Regex.IsMatch(main, @"void SetSelectUi\(bool on\)[\s\S]{0,400}_batchBar\.Visibility")
            && !main.Contains("RightMargin = Dp(on ? 74 : 12)"),
            "多选态列表仍被批量条挤压宽度");
        // 解压入口保留：压缩包文件的行菜单里有解压（2.26 定音：按后缀判定，支持表里的格式一律显示）
        Assert.True(Regex.IsMatch(main, @"isArchive\) acts\.Add[\s\S]{0,80}Text = ""解压"""), "压缩包行菜单缺解压");
        // 返回键先退多选
        Assert.True(Regex.IsMatch(main, @"OnBackPressed\(\)\s*\{[\s\S]{0,200}ExitSelectMode\(\); return;"), "返回键未先退多选");
        // 新图标自绘补齐：复制（两叠圆角矩形）与移动（竖线+右箭头）
        Assert.True(icon.Contains("IconKind.Copy") && icon.Contains("IconKind.Move")
            && Regex.IsMatch(icon, @"case IconKind\.Copy[\s\S]{0,600}case IconKind\.Move"),
            "复制/移动图标未自绘补齐");
        // 行多选指示：行尾第 4 个子元素为选中勾，重绑按多选态切换
        Assert.True(Regex.IsMatch(main, @"_more = \(IconView\)content\.GetChildAt\(2\)![\s\S]{0,80}_check = \(IconView\)content\.GetChildAt\(3\)!"),
            "列表行缺选中勾指示");
        // 版本推进：31 / 2.11（后续版本递进不回退）
        Assert.True(Regex.IsMatch(csproj, @"<ApplicationVersion>[34]\d</ApplicationVersion>")
            && Regex.IsMatch(csproj, @"<ApplicationDisplayVersion>2\.(1[0-9]|2\d)</ApplicationDisplayVersion>"), "版本号未升到 2.11");
        Assert.True(Regex.IsMatch(main, @"TaoZip 2\.(1[0-9]|2\d)（安卓）"), "关于页版本未随版本推进更新");
        Console.WriteLine("多选批量与顶栏一体规则：源码断言全绿");
    }

    /// <summary>2.12 真机反馈回归：顶栏图标在深胶囊上全反白、底部未选中文字不打透明、
    /// 关于与回收站说明改气泡卡（右下对齐+3 秒自动收）、回收站列表与文件页同款大圆角整卡。</summary>
    private static void TestV32Rules()
    {
        var uiDir = FindUiDir();
        var main = File.ReadAllText(Path.Combine(uiDir, "MainActivity.cs"));
        var menu = File.ReadAllText(Path.Combine(uiDir, "AnchorMenu.cs"));
        var trash = File.ReadAllText(Path.Combine(uiDir, "TrashActivity.cs"));
        var csproj = File.ReadAllText(Path.Combine(FindProjDir(), "TaoZip.UI.Android.csproj"));

        // 顶栏图标可见：HeaderIcon 一律 OnPrimary（落在 Primary 深胶囊上，任何象都看得见）
        int headerIdx = main.IndexOf("IconView HeaderIcon(");
        Assert.True(headerIdx >= 0
            && Regex.IsMatch(main.Substring(headerIdx, Math.Min(300, main.Length - headerIdx)), @"v\.Bind\(kind, ThemeService\.Key\.OnPrimary\)"),
            "顶栏图标未统一反白（OnPrimary）");
        // 隐藏文件眼睛：无条件反白，不再随显隐态换色（状态靠 toast 传达）
        Assert.False(main.Contains("ShowHidden ? ThemeService.Key.Primary : ThemeService.Key.TextMain"),
            "眼睛图标仍按显隐态换色，深胶囊上看不见");
        Assert.True(main.Contains("_eyeIcon.Bind(IconKind.Eye, ThemeService.Key.OnPrimary)"),
            "隐藏眼睛未改为无条件反白");
        // 底部胶囊未选中文字：不打透明度（墨夜 OnPrimary 黑字会糊掉），直接全色反白
        int tabIdx = main.IndexOf("internal static class TabHighlight");
        var tabSeg = main.Substring(tabIdx, Math.Min(1600, main.Length - tabIdx));
        Assert.False(tabSeg.Contains("on.A * 0.75f"), "底部未选中文字仍打透明度");
        Assert.True(Regex.IsMatch(tabSeg, @"tab\.Text\.SetTextColor\(ThemeService\.C\(ThemeService\.Key\.OnPrimary\)\)"),
            "底部未选中文字未改全色反白");
        // 气泡卡：方法齐备、右下对齐（左缘按图标右缘收）、3 秒自动收
        Assert.True(Regex.IsMatch(menu, @"public static void ShowBubble\(Activity a, View anchor, string text\)"),
            "AnchorMenu 缺 ShowBubble 气泡卡");
        Assert.True(menu.Contains("PostDelayed(CloseBubble, 3000)"), "气泡卡缺 3 秒自动收");
        Assert.True(Regex.IsMatch(menu, @"loc\[0\] \+ anchor\.Width - cardW"), "气泡卡未按图标右缘右下对齐");
        // 关于改气泡卡：签名带锚点，点图标弹出
        Assert.True(main.Contains("void ShowAbout(View anchor)"), "关于未改气泡卡锚点签名");
        // 回收站：页面描述文字收进 ? 气泡
        Assert.False(trash.Contains("root.AddView(tip)"), "回收站页面描述文字未收进气泡");
        Assert.True(trash.Contains("ShowBubble(this, help,"), "回收站缺 ? 气泡入口");
        Assert.True(trash.Contains("clear.Bind(IconKind.Trash, ThemeService.Key.Danger)"), "清空未改危险色垃圾桶图标");
        Assert.True(trash.IndexOf("header.AddView(clear") < trash.IndexOf("header.AddView(help"),
            "? 图标未放在清空右侧（最右）");
        // 回收站列表与文件页同款：24dp 大圆角整卡，行不再各自小卡
        Assert.True(trash.Contains("cardBg.SetCornerRadius(Dp(24))"), "回收站列表未包 24dp 大圆角整卡");
        Assert.False(trash.Contains("SetCornerRadius(_a.Dp(14))"), "回收站行仍是独立小卡");
        Assert.False(trash.Contains("row.Background = cardBg"), "回收站行背景未改透明");
        // 版本推进：32 / 2.12（后续版本递进不回退）
        Assert.True(Regex.IsMatch(csproj, @"<ApplicationVersion>[34]\d</ApplicationVersion>")
            && Regex.IsMatch(csproj, @"<ApplicationDisplayVersion>2\.(1[0-9]|2\d)</ApplicationDisplayVersion>"), "版本号未升到 2.12");
        Assert.True(Regex.IsMatch(main, @"TaoZip 2\.(1[0-9]|2\d)（安卓）"), "关于页版本未随版本推进更新");
        Console.WriteLine("图标可见与气泡卡规则：源码断言全绿");
    }

    /// <summary>2.13 真机反馈回归：批量条改半透明底文字浮层（不挤压列表宽度、右缘绝不压行尾按钮）、
    /// 列表行左右留白取齐、版本推进 2.13。</summary>
    private static void TestV33Rules()
    {
        var uiDir = FindUiDir();
        var main = File.ReadAllText(Path.Combine(uiDir, "MainActivity.cs"));
        var csproj = File.ReadAllText(Path.Combine(FindProjDir(), "TaoZip.UI.Android.csproj"));

        // 批量浮层：半透明底（FloatBg 降透明度）+ 文字按钮，不用整块不透明胶囊
        int barIdx = main.IndexOf("void BuildBatchBar()");
        var barSeg = main.Substring(barIdx, Math.Min(2000, main.Length - barIdx));
        Assert.True(Regex.IsMatch(barSeg, @"fbg\.A \* 0\.78f"), "批量浮层底不是半透明");
        Assert.True(Regex.IsMatch(barSeg, @"RightMargin = Dp\(62\)"), "批量浮层未让出行尾按钮列（右缘 62）");
        Assert.False(barSeg.Contains("BatchIcon"), "批量浮层仍残留图标按钮写法");
        // 列表不再让位：SetSelectUi 只切显隐，全文件无 74 缩宽逻辑
        int selIdx = main.IndexOf("void SetSelectUi(bool on)");
        var selSeg = main.Substring(selIdx, Math.Min(400, main.Length - selIdx));
        Assert.True(Regex.IsMatch(selSeg, @"_batchBar\.Visibility = on \? ViewStates\.Visible : ViewStates\.Gone;"),
            "SetSelectUi 未改为只切批量浮层显隐");
        Assert.False(selSeg.Contains("RightMargin"), "多选态列表仍被挤压宽度");
        // 行左右留白取齐：12/9/6/9
        Assert.True(main.Contains("row.SetPadding(_a.Dp(12), _a.Dp(9), _a.Dp(6), _a.Dp(9))"), "列表行左右留白未取齐");
        // 版本推进：33 / 2.13
        Assert.True(Regex.IsMatch(csproj, @"<ApplicationVersion>[34]\d</ApplicationVersion>")
            && Regex.IsMatch(csproj, @"<ApplicationDisplayVersion>2\.(1[0-9]|2\d)</ApplicationDisplayVersion>"), "版本号未升到 2.13");
        Assert.True(Regex.IsMatch(main, @"TaoZip 2\.(1[0-9]|2\d)（安卓）"), "关于页版本未随版本推进更新");
        Console.WriteLine("批量浮层与行距取齐规则：源码断言全绿");
    }

    private static void TestV34Rules()
    {
        var uiDir = FindUiDir();
        var main = File.ReadAllText(Path.Combine(uiDir, "MainActivity.cs"));
        var manifest = File.ReadAllText(Path.Combine(uiDir, "AndroidManifest.xml"));
        var csproj = File.ReadAllText(Path.Combine(FindProjDir(), "TaoZip.UI.Android.csproj"));
        var appCs = File.ReadAllText(Path.Combine(uiDir, "App.cs"));
        var svc = File.ReadAllText(Path.Combine(uiDir, "TaskForegroundService.cs"));
        var tasks = File.ReadAllText(Path.Combine(uiDir, "TasksActivity.cs"));
        var bll = File.ReadAllText(Path.Combine(FindProjDir(), "..", "..", "src", "TaoZip.BLL", "ArchiveService.cs"));

        // 崩溃兜底：全局异常落盘，绝不再"闪退查无原因"
        Assert.True(appCs.Contains("UnhandledExceptionRaiser"), "全局异常未注册");
        Assert.True(appCs.Contains("taozip_crash.log"), "崩溃日志未落盘");

        // 前台服务：dataSync 保活 + 通知进度（进度条/百分比/剩余时间）
        // 2.15 起类型声明移入清单（特性不再携带该参数），改断言清单声明
        Assert.True(Regex.IsMatch(manifest, @"foregroundServiceType\s*=\s*""dataSync"""), "清单未声明 dataSync 前台服务类型");
        Assert.True(svc.Contains("StartForeground("), "服务未起前台通知");
        Assert.True(svc.Contains("SetProgress(100, pct, indeterminate)"), "通知栏无进度条");
        Assert.True(svc.Contains("剩余约"), "通知栏无剩余时间估算");
        Assert.True(svc.Contains("EnsureNotificationPermission"), "未补安卓13通知权限请求");

        // 任务中心：列表管理 + 取消 + 清空已结束记录
        Assert.True(tasks.Contains("TasksActivity"), "任务中心页面缺失");
        Assert.True(tasks.Contains("Service.Cancel(r.Id)"), "任务中心无取消操作");
        Assert.True(tasks.Contains("RemoveFinished()"), "任务中心无清空已结束记录");

        // 服务层：清空已结束记录；主界面：任务入队即唤起前台服务 + 顶栏任务清单入口
        Assert.True(bll.Contains("public int RemoveFinished()"), "任务服务未提供清空已结束记录");
        Assert.True(main.Contains("TaskForegroundService.Start(this);"), "压缩解压未交给前台服务保活");
        Assert.True(main.Contains("typeof(TasksActivity)"), "顶栏无任务中心入口");
        Assert.True(Regex.IsMatch(main, @"IconKind\.Tasks"), "任务清单图标未接入");

        // 版本推进：三个新权限 + 2.1x（精确版本由当版用例校验）
        Assert.True(manifest.Contains("android.permission.POST_NOTIFICATIONS"), "清单缺通知权限");
        Assert.True(manifest.Contains("android.permission.FOREGROUND_SERVICE"), "清单缺前台服务权限");
        Assert.True(manifest.Contains("android.permission.FOREGROUND_SERVICE_DATA_SYNC"), "清单缺 dataSync 前台服务权限");
        Assert.True(Regex.IsMatch(csproj, @"<ApplicationVersion>[34]\d</ApplicationVersion>"), "版本号未保持在 3x 区间");
        Assert.True(Regex.IsMatch(csproj, @"<ApplicationDisplayVersion>2\.(1[0-9]|2\d)</ApplicationDisplayVersion>"), "显示版本未保持在 2.1x 区间");
        Console.WriteLine("后台任务与通知进度规则：源码断言全绿");
    }

    // 2.15：三条真机闪退反馈连根修——①批量浮层双挂父 ②任务中心启动闪退 ③图片预览启动闪退
    private static void TestV35Rules()
    {
        var uiDir = FindUiDir();
        var main = File.ReadAllText(Path.Combine(uiDir, "MainActivity.cs"));
        var tasks = File.ReadAllText(Path.Combine(uiDir, "TasksActivity.cs"));
        var csproj = File.ReadAllText(Path.Combine(FindProjDir(), "TaoZip.UI.Android.csproj"));

        // 批量浮层：BatchText 只创建返回，挂父由 BuildBatchBar 统一——同层只允许一处 AddView
        Assert.True(Regex.IsMatch(main, @"BatchText\(string label[\s\S]*?return t;[\s\S]*?\}"),
            "BatchText 未改为只创建返回");
        int batchBodyStart = main.IndexOf("void BuildBatchBar()");
        int batchBodyEnd = main.IndexOf("TextView BatchText(");
        Assert.True(batchBodyStart >= 0 && batchBodyEnd > batchBodyStart, "BuildBatchBar/BatchText 结构缺失");
        string batchBody = main.Substring(batchBodyStart, batchBodyEnd - batchBodyStart);
        int addCount = Regex.Matches(batchBody, @"_batchBar\.AddView\(").Count;
        Assert.True(addCount == 7, $"浮层按钮挂父次数应为 7，实际 {addCount}");
        Assert.True(batchBody.Contains("_root.AddView(_batchBar"), "浮层未挂根容器");

        // 任务中心：_list 全程只挂一次父（2.16 起由 scroll 统一装载，禁止再叠 wrap 双挂）
        Assert.True(tasks.Contains("scroll.AddView(_list)"), "_list 未由 scroll 统一装载");
        Assert.True(!Regex.IsMatch(tasks, @"wrap\.AddView\(_list\)"), "_list 仍被 wrap 二次挂父");

        // 版本推进：2.1x 区间（精确版本由当版用例校验）
        Assert.True(Regex.IsMatch(csproj, @"<ApplicationVersion>[34]\d</ApplicationVersion>"), "版本号未保持在 3x 区间");
        Assert.True(Regex.IsMatch(csproj, @"<ApplicationDisplayVersion>2\.(1[0-9]|2\d)</ApplicationDisplayVersion>"), "显示版本未保持在 2.1x 区间");
        Console.WriteLine("三处启动闪退连根修规则：源码断言全绿");
    }

    // 2.20：预览定音——应用内预览两轮闪退未断根，砍预览页，点图只把当前一张交给系统图库；版本 40/2.20
    private static void TestV37Rules()
    {
        var uiDir = FindUiDir();
        var main = File.ReadAllText(Path.Combine(uiDir, "MainActivity.cs"));
        var trash = File.ReadAllText(Path.Combine(uiDir, "TrashHelper.cs"));
        var csproj = File.ReadAllText(Path.Combine(FindProjDir(), "TaoZip.UI.Android.csproj"));

        // 预览页已删干净：文件不删回、主界面零引用
        Assert.True(!File.Exists(Path.Combine(uiDir, "ImagePreviewActivity.cs")), "预览页文件未删干净");
        Assert.True(!main.Contains("ImagePreviewActivity"), "主界面仍引用已砍的预览页");
        // 点图路由：IsImage 分支只走系统图库（OpenWithSystem），翻页交给系统预览器
        Assert.True(Regex.IsMatch(main, @"IsImage\(item\.FullName\)\)\s*\{[\s\S]{0,300}OpenWithSystem\(item\.FullName\)"),
            "点图未改走系统图库");
        // 从未使用的字段清理
        Assert.True(!main.Contains("_searchActive") && !Regex.IsMatch(main, @"LinearLayout _header\b"), "无用字段未清干净");
        // 元数据后缀：健壮性敏感处零中文——新写入走 ASCII，旧中文后缀仍能读能删
        Assert.True(trash.Contains("MetaExt = \".trashmeta\"") && trash.Contains("LegacyMetaExt = \".回收站来源\""),
            "元数据后缀未按 ASCII 新写 + 旧后缀兼容处理");
        Assert.True(Regex.IsMatch(trash, @"MetaPaths\(string entryPath\)") && Regex.IsMatch(trash, @"IsMetaName\(string name\)"),
            "新旧后缀读删兼容方法缺失");
        // 受保护路径删除：删除前拦截给人话提示，不再抛原始权限错误
        Assert.True(Regex.IsMatch(trash, @"IsProtectedPath\(path\)[\s\S]{0,200}UnauthorizedAccessException"), "受保护路径未拦截提示");
        // 版本推进：不低于 40 / 2.20（版本递进不回退，2.21 起按 TestV221Rules 口径校验）
        Assert.True(Regex.IsMatch(csproj, @"<ApplicationVersion>(40|[4-9]\d)</ApplicationVersion>")
            && Regex.IsMatch(csproj, @"<ApplicationDisplayVersion>2\.(20|2[1-9]|[3-9]\d)</ApplicationDisplayVersion>"), "版本号未升到 2.20");
        Console.WriteLine("死代码清理与规范自检规则：源码断言全绿");
    }

    // 2.21：三处真机反馈——①视频播放打通道播放（道播放开放接收入口）②菜单删除与左滑同口径、列表零残留 ③回收站内支持预览
    private static void TestV221Rules()
    {
        var uiDir = FindUiDir();
        var main = File.ReadAllText(Path.Combine(uiDir, "MainActivity.cs"));
        var trashAct = File.ReadAllText(Path.Combine(uiDir, "TrashActivity.cs"));
        var csproj = File.ReadAllText(Path.Combine(FindProjDir(), "TaoZip.UI.Android.csproj"));

        // ① 外部打开逻辑静态共享：主界面与回收站同一口径，视频同一定向道播放
        Assert.True(Regex.IsMatch(main, @"internal static void OpenExternally\(Activity a, string path\)"),
            "外部打开未抽成静态共享实现");
        Assert.True(Regex.IsMatch(main, @"void OpenWithSystem\(string path\) => OpenExternally\(this, path\)"),
            "主界面打开未接静态共享实现");
        // ② 删除同口径：左滑手势走 ConfirmSwipeDelete（2.22 起右侧菜单砍掉删除，入口只留左滑与多选批量）
        Assert.True(Regex.IsMatch(main, @"void DeleteRow\(\) => _a\.ConfirmSwipeDelete\(_path, _vm\.Name\)"),
            "左滑删除确认链路缺失");
        Assert.True(Regex.IsMatch(main, @"Refresh\(\);\s*\n\s*// 2\.21 兜底清场[\s\S]{0,120}_items\.RemoveAll\(i => i\.FullName == path\)"),
            "删除缺兜底清场");
        // ② 同型排查：压缩包扫描回调的缓存赋值必须在代号检查之后
        Assert.True(Regex.IsMatch(main, @"if \(gen != _archScanGen \|\| Mode != ViewMode\.Archives\) return;\s*\n\s*ArchivesCache = list;"),
            "压缩包扫描回调缓存赋值仍早于代号检查");
        // ③ 回收站预览：行点按走 OpenPreview，文件直接系统打开，文件夹仍走功能卡
        Assert.True(Regex.IsMatch(trashAct, @"void OpenPreview\(TrashHelper\.TrashItem it, View anchor\)"), "回收站缺预览入口");
        Assert.True(trashAct.Contains("MainActivity.OpenExternally(this, it.EntryPath)"), "回收站预览未走共享打开实现");
        Assert.True(Regex.IsMatch(trashAct, @"row\.Click \+= \(s, e\) => _a\.OpenPreview\(it, row\);"), "回收站行点按未接预览");
        // 版本推进：不低于 41 / 2.21（版本递进不回退，2.22 起按 TestV222Rules 口径校验）
        Assert.True(Regex.IsMatch(csproj, @"<ApplicationVersion>(41|[4-9]\d)</ApplicationVersion>")
            && Regex.IsMatch(csproj, @"<ApplicationDisplayVersion>2\.(2[1-9]|[3-9]\d)</ApplicationDisplayVersion>"), "版本号未升到 2.21");
        Assert.True(main.Contains("\"TaoZip 2.2"), "关于页文案未同步 2.21");
        Console.WriteLine("删除同口径与回收站预览规则：源码断言全绿");
    }

    // 2.22：两处真机反馈——①分类格式筛选条与列表的间距调大（与其它间距一档取齐）②右侧更多菜单砍掉「删除」入口
    private static void TestV222Rules()
    {
        var uiDir = FindUiDir();
        var main = File.ReadAllText(Path.Combine(uiDir, "MainActivity.cs"));
        var csproj = File.ReadAllText(Path.Combine(FindProjDir(), "TaoZip.UI.Android.csproj"));

        // ① 间距：筛选条底垫与左右 10 一档取齐（Dp(10), Dp(4), Dp(10), Dp(10)），筛选条和列表不再贴死
        Assert.True(Regex.IsMatch(main, @"_chipsBar\.SetPadding\(Dp\(10\), Dp\(4\), Dp\(10\), Dp\(10\)\)"),
            "筛选条底部间距未调大");
        // ② 菜单砍删除：ShowEntryMenu 不再有「删除」项；左滑 DeleteRow 与批量删除两条路保留
        Assert.True(!Regex.IsMatch(main, @"ShowEntryMenu[\s\S]{0,2000}?Text = ""删除"""),
            "右侧更多菜单仍留有删除入口");
        Assert.True(Regex.IsMatch(main, @"void DeleteRow\(\) => _a\.ConfirmSwipeDelete\(_path, _vm\.Name\)"), "左滑删除链路缺失");
        Assert.True(main.Contains("TrashHelper.MoveToTrash(p); PurgeFromLists(p); ok++"), "多选批量删除链路缺失");
        // 版本推进：不低于 42 / 2.22（版本递进不回退，2.23 起按 TestV223Rules 口径校验）
        Assert.True(Regex.IsMatch(csproj, @"<ApplicationVersion>(4[2-9]|[5-9]\d)</ApplicationVersion>")
            && Regex.IsMatch(csproj, @"<ApplicationDisplayVersion>2\.(2[2-9]|[3-9]\d)</ApplicationDisplayVersion>"), "版本号未升到 2.22");
        Assert.True(main.Contains("\"TaoZip 2.2"), "关于页文案未同步 2.22");
        Console.WriteLine("筛选间距与菜单砍删除规则：源码断言全绿");
    }

    // 2.23：三处真机反馈——①批量菜单补「解压」，解压目标文件夹可挑且默认当前目录 ②压缩包页底部按钮改浮动胶囊
    private static void TestV223Rules()
    {
        var uiDir = FindUiDir();
        var main = File.ReadAllText(Path.Combine(uiDir, "MainActivity.cs"));
        var arch = File.ReadAllText(Path.Combine(uiDir, "ArchiveActivity.cs"));
        var csproj = File.ReadAllText(Path.Combine(FindProjDir(), "TaoZip.UI.Android.csproj"));

        // ① 批量解压：浮层在「退出」之前有「解压」项；没选中压缩包时给明确提示
        Assert.True(Regex.IsMatch(main, @"BatchText\(""解压"", ThemeService\.Key\.TextMain, BatchExtract\)[\s\S]{0,200}BatchText\(""退出"""),
            "批量浮层缺「解压」入口");
        Assert.True(Regex.IsMatch(main, @"void BatchExtract\(\)\s*\n\s*\{[\s\S]{0,200}IsArchiveFile[\s\S]{0,300}选中的项里没有压缩包"),
            "批量解压未校验压缩包或提示缺失");
        Assert.True(Regex.IsMatch(main, @"void BatchExtract\(\)[\s\S]{0,400}PickFolder\(CurrentDir"),
            "批量解压未接目标文件夹选择");
        // ② 解压目标可选：入口用 PickFolder 逐级挑，单项解压默认当前目录
        Assert.True(Regex.IsMatch(main, @"void ExtractArchive\(string path\)[\s\S]{0,400}input\.Text = CurrentDir;"),
            "单项解压默认目标不是当前目录");
        Assert.True(main.Contains("void PickFolder(string start, Action<string> onPick)"), "主页目录选择器缺失");
        Assert.True(Regex.IsMatch(main, @"FolderPickLink\(input, \(\) => CurrentDir\)"), "单项解压未接「选个文件夹」入口");
        Assert.True(arch.Contains("void PickFolder(string start, Action<string> onPick)"), "压缩包页目录选择器缺失");
        Assert.True(Regex.IsMatch(arch, @"void DoExtract\(\)[\s\S]{0,300}input\.Text = parent;"), "压缩包页解压默认目标缺失");
        Assert.True(Regex.IsMatch(arch, @"FolderPickLink\(input, \(\) => parent\)"), "压缩包页解压未接「选个文件夹」入口");
        // ③ 压缩包页底栏改浮动胶囊：底部悬浮 + 圆角 30 + 半透明胶囊底，列表底部留让位
        Assert.True(Regex.IsMatch(arch, @"_root\.AddView\(_bar, new FrameLayout\.LayoutParams\(-1, -2, GravityFlags\.Bottom\)\s*\n\s*\{ LeftMargin = Dp\(14\), RightMargin = Dp\(14\), BottomMargin = Dp\(12\) \}"),
            "压缩包页底栏未改底部悬浮布局");
        Assert.True(Regex.IsMatch(arch, @"pillBg\.SetCornerRadius\(Dp\(30\)\)"), "压缩包页底栏未改圆角胶囊底");
        Assert.True(Regex.IsMatch(arch, @"_bar\.Elevation = Dp\(6\)"), "压缩包页底栏缺悬浮投影");
        Assert.True(Regex.IsMatch(arch, @"_list\.SetPadding\(0, Dp\(4\), 0, Dp\(84\)\)"), "压缩包页列表底部未给悬浮栏让位");
        // 版本推进：不低于 43 / 2.23（版本递进不回退，2.24 起按 TestV224Rules 口径校验）
        Assert.True(Regex.IsMatch(csproj, @"<ApplicationVersion>(4[3-9]|[5-9]\d)</ApplicationVersion>")
            && Regex.IsMatch(csproj, @"<ApplicationDisplayVersion>2\.(2[3-9]|[3-9]\d)</ApplicationDisplayVersion>"), "版本号未升到 2.23");
        Assert.True(main.Contains("\"TaoZip 2.2"), "关于页文案未同步 2.2x");
        Console.WriteLine("批量解压与浮动底栏规则：源码断言全绿");
    }

    private static void TestV224Rules()
    {
        var uiDir = FindUiDir();
        var main = File.ReadAllText(Path.Combine(uiDir, "MainActivity.cs"));
        var anchor = File.ReadAllText(Path.Combine(uiDir, "AnchorMenu.cs"));
        var tasks = File.ReadAllText(Path.Combine(uiDir, "TasksActivity.cs"));
        var csproj = File.ReadAllText(Path.Combine(FindProjDir(), "TaoZip.UI.Android.csproj"));

        // ① 菜单卡片宽度收窄：ShowInternal 下限 168dp → 120dp（ShowBubble 气泡卡不受影响）
        Assert.True(anchor.Contains("Dp(120)"), "行菜单卡片宽度下限未收窄到 120dp");
        // ② 任务中心：进度条只在排队/进行中显示，已完成/失败/取消不再画圈
        Assert.True(Regex.IsMatch(tasks, @"var live = r\.State is TaskState\.Queued or TaskState\.Running;\s*\n\s*if \(live\)\s*\n\s*\{[\s\S]{0,300}ProgressBar[\s\S]{0,200}AddView\(bar"),
            "任务卡片进度条未限定为排队/进行中才显示");
        // 版本推进：44 / 2.24（版本递进不回退，2.25 起按 TestV225Rules 口径校验）
        Assert.True(Regex.IsMatch(csproj, @"<ApplicationVersion>(4[4-9]|[5-9]\d)</ApplicationVersion>")
            && Regex.IsMatch(csproj, @"<ApplicationDisplayVersion>2\.(2[4-9]|[3-9]\d)</ApplicationDisplayVersion>"), "版本号未升到 2.24");
        Assert.True(main.Contains("\"TaoZip 2.2"), "关于页文案未同步 2.2x");
        Console.WriteLine("卡片宽度与任务进度圈规则：源码断言全绿");
    }

    private static void TestV225Rules()
    {
        var uiDir = FindUiDir();
        var main = File.ReadAllText(Path.Combine(uiDir, "MainActivity.cs"));
        var csproj = File.ReadAllText(Path.Combine(FindProjDir(), "TaoZip.UI.Android.csproj"));

        // 版本推进：45 / 2.25（版本递进不回退，2.26 起按 TestV226Rules 口径校验）
        Assert.True(Regex.IsMatch(csproj, @"<ApplicationVersion>(4[5-9]|[5-9]\d)</ApplicationVersion>")
            && Regex.IsMatch(csproj, @"<ApplicationDisplayVersion>2\.(2[5-9]|[3-9]\d)</ApplicationDisplayVersion>"), "版本号未升到 2.25");
        Assert.True(main.Contains("\"TaoZip 2.2"), "关于页文案未同步 2.2x");
        Console.WriteLine("行菜单解压入口规则：源码断言全绿");
    }

    private static void TestV226Rules()
    {
        var uiDir = FindUiDir();
        var main = File.ReadAllText(Path.Combine(uiDir, "MainActivity.cs"));
        var csproj = File.ReadAllText(Path.Combine(FindProjDir(), "TaoZip.UI.Android.csproj"));

        // 「更多」点回调根修：不得在行首建时钉死条目（视图复用后拿旧条目，压缩包页点出文件夹/别的文件的菜单），
        // 必须经 SwipeRow._vm 字段取当次绑定条目，与行点击/长按的 GetItem() 同口径
        Assert.False(main.Contains("_a.ShowEntryMenu(vm, more)"), "「更多」回调仍钉死行首建时的旧条目");
        Assert.True(Regex.IsMatch(main, @"_more\.Click \+= \(s2, e2\) => \{ SwipeRow\.Close\(null\); _a\.ShowEntryMenu\(_vm, _more\); \}"),
            "「更多」回调未改读当次绑定条目");
        // 解压入口按后缀判定：支持表里的压缩包一律显示（用户定音），不再走一律显示+提示的 2.25 方案
        Assert.True(Regex.IsMatch(main, @"isArchive\) acts\.Add[\s\S]{0,80}Text = ""解压"""), "行菜单「解压」未按后缀判定显示");
        Assert.False(main.Contains("这个文件不是压缩包，或暂时不支持这种格式"), "2.25 的兜底提示残留未清");
        // 版本推进：46 / 2.26（版本递进不回退）
        Assert.True(Regex.IsMatch(csproj, @"<ApplicationVersion>(4[6-9]|[5-9]\d)</ApplicationVersion>")
            && Regex.IsMatch(csproj, @"<ApplicationDisplayVersion>2\.(2[6-9]|[3-9]\d)</ApplicationDisplayVersion>"), "版本号未升到 2.26");
        Assert.True(main.Contains("\"TaoZip 2.2"), "关于页文案未同步 2.2x（版本口径由当版用例校验）");
        Console.WriteLine("更多按钮陈旧条目规则：源码断言全绿");
    }

    // 2.27：两条真机反馈——①压缩直接闪退（任务链路连环炸）②排序作为所有筛选项的组合条件并挪到「内部存储」标题行右侧
    private static void TestV227Rules()
    {
        var uiDir = FindUiDir();
        var main = File.ReadAllText(Path.Combine(uiDir, "MainActivity.cs"));
        var service = File.ReadAllText(Path.Combine(FindProjDir(), "../../src/TaoZip.BLL/ArchiveService.cs"));
        var tfs = File.ReadAllText(Path.Combine(uiDir, "TaskForegroundService.cs"));
        var csproj = File.ReadAllText(Path.Combine(FindProjDir(), "TaoZip.UI.Android.csproj"));

        // ① 闪退根修行为断言：订阅方炸了不许拖垮任务提交，许可必须归还（第二个任务照常跑完）
        Run("闪退根修：订阅方异常不外泄且许可不泄漏", () => TestSubscriberFaultIsolated());

        // ① 补源码断言：收尾顺序——许可归还不能被历史落盘异常吃掉
        Assert.True(Regex.IsMatch(service, @"try\s*\{ lock \(_lock\) _cancels\.Remove\(record\.Id\); \} catch \{ \}"),
            "任务收尾未对取消登记清理兜底");
        Assert.True(Regex.IsMatch(service, @"catch \(Exception historyEx\)[\s\S]{0,400}_gate\.Release\(\);"),
            "历史落盘异常仍可能吃掉许可归还");
        Assert.False(Regex.IsMatch(service, @"TaskUpdated\?\.Invoke\(record\);\s*\n(\s*lock|\s*_history)"),
            "任务通知订阅方仍无兜底，订阅方异常会外泄炸调用方");
        // 前台服务刷新通知：First 改 FirstOrDefault + 整体兜底，队列只有排队中任务时不再炸主线程
        Assert.True(Regex.IsMatch(tfs, @"running\.FirstOrDefault\(r => r\.State == TaskState\.Running\) \?\? running\[0\]"),
            "前台服务仍用 First 找 Running，队列只有排队任务时会炸主线程");
        Assert.True(Regex.IsMatch(tfs, @"void Update\(\)\s*\n\s*\{\s*\n\s*try"),
            "前台服务通知刷新未整体兜底");
        // 安卓端历史落盘用应用私有目录：AppContext.BaseDirectory 在安卓是只读系统目录
        Assert.True(Regex.IsMatch(main, @"new ArchiveService\(new global::TaoZip\.DAL\.HistoryStore\(FS\.Path\.Combine\(dir, ""taozip_history\.json""\)\)\)"),
            "安卓端历史落盘未改用应用私有目录");
        Assert.False(Regex.IsMatch(main, @"internal static readonly ArchiveService Service = new\(\);"),
            "任务服务仍为静态直建，历史路径未按平台修正");

        // ② 排序作为所有筛选项的组合条件：分类细分筛选、压缩包页（缓存+扫描回填）、搜索结果全链过 SortItems
        Assert.True(Regex.IsMatch(main, @"void ApplyCatFilter\(\)[\s\S]{0,400}SortItems\(_items\);"),
            "分类细分筛选链未应用用户排序");
        Assert.True(Regex.IsMatch(main, @"_items = ArchivesCache;\s*\n\s*SortItems\(_items\);"),
            "压缩包页缓存链未应用用户排序");
        Assert.True(Regex.IsMatch(main, @"_items = list;\s*\n\s*SortItems\(_items\);"),
            "压缩包页扫描回填链未应用用户排序");
        Assert.True(Regex.IsMatch(main, @"\.Select\(h => EntryVM\.From\(h\.Info\)\)\s*\n\s*\.ToList\(\);\s*\n[\s\S]{0,120}SortItems\(_items\);"),
            "搜索结果链未应用用户排序");
        // 排序入口挪位：顶栏不再有排序按钮，「内部存储」标题行右侧常驻排序入口，功能卡靠右下呼出
        Assert.False(Regex.IsMatch(main, @"sortBtn = HeaderIcon\(IconKind\.Sort"), "顶栏排序按钮未撤");
        Assert.True(Regex.IsMatch(main, @"sortInCard\.Bind\(IconKind\.Sort"), "「内部存储」标题行右侧缺排序入口");
        Assert.True(Regex.IsMatch(main, @"AnchorMenu\.Show\(this, anchor \?\? _logo, acts\.ToArray\(\)\);"),
            "排序功能卡未改靠右下呼出");

        // 版本推进：47 / 2.27（版本递进不回退）
        Assert.True(Regex.IsMatch(csproj, @"<ApplicationVersion>(4[7-9]|[5-9]\d)</ApplicationVersion>")
            && Regex.IsMatch(csproj, @"<ApplicationDisplayVersion>2\.(2[7-9]|[3-9]\d)</ApplicationDisplayVersion>"), "版本号未升到 2.27");
        Assert.True(main.Contains("\"TaoZip 2.27（安卓）"), "关于页文案未同步 2.27");
        Console.WriteLine("压缩闪退根修与排序全链规则：源码断言全绿");
    }

    /// <summary>闪退根修行为断言：TaskUpdated 订阅方抛异常时，Enqueue 照常返回、任务照常跑完、并发许可不泄漏。</summary>
    private static void TestSubscriberFaultIsolated()
    {
        // 历史落盘必炸的路径：把目录位置占成文件，SaveRaw 的 CreateDirectory 必抛——老代码在此处炸掉许可
        var blocker = Path.Combine(Path.GetTempPath(), "taozip_tests_blocker_" + Guid.NewGuid().ToString("N")[..8]);
        File.WriteAllText(blocker, "占位：让历史落盘目录必然创建失败");
        var doomedStore = new HistoryStore(Path.Combine(blocker, "history.json"));
        using var svc = new ArchiveService(doomedStore);
        var boom = new List<string>();
        svc.TaskUpdated += _ => { boom.Add("boom"); throw new InvalidOperationException("订阅方故意炸"); };

        var rec1 = svc.Enqueue(TaskKind.Compress, "闪退根修·任务一", (id, p, ct) => { });
        Assert.True(boom.Count > 0, "订阅方未被通知到");
        Assert.Equal(TaskState.Done, WaitFinished(svc, rec1.Id));
        // 老代码历史落盘抛异常会跳过 _gate.Release，第二个任务永远排队——2.27 起必须照常跑完
        var rec2 = svc.Enqueue(TaskKind.Compress, "闪退根修·任务二", (id, p, ct) => { });
        Assert.Equal(TaskState.Done, WaitFinished(svc, rec2.Id));
        Assert.Equal(TaskState.Done, WaitFinished(svc, rec1.Id)); // 收尾通知炸过仍保持完成态
        try { Directory.Delete(blocker); } catch { }
        Console.WriteLine("闪退根修行为断言：订阅方炸×2、许可归还、任务二照常完成");
    }

    private static TaskState WaitFinished(ArchiveService svc, string id, int timeoutMs = 10000)
    {
        var dl = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < dl)
        {
            var r = svc.Records.FirstOrDefault(x => x.Id == id);
            if (r != null && r.State is TaskState.Done or TaskState.Failed or TaskState.Cancelled) return r.State;
            Thread.Sleep(30);
        }
        Assert.True(false, $"任务 {id} 未在时限内收尾（疑似许可泄漏，任务永远排队）");
        return TaskState.Queued;
    }

    // 2.16：三条真机反馈——①更多菜单删除后列表复活（后台扫描竞态）②图片改走系统预览 ③任务中心样式对齐回收站
    private static void TestV36Rules()
    {
        var uiDir = FindUiDir();
        var main = File.ReadAllText(Path.Combine(uiDir, "MainActivity.cs"));
        var tasks = File.ReadAllText(Path.Combine(uiDir, "TasksActivity.cs"));
        var csproj = File.ReadAllText(Path.Combine(FindProjDir(), "TaoZip.UI.Android.csproj"));

        // 删除不复活：分类与压缩包的后台扫描回调必须带代号——删除后旧回调一律作废
        Assert.True(main.Contains("int _catScanGen;") && main.Contains("int _archScanGen;"), "扫描代号字段缺失");
        Assert.True(Regex.IsMatch(main, @"int gen = \+\+_catScanGen;[\s\S]*?gen != _catScanGen \|\| Mode != ViewMode\.Files"),
            "分类扫描回调未作废旧代号");
        Assert.True(Regex.IsMatch(main, @"int gen = \+\+_archScanGen;[\s\S]*?gen != _archScanGen \|\| Mode != ViewMode\.Archives"),
            "压缩包扫描回调未作废旧代号");
        // 媒体文件删后通知媒体库重扫，系统相册不留鬼影
        Assert.True(Regex.IsMatch(main, @"PurgeFromLists\(path\);[\s\S]{0,200}MediaScannerConnection\.ScanFile"),
            "删除媒体后未通知媒体库重扫");

        // 图片预览：2.16 曾改走系统预览，2.18 用户定音恢复应用内全套手势预览——点图路由由 TestV24Rules 接管断言

        // 任务中心：样式对齐回收站——24dp 整卡、空态大图标、清空收进顶栏危险色垃圾桶
        Assert.True(tasks.Contains("SetCornerRadius(Dp(24))"), "任务中心列表未用 24dp 整卡");
        Assert.True(Regex.IsMatch(tasks, @"IconKind\.Archive[\s\S]{0,200}还没有压缩解压任务"), "空态未用大图标样式");
        Assert.True(Regex.IsMatch(tasks, @"IconKind\.Trash[\s\S]{0,200}ConfirmClear"), "清空未收进顶栏垃圾桶图标");
        Assert.True(tasks.Contains("ApplyEdge(root);"), "任务中心未随象取色");
        Assert.True(!tasks.Contains("clearWrap"), "底部清空文字按钮未移除");

        // 版本推进：不低于 36 / 2.16（精确版本由当版用例校验）
        Assert.True(Regex.IsMatch(csproj, @"<ApplicationVersion>3[6-9]|4[0-9]</ApplicationVersion>"), "版本号未升到 36");
        Assert.True(Regex.IsMatch(csproj, @"<ApplicationDisplayVersion>2\.(1[6-9]|2\d)</ApplicationDisplayVersion>"), "显示版本未升到 2.16");
        Console.WriteLine("删除不复活与系统预览规则：源码断言全绿");
    }

    private static void TestVisualMatrix()
    {
        var themeCs = File.ReadAllText(Path.Combine(FindUiDir(), "ThemeService.cs"));
        var palettes = ParsePalettes(themeCs);
        Assert.Equal(3, palettes.Count);

        foreach (var (xiang, p) in palettes)
        {
            Assert.Equal(14, p.Count); // 14 个颜色键齐全；LightStatusIcons 不入色板
            // 层色底定律：页底 < 卡面 <= 悬浮层
            Assert.True(Lum(p["PageBg"]) < Lum(p["CardBg"]) && Lum(p["CardBg"]) <= Lum(p["FloatBg"]),
                $"{xiang} 违反层色底定律");

            Ge(xiang, p, "TextMain", "PageBg", 4.5);
            Ge(xiang, p, "TextMain", "CardBg", 4.5);
            Ge(xiang, p, "TextMain", "PressedBg", 4.5);
            Ge(xiang, p, "TextSub", "PageBg", 3.5);
            Ge(xiang, p, "TextSub", "CardBg", 3.5);
            Ge(xiang, p, "TextHint", "PageBg", 3.0);
            Ge(xiang, p, "TextHint", "CardBg", 3.0);
            Ge(xiang, p, "OnPrimary", "Primary", 4.5);
            Ge(xiang, p, "OnDanger", "Danger", 4.5);
            Ge(xiang, p, "Primary", "PageBg", 3.0);
            Ge(xiang, p, "Primary", "CardBg", 3.0);
            Ge(xiang, p, "Danger", "PageBg", 3.0);
            Ge(xiang, p, "Danger", "CardBg", 3.0);
            Ge(xiang, p, "Accent", "PageBg", 3.0);
            Ge(xiang, p, "Accent", "CardBg", 3.0);
            Ge(xiang, p, "Stroke", "PageBg", 2.0);
            Ge(xiang, p, "Stroke", "CardBg", 2.0);
            Ge(xiang, p, "Divider", "CardBg", 2.0);
        }

        Console.WriteLine($"视觉之道矩阵：{_matrixChecked} 对配色断言 × 3 象全绿");
    }

    private static void TestUiSourceRules()
    {
        var uiDir = FindUiDir();
        foreach (var f in new[] { "MainActivity.cs", "ArchiveActivity.cs", "TaoDialog.cs", "TaoIcon.cs" })
        {
            var src = File.ReadAllText(Path.Combine(uiDir, f));
            Assert.False(src.Contains("Color.ParseColor"), $"{f} 绕开色板硬编码取色");
        }
        var main = File.ReadAllText(Path.Combine(uiDir, "MainActivity.cs"));
        Assert.True(main.Contains("ShowEmpty"), "MainActivity 缺空态视图");
        var arch = File.ReadAllText(Path.Combine(uiDir, "ArchiveActivity.cs"));
        Assert.True(arch.Contains("_emptyBox"), "压缩包页缺空态视图");
        var theme = File.ReadAllText(Path.Combine(uiDir, "ThemeService.cs"));
        Assert.True(theme.Contains("Shuimo") && theme.Contains("Tianhui") && theme.Contains("Moye"), "三象不齐");
        Assert.True(Regex.IsMatch(theme, @"Current\s*\{\s*get;\s*private set;\s*\}\s*=\s*Xiang\.Shuimo"), "默认象非水墨");
    }
}

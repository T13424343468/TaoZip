using Ionic.Zip;
using SharpCompress.Common;
using SharpCompress.Writers.Tar;
using TaoZip.Model;

namespace TaoZip.BLL;

/// <summary>
/// 压缩写入：zip 走 DotNetZip（支持 AES-256 加密、注释、多编码、Zip64），
/// tar/tgz 走 SharpCompress TarWriter，gz 走 GZipStream。
/// 说明：7z/rar 因授权与实现限制不支持创建，仅支持解压。
/// </summary>
public static class ArchiveWriter
{
    public static void Compress(CompressRequest req, string? taskId = null,
        IProgress<ArchiveProgress>? progress = null, CancellationToken ct = default)
    {
        foreach (var src in req.SourcePaths)
            if (!File.Exists(src) && !Directory.Exists(src))
                throw new FileNotFoundException("找不到源路径：" + src);

        switch (req.Format)
        {
            case CompressFormat.Zip:
                CompressZip(req, taskId, progress, ct);
                break;
            case CompressFormat.Tar:
                CompressTar(req, taskId, progress, ct, gzip: false);
                break;
            case CompressFormat.Tgz:
                CompressTar(req, taskId, progress, ct, gzip: true);
                break;
            case CompressFormat.Gz:
                CompressSingleGz(req, progress, ct);
                break;
            default:
                throw new NotSupportedException("不支持的压缩格式");
        }

        // 分卷：压缩完成后按大小切分
        if (req.VolumeSizeMb > 0 && req.Format != CompressFormat.Gz)
            VolumeHelper.SplitFile(req.DestPath, req.VolumeSizeMb * 1024L * 1024L, keepOriginal: false);
    }

    private static void CompressZip(CompressRequest req, string? taskId,
        IProgress<ArchiveProgress>? progress, CancellationToken ct)
    {
        var enc = FileNameEncodings.Resolve(req.FileNameEncoding);
        using var zip = new Ionic.Zip.ZipFile(enc)
        {
            CompressionLevel = ToLevel(req.Level),
            Comment = req.Comment ?? "",
        };
        if (!string.IsNullOrEmpty(req.Password))
        {
            zip.Password = req.Password;
            zip.Encryption = EncryptionAlgorithm.WinZipAes256;
        }

        int totalItems = 0;
        foreach (var src in req.SourcePaths)
        {
            if (File.Exists(src)) totalItems++;
            else totalItems += Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories).Count() + 1;
        }
        totalItems = Math.Max(1, totalItems);
        int done = 0;

        zip.AddProgress += (_, e) =>
        {
            if (e.EventType != ZipProgressEventType.Adding_AfterAddEntry) return;
            done++;
            progress?.Report(new ArchiveProgress
            {
                TaskId = taskId ?? "",
                Percent = Math.Min(99, done * 100 / totalItems),
                CurrentItem = e.CurrentEntry?.FileName ?? "",
            });
        };

        // 多个源时以第一个源的父目录为根，保留相对结构
        string rootDir = req.SourcePaths.Count > 0 && Directory.Exists(req.SourcePaths[0])
            ? (Path.GetDirectoryName(Path.GetFullPath(req.SourcePaths[0])) ?? "") : "";
        foreach (var src in req.SourcePaths)
        {
            ct.ThrowIfCancellationRequested();
            if (File.Exists(src))
            {
                var rel = rootDir.Length > 0 && Path.GetFullPath(src).StartsWith(rootDir, StringComparison.OrdinalIgnoreCase)
                    ? Path.GetRelativePath(rootDir, src) : Path.GetFileName(src);
                zip.AddFile(src, Path.GetDirectoryName(rel)?.Replace('\\', '/'));
            }
            else if (Directory.Exists(src))
            {
                var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(src)));
                zip.AddDirectory(src, rootDir.Length > 0 ? Path.GetRelativePath(rootDir, src) : name);
            }
        }

        zip.Save(req.DestPath);
        progress?.Report(new ArchiveProgress { TaskId = taskId ?? "", Percent = 100 });
    }

    private static Ionic.Zlib.CompressionLevel ToLevel(int level) => level switch
    {
        <= 1 => Ionic.Zlib.CompressionLevel.None,
        <= 3 => Ionic.Zlib.CompressionLevel.BestSpeed,
        <= 6 => Ionic.Zlib.CompressionLevel.Default,
        _ => Ionic.Zlib.CompressionLevel.BestCompression,
    };

    private static void CompressTar(CompressRequest req, string? taskId,
        IProgress<ArchiveProgress>? progress, CancellationToken ct, bool gzip)
    {
        var sources = CollectFiles(req.SourcePaths);
        int total = Math.Max(1, sources.Count), done = 0;
        string rootDir = req.SourcePaths.Count > 0 && Directory.Exists(req.SourcePaths[0])
            ? (Path.GetDirectoryName(Path.GetFullPath(req.SourcePaths[0])) ?? "") : "";

        if (!gzip)
        {
            using var fs = File.Create(req.DestPath);
            using var tar = new TarWriter(fs, new TarWriterOptions(CompressionType.None));
            foreach (var (file, entryName) in sources)
            {
                ct.ThrowIfCancellationRequested();
                using var content = File.OpenRead(file);
                tar.Write(entryName, content, File.GetLastWriteTime(file));
                done++;
                progress?.Report(new ArchiveProgress
                {
                    TaskId = taskId ?? "",
                    Percent = Math.Min(99, done * 100 / total),
                    CurrentItem = Path.GetFileName(file),
                });
            }
        }
        else
        {
            // 先写临时 tar，再整体 gzip 包装
            var tmpTar = req.DestPath + ".zgtmptar";
            using (var fs = File.Create(tmpTar))
            using (var tar = new TarWriter(fs, new TarWriterOptions(CompressionType.None)))
            {
                foreach (var (file, entryName) in sources)
                {
                    ct.ThrowIfCancellationRequested();
                    using var content = File.OpenRead(file);
                    tar.Write(entryName, content, File.GetLastWriteTime(file));
                    done++;
                    progress?.Report(new ArchiveProgress
                    {
                        TaskId = taskId ?? "",
                        Percent = Math.Min(80, done * 80 / total),
                        CurrentItem = Path.GetFileName(file),
                    });
                }
            }
            using (var tarFs = File.OpenRead(tmpTar))
            using (var outFs = File.Create(req.DestPath))
            using (var gz = new System.IO.Compression.GZipStream(outFs, System.IO.Compression.CompressionLevel.Optimal))
            {
                tarFs.CopyTo(gz, 1 << 16);
            }
            File.Delete(tmpTar);
        }
        progress?.Report(new ArchiveProgress { TaskId = taskId ?? "", Percent = 100 });
    }

    private static void CompressSingleGz(CompressRequest req,
        IProgress<ArchiveProgress>? progress, CancellationToken ct)
    {
        if (req.SourcePaths.Count != 1 || !File.Exists(req.SourcePaths[0]))
            throw new InvalidOperationException("gzip 只能压缩单个文件");
        var src = req.SourcePaths[0];
        using var inFs = File.OpenRead(src);
        using var outFs = File.Create(req.DestPath);
        using var gz = new System.IO.Compression.GZipStream(outFs, System.IO.Compression.CompressionLevel.Optimal);
        inFs.CopyTo(gz, 1 << 16);
        progress?.Report(new ArchiveProgress { TaskId = "", Percent = 100 });
    }

    internal static List<(string File, string EntryName)> CollectFiles(IReadOnlyList<string> sourcePaths)
    {
        string rootDir = sourcePaths.Count > 0 && Directory.Exists(sourcePaths[0])
            ? (Path.GetDirectoryName(Path.GetFullPath(sourcePaths[0])) ?? "") : "";
        var list = new List<(string, string)>();
        foreach (var src in sourcePaths)
        {
            if (File.Exists(src))
            {
                var rel = rootDir.Length > 0 ? Path.GetRelativePath(rootDir, src) : Path.GetFileName(src);
                list.Add((src, rel.Replace('\\', '/')));
            }
            else if (Directory.Exists(src))
            {
                var baseRel = rootDir.Length > 0
                    ? Path.GetRelativePath(rootDir, src)
                    : Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(src)));
                foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(rootDir.Length > 0 ? rootDir : src, f);
                    var entry = rootDir.Length > 0 ? rel : Path.Combine(baseRel, rel);
                    list.Add((f, entry.Replace('\\', '/')));
                }
            }
        }
        return list;
    }
}

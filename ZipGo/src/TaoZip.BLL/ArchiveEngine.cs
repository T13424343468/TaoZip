using Ionic.Zip;
using SharpCompress.Archives;
using SharpCompress.Archives.Tar;
using SharpCompress.Common;
using SharpCompress.Providers;
using SharpCompress.Readers;
using TaoZip.Model;

namespace TaoZip.BLL;

/// <summary>
/// 压缩核心引擎：格式识别、浏览、解压、完整性测试。
/// 归档型格式走 SharpCompress（zip/7z/rar/tar 及 tar.gz 包装），
/// 单流格式 gz/bz2/xz 走解压流；zip 注释与中文文件名编码单独处理。
/// </summary>
public static class ArchiveEngine
{
    /// <summary>支持的解压格式扩展名。</summary>
    public static readonly string[] ArchiveExtensions =
    {
        ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".tbz2", ".txz"
    };

    public static bool IsArchiveFile(string path)
    {
        var name = Path.GetFileName(path).ToLowerInvariant();
        if (name.EndsWith(".tar.gz") || name.EndsWith(".tar.bz2") || name.EndsWith(".tar.xz"))
            return true;
        return ArchiveExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
    }

    /// <summary>按扩展名识别格式标识。</summary>
    public static string DetectFormat(string path)
    {
        var name = Path.GetFileName(path).ToLowerInvariant();
        if (name.EndsWith(".tar.gz") || name.EndsWith(".tar.bz2") || name.EndsWith(".tar.xz")
            || name.EndsWith(".tgz") || name.EndsWith(".tbz2") || name.EndsWith(".txz"))
            return "tgz";
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".zip" => "zip",
            ".7z" => "7z",
            ".rar" => "rar",
            ".tar" => "tar",
            ".gz" => "gz",
            ".bz2" => "bz2",
            ".xz" => "xz",
            _ => "zip"
        };
    }

    /// <summary>列出压缩包内容。</summary>
    public static ArchiveInfo OpenInfo(string archivePath, string? password = null, string? fileNameEncoding = null)
    {
        var format = DetectFormat(archivePath);
        string? comment = null;
        if (format == "zip")
            comment = ReadZipComment(archivePath, fileNameEncoding);

        using var session = OpenSession(archivePath, password, fileNameEncoding);
        var entries = session.Entries.Select(e => new ArchiveEntry
        {
            Path = e.Path,
            IsDirectory = e.IsDirectory,
            Size = e.Size,
            CompressedSize = e.CompressedSize,
            IsEncrypted = e.IsEncrypted,
            Modified = e.Modified,
        }).ToList();
        return new ArchiveInfo
        {
            FilePath = archivePath,
            Format = format,
            TotalSize = new FileInfo(archivePath).Length,
            Entries = entries,
            Comment = comment,
        };
    }

    /// <summary>读取 zip 全局注释（按写入时所用编码解码）。</summary>
    private static string? ReadZipComment(string archivePath, string? fileNameEncoding)
    {
        try
        {
            var ro = new ReadOptions { Encoding = FileNameEncodings.Resolve(fileNameEncoding) };
            using var zf = ZipFile.Read(archivePath, ro);
            return string.IsNullOrEmpty(zf.Comment) ? null : zf.Comment;
        }
        catch { return null; }
    }

    // ---------- 会话：一次打开，支持浏览/逐条提取 ----------

    internal sealed class SessionEntry
    {
        public required string Path { get; init; }
        public bool IsDirectory { get; init; }
        public long Size { get; init; }
        public long CompressedSize { get; init; }
        public bool IsEncrypted { get; init; }
        public DateTime Modified { get; init; }
        /// <summary>zip 条目 CRC32（其他格式为 0）。</summary>
        public long Crc { get; init; }
        /// <summary>内部条目对象（用于打开流）。</summary>
        public object? Inner { get; init; }
    }

    internal sealed class ArchiveSession : IDisposable
    {
        private readonly IDisposable? _archive;
        private readonly Stream? _ownedStream;
        public IReadOnlyList<SessionEntry> Entries { get; }
        public Func<SessionEntry, Stream> OpenStream { get; }

        public ArchiveSession(IReadOnlyList<SessionEntry> entries, Func<SessionEntry, Stream> openStream,
            IDisposable? archive = null, Stream? ownedStream = null)
        {
            Entries = entries;
            OpenStream = openStream;
            _archive = archive;
            _ownedStream = ownedStream;
        }

        public void Dispose() { _archive?.Dispose(); _ownedStream?.Dispose(); }
    }

    internal static ArchiveSession OpenSession(string archivePath, string? password, string? fileNameEncoding = null)
    {
        var format = DetectFormat(archivePath);
        var ro = ReaderOptions(password, fileNameEncoding);

        switch (format)
        {
            case "gz":
            {
                var gz = SharpCompress.Archives.GZip.GZipArchive.OpenArchive(archivePath, ro);
                return WrapSingleArchive(gz, SuggestName(archivePath));
            }
            case "bz2":
            {
                // bz2 非归档格式：用解压流包装成单条目会话
                var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read);
                var bz = CompressionProviderRegistry.Default.CreateDecompressStream(CompressionType.BZip2, fs);
                var entry = new SessionEntry { Path = SuggestName(archivePath), Inner = null };
                return new ArchiveSession(
                    new List<SessionEntry> { entry },
                    _ => bz, ownedStream: bz);
            }
            case "xz":
            {
                var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read);
                var xz = CompressionProviderRegistry.Default.CreateDecompressStream(CompressionType.Xz, fs);
                var entry = new SessionEntry { Path = SuggestName(archivePath), Inner = null };
                return new ArchiveSession(
                    new List<SessionEntry> { entry },
                    _ => xz, ownedStream: xz);
            }
            case "tgz":
            {
                // 先解出外层压缩流，再在内层 tar 上建立会话
                var gz = SharpCompress.Archives.GZip.GZipArchive.OpenArchive(archivePath, ro);
                var outer = gz.Entries.First().OpenEntryStream();
                var ms = new MemoryStream();
                outer.CopyTo(ms, 1 << 16);
                outer.Dispose();
                gz.Dispose();
                ms.Position = 0;
                var tar = TarArchive.OpenArchive(ms, ro);
                var list = tar.Entries.Select(e => new SessionEntry
                {
                    Path = SanitizeEntryPath(e.Key ?? "unnamed"),
                    IsDirectory = e.IsDirectory,
                    Size = e.Size,
                    CompressedSize = e.CompressedSize,
                    IsEncrypted = e.IsEncrypted,
                    Modified = e.LastModifiedTime ?? DateTime.Now,
                    Inner = e,
                }).ToList();
                return new ArchiveSession(list,
                    e => ((IArchiveEntry)e.Inner!).OpenEntryStream(),
                    archive: tar, ownedStream: ms);
            }
            default:
            {
                var archive = ArchiveFactory.OpenArchive(archivePath, ro);
                var list = archive.Entries.Select(e =>
                {
                    long crc = 0;
                    if (e is SharpCompress.Archives.Zip.ZipArchiveEntry ze) crc = ze.Crc;
                    return new SessionEntry
                    {
                        Path = SanitizeEntryPath(e.Key ?? "unnamed"),
                        IsDirectory = e.IsDirectory,
                        Size = e.Size,
                        CompressedSize = e.CompressedSize,
                        IsEncrypted = e.IsEncrypted,
                        Modified = e.LastModifiedTime ?? DateTime.Now,
                        Crc = crc,
                        Inner = e,
                    };
                }).ToList();
                return new ArchiveSession(list,
                    e => ((IArchiveEntry)e.Inner!).OpenEntryStream(),
                    archive: archive);
            }
        }
    }

    /// <summary>gzip 单流归档包装成单条目会话。</summary>
    private static ArchiveSession WrapSingleArchive(IDisposable archive, string name)
    {
        var ia = (IArchive)archive;
        var e = ia.Entries.FirstOrDefault();
        var entry = new SessionEntry
        {
            Path = name,
            Size = e?.Size ?? 0,
            Modified = e?.LastModifiedTime ?? DateTime.Now,
            Inner = e,
        };
        return new ArchiveSession(new List<SessionEntry> { entry },
            s => ((IArchiveEntry)s.Inner!).OpenEntryStream(), archive: archive);
    }

    private static ReaderOptions ReaderOptions(string? password, string? fileNameEncoding = null)
    {
        var ro = new ReaderOptions();
        if (!string.IsNullOrEmpty(password)) ro.Password = password;
        var enc = FileNameEncodings.Resolve(fileNameEncoding);
        ro.ArchiveEncoding = new ArchiveEncoding
        {
            Default = enc,
            Password = enc,
        };
        return ro;
    }

    private static string SuggestName(string archivePath)
        => Path.GetFileNameWithoutExtension(archivePath);

    /// <summary>清理条目路径，阻断 zip slip 路径穿越。</summary>
    public static string SanitizeEntryPath(string entryPath)
    {
        var parts = entryPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var cleaned = parts.Where(p => p != "." && p != "..").ToArray();
        return cleaned.Length == 0 ? "unnamed" : string.Join('/', cleaned);
    }

    /// <summary>解压全部或指定条目到目标目录。</summary>
    public static void Extract(ExtractRequest req, string? taskId = null,
        IProgress<ArchiveProgress>? progress = null, CancellationToken ct = default)
    {
        if (!File.Exists(req.ArchivePath))
            throw new FileNotFoundException("找不到压缩包：" + req.ArchivePath);
        Directory.CreateDirectory(req.DestDir);

        using var session = OpenSession(req.ArchivePath, req.Password, req.FileNameEncoding);
        var wanted = req.EntryPaths is { Count: > 0 } ? new HashSet<string>(req.EntryPaths) : null;
        var files = session.Entries.Where(e => !e.IsDirectory && (wanted == null || wanted.Contains(e.Path))).ToList();
        foreach (var d in session.Entries.Where(e => e.IsDirectory))
            Directory.CreateDirectory(SafeJoin(req.DestDir, d.Path));

        int total = Math.Max(1, files.Count), done = 0;
        foreach (var e in files)
        {
            ct.ThrowIfCancellationRequested();
            var destFile = SafeJoin(req.DestDir, e.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            if (!(req.SkipExisting && File.Exists(destFile)))
            {
                using var es = session.OpenStream(e);
                using var fs = File.Create(destFile);
                es.CopyTo(fs, 1 << 16);
                if (e.Modified.Year > 1980)
                    File.SetLastWriteTime(destFile, e.Modified);
            }
            done++;
            progress?.Report(new ArchiveProgress
            {
                TaskId = taskId ?? "",
                Percent = done * 100 / total,
                CurrentItem = Path.GetFileName(e.Path),
            });
        }
    }

    /// <summary>
    /// 完整性测试：逐条目完整读取，zip 条目同时校验 CRC32。
    /// 返回坏条目列表（空列表=通过）。
    /// </summary>
    public static List<string> Test(string archivePath, string? password = null,
        string? taskId = null, IProgress<ArchiveProgress>? progress = null, CancellationToken ct = default)
    {
        var bad = new List<string>();
        using var session = OpenSession(archivePath, password);
        var all = session.Entries.Where(e => !e.IsDirectory).ToList();
        int total = Math.Max(1, all.Count), done = 0;
        foreach (var e in all)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                uint crc = 0;
                using var s = session.OpenStream(e);
                var buf = new byte[1 << 16];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                    crc = Crc32.Update(crc, buf.AsSpan(0, n));
                if (e.Crc != 0 && crc != (uint)e.Crc)
                    bad.Add(e.Path + " — CRC 校验失败");
            }
            catch (Exception ex)
            {
                bad.Add(e.Path + " — " + ex.Message);
            }
            done++;
            progress?.Report(new ArchiveProgress { TaskId = taskId ?? "", Percent = done * 100 / total, CurrentItem = e.Path });
        }
        return bad;
    }

    /// <summary>拼接目标目录与条目内部路径（已防穿越）。</summary>
    public static string SafeJoin(string destDir, string entryPath)
        => Path.Combine(destDir, SanitizeEntryPath(entryPath).Replace('/', Path.DirectorySeparatorChar));
}

/// <summary>轻量 CRC32（IEEE 802.3，与 zip 一致）。</summary>
public static class Crc32
{
    private static readonly uint[] Table = Build();

    private static uint[] Build()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            t[i] = c;
        }
        return t;
    }

    public static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        crc = ~crc;
        foreach (var b in data)
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }
}

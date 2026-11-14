using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Compressors;
using SharpCompress.Writers;
using SharpCompress.Writers.Tar;
using SharpCompress.Readers;

namespace TaoZip.Core;

/// <summary>压缩与解压核心: 压缩 zip / tar.gz, 解压 zip / 7z / rar(仅解压) / tar / gz / bz2 / xz, 完整性测试与智能解压。</summary>
public class ArchiveService
{
    /// <summary>压缩字节进度: 累计已写入字节并按百分比上报 (封顶 99.9, 完成由界面置 100)。</summary>
    private sealed class ByteProgress
    {
        public long Total;
        public long Done;
        public IProgress<double>? Report;
        public void Add(long n)
        {
            Done += n;
            if (Report != null && Total > 0) Report.Report(Math.Min(99.9, Done * 100.0 / Total));
        }
    }

    /// <summary>创建 zip 压缩包 (普通压缩; 加密 zip 暂不支持, 界面需说明)。progress 按已写入字节报告 0-100 百分比。</summary>
    public void CreateZip(IEnumerable<string> paths, string destZip, IProgress<double>? progress = null)
    {
        var items = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        // 兜底闸 (2026-11-14 审查红2): 目标包出现在待压缩清单里时, File.Create 会先把源截断为零再压空文件——源数据被毁, 必须拒绝
        var destFull = Path.GetFullPath(destZip);
        if (items.Any(p => string.Equals(Path.GetFullPath(p), destFull, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("压缩包不能就是所选文件本身, 请换个位置或名称。");
        var bp = new ByteProgress { Total = items.Sum(SizeOf), Report = progress };
        using var fs = File.Create(destZip);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var p in items)
            AddToZip(zip, p, Path.GetFileName(p), destZip, bp);
    }

    /// <summary>创建 tar.gz 压缩包。progress 按已完成文件字节报告 (逐文件粒度)。</summary>
    public void CreateTarGz(IEnumerable<string> paths, string destTarGz, IProgress<double>? progress = null)
    {
        var items = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        long totalBytes = items.Sum(SizeOf);
        long doneBytes = 0;
        using var fs = File.Create(destTarGz);
        using var writer = new TarWriter(fs, new TarWriterOptions(CompressionType.GZip));
        foreach (var p in items)
        {
            AddToTar(writer, p, Path.GetFileName(p));
            doneBytes += SizeOf(p);
            progress?.Report(Math.Min(99.9, doneBytes * 100.0 / Math.Max(totalBytes, 1)));
        }
    }

    /// <summary>路径总字节数: 文件取长度, 文件夹累计内部全部文件。</summary>
    private static long SizeOf(string path)
    {
        try
        {
            if (File.Exists(path)) return new FileInfo(path).Length;
            if (Directory.Exists(path))
                return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                    .Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } });
        }
        catch { /* 统计失败按 0 处理, 不阻塞压缩 */ }
        return 0;
    }

    /// <summary>解压到指定目录 (按内容自动识别格式, 含路径安全防护); progress 按字节报告 0-100 百分比。</summary>
    public ExtractResult Extract(string archivePath, string destDir, IProgress<double>? progress = null)
    {
        Directory.CreateDirectory(destDir);
        int files = 0, dirs = 0;
        using var src = OpenAny(archivePath);
        var archive = src.Archive;
        var entries = archive.Entries.ToList();
        long totalBytes = entries.Sum(e => Math.Max(e.Size, 0));
        long doneBytes = 0;
        var buffer = new byte[1 << 20]; // 1MB 分块拷贝, 大文件进度平滑
        foreach (var entry in entries)
        {
            var name = entry.Key ?? throw new InvalidFormatException("压缩包含无名条目");
            var safe = SafePath(name, destDir);
            if (entry.IsDirectory)
            {
                Directory.CreateDirectory(safe);
                dirs++;
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(safe)!);
                using var es = entry.OpenEntryStream();
                using var outFs = File.Create(safe);
                int read;
                while ((read = es.Read(buffer, 0, buffer.Length)) > 0)
                {
                    outFs.Write(buffer, 0, read);
                    doneBytes += read;
                    if (progress != null && totalBytes > 0)
                        progress.Report(Math.Min(99.9, doneBytes * 100.0 / totalBytes));
                }
                files++;
            }
        }
        return new ExtractResult(files, dirs);
    }

    /// <summary>智能解压: 压缩包只有一个根目录时解到目标目录内, 否则自动建同名文件夹, 避免散落一地。</summary>
    public ExtractResult SmartExtract(string archivePath, string destBase, IProgress<double>? progress = null)
    {
        string root = DetectSingleRoot(archivePath);
        // 单根: 条目自带根目录, 直接解到目标目录; 多根: 建同名文件夹防散落
        string dest = string.IsNullOrEmpty(root)
            ? Path.Combine(destBase, Path.GetFileNameWithoutExtension(archivePath))
            : destBase;
        return Extract(archivePath, dest, progress);
    }

    /// <summary>解压默认目标: 压缩包同名文件夹 (去掉全部后缀), 已存在则自动加序号。</summary>
    public static string DefaultDestFor(string archive)
    {
        var dir = Path.GetDirectoryName(archive) ?? ".";
        var stem = Path.GetFileName(archive);
        while (true)
        {
            var ext = Path.GetExtension(stem);
            if (ext.Length == 0) break;
            stem = stem[..^ext.Length];
        }
        var dest = Path.Combine(dir, stem);
        var n = 2;
        while (Directory.Exists(dest)) dest = Path.Combine(dir, $"{stem}({n++})");
        return dest;
    }

    /// <summary>完整性测试: 读尽全部条目, 返回条目数与总解压后大小; 损坏时抛异常。</summary>
    public (int Entries, long Bytes) Test(string archivePath)
    {
        int count = 0;
        long bytes = 0;
        using var src = OpenAny(archivePath);
        foreach (var entry in src.Archive.Entries)
        {
            count++;
            if (entry.IsDirectory) continue;
            using var es = entry.OpenEntryStream();
            var buf = new byte[81920];
            while (es.Read(buf, 0, buf.Length) > 0) { }
            bytes += entry.Size;
        }
        return (count, bytes);
    }

    /// <summary>列出压缩包全部条目 (名称 / 是否目录 / 字节大小)。</summary>
    public List<(string Name, bool IsDir, long Size)> ListEntries(string archivePath)
    {
        var list = new List<(string, bool, long)>();
        using var src = OpenAny(archivePath);
        foreach (var entry in src.Archive.Entries)
            list.Add((entry.Key ?? "", entry.IsDirectory, entry.Size));
        return list;
    }

    /// <summary>探测压缩包是否只含一个根目录 (返回根目录名, 无则返回空串)。</summary>
    public string DetectSingleRoot(string archivePath)
    {
        using var src = OpenAny(archivePath);
        var tops = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in src.Archive.Entries)
        {
            var key = entry.Key ?? "";
            // 只有目录条目或带子路径的条目才算"根文件夹"; 单文件包直接归为多根
            if (!entry.IsDirectory && !key.Contains('/')) continue;
            var first = key.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (first != null) tops.Add(first);
        }
        return tops.Count == 1 ? tops.First() : "";
    }

    /// <summary>打开压缩包 (功成身退): tar.gz/tgz 需先解 GZip 外层, tar 落临时文件并随持有者生命周期释放,
    /// 不再把整包读进内存; 其余按内容探测。调用方 using 持有者, 释放时连临时文件一并归还。</summary>
    private static ArchiveHolder OpenAny(string path)
    {
        var lower = path.ToLowerInvariant();
        if (lower.EndsWith(".tar.gz") || lower.EndsWith(".tgz"))
        {
            var tmp = Path.Combine(TaoPaths.TempDir("TaoZip"), "taozip-" + Guid.NewGuid().ToString("N") + ".tar");
            try
            {
                using (var fs = File.OpenRead(path))
                using (var gzip = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress))
                using (var tfs = File.Create(tmp))
                    gzip.CopyTo(tfs);
                var owner = File.OpenRead(tmp);
                try { return new ArchiveHolder(ArchiveFactory.OpenArchive(owner), tmp, owner); }
                catch { owner.Dispose(); throw; }
            }
            catch { try { File.Delete(tmp); } catch { } throw; }
        }
        var stream = File.OpenRead(path);
        try { return new ArchiveHolder(ArchiveFactory.OpenArchive(stream), null, stream); }
        catch { stream.Dispose(); throw; }
    }

    /// <summary>压缩包持有者: 包住包本体与其依赖的流/临时文件, 释放时全部归还。</summary>
    private sealed class ArchiveHolder : IDisposable
    {
        public readonly IArchive Archive;
        private readonly string? _tempFile;
        private readonly Stream _owner;

        public ArchiveHolder(IArchive archive, string? tempFile, Stream owner)
        {
            Archive = archive;
            _tempFile = tempFile;
            _owner = owner;
        }

        public void Dispose()
        {
            try { Archive.Dispose(); } catch { }
            _owner.Dispose();
            if (_tempFile != null) { try { File.Delete(_tempFile); } catch { } }
        }
    }

    private void AddToZip(ZipArchive zip, string path, string entryName, string destZip, ByteProgress bp)
    {
        // 目标压缩包可能位于被压缩目录内部 (自定义位置时), 必须跳过自身, 否则边写边读
        var destFull = Path.GetFullPath(destZip);
        if (File.Exists(path))
        {
            var e = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using var inFs = File.OpenRead(path);
            using var es = e.Open();
            var buffer = new byte[1 << 20]; // 1MB 分块, 大文件进度平滑
            int read;
            while ((read = inFs.Read(buffer, 0, buffer.Length)) > 0)
            {
                es.Write(buffer, 0, read);
                bp.Add(read);
            }
        }
        else if (Directory.Exists(path))
        {
            // 先写目录条目 (含空目录), 解压后目录结构完整
            zip.CreateEntry(entryName + "/");
            foreach (var d in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
            {
                var rel = entryName + "/" + Path.GetRelativePath(path, d).Replace('\\', '/');
                zip.CreateEntry(rel + "/");
            }
            foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                if (string.Equals(Path.GetFullPath(f), destFull, StringComparison.OrdinalIgnoreCase))
                    continue; // 跳过目标压缩包自身
                // 保留顶层目录名, 解压后与原文件夹结构一致
                var rel = entryName + "/" + Path.GetRelativePath(path, f).Replace('\\', '/');
                var e = zip.CreateEntry(rel, CompressionLevel.Optimal);
                using var inFs = File.OpenRead(f);
                using var es = e.Open();
                var buffer = new byte[1 << 20];
                int read;
                while ((read = inFs.Read(buffer, 0, buffer.Length)) > 0)
                {
                    es.Write(buffer, 0, read);
                    bp.Add(read);
                }
            }
        }
        else throw new FileNotFoundException($"找不到要压缩的路径: {path}");
    }

    private void AddToTar(TarWriter writer, string path, string entryName)
    {
        if (File.Exists(path))
        {
            using var inFs = File.OpenRead(path);
            writer.Write(entryName, inFs, File.GetLastWriteTime(path));
        }
        else if (Directory.Exists(path))
        {
            foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(path, f).Replace('\\', '/');
                using var inFs = File.OpenRead(f);
                writer.Write(rel, inFs, File.GetLastWriteTime(f));
            }
        }
        else throw new FileNotFoundException($"找不到要压缩的路径: {path}");
    }

    /// <summary>防压缩包路径穿越 (ZipSlip): 条目路径必须落在解压目录内。
    /// 两道闸 (2026-11-14 审查红1): ① 盘符/根路径形态 (C:/x、C:x、/x) 直接拒绝——Path.Combine 遇到这类条目会丢弃目标目录;
    /// ② 拼合取全路径后再核前缀, 双平台兜底, 防任何残余穿越形态落网。</summary>
    private static string SafePath(string entryName, string destDir)
    {
        var rel = entryName.Replace('\\', '/').TrimStart('/');
        foreach (var seg in rel.Split('/', StringSplitOptions.RemoveEmptyEntries))
            if (seg == "..") throw new InvalidFormatException($"压缩包含非法路径: {entryName}");
        if (Path.IsPathRooted(rel)) throw new InvalidFormatException($"压缩包含非法路径: {entryName}");
        var destRoot = Path.GetFullPath(destDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                       + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(destDir, rel));
        if (!full.StartsWith(destRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidFormatException($"压缩包含非法路径: {entryName}");
        return full;
    }
}

/// <summary>解压结果统计。</summary>
public record ExtractResult(int Files, int Directories);

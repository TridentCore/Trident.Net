using System.Text.Json;
using TridentCore.Abstractions;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Engines.Deploying;
using FileHash = TridentCore.Abstractions.Utilities.FileHash;

namespace TridentCore.Core.Utilities;

public static class PatchStorageHelper
{
    public const string IndexFileName = "data.patch.json";

    public static Task<PatchSet> LoadAsync(string key, CancellationToken token = default) =>
        LoadDirectoryAsync(PathDef.Default.DirectoryOfHome(key), token);

    public static async Task<PatchSet> LoadDirectoryAsync(string home, CancellationToken token = default)
    {
        var root = Path.Combine(home, "patches");
        var index = await ReadIndexAtAsync(root, token).ConfigureAwait(false);
        var operations = new List<PatchSet.Instruction>();
        foreach (var (layer, entries) in new[] { ("import", index.Import), ("users", index.Users) })
        {
            ValidateEntries(root, layer, entries);
            foreach (var entry in entries.Where(x => x.Enabled))
            {
                var relative = $"{layer}/{entry.Path.Replace('\\', '/')}";
                var path = PatchHelper.ResolvePath(root, relative);
                try
                {
                    var document = await ReadDocumentAsync(path, token).ConfigureAwait(false);
                    var directory = Path.GetDirectoryName(path)!;
                    foreach (var operation in document.Operations)
                    {
                        PatchSet.Validate(operation);
                        if (!PatchHelper.Matches(operation.Rules))
                        {
                            continue;
                        }

                        var assets = new Dictionary<string, PatchSet.LocalAsset>();
                        foreach (var library in PatchSet.GetLibraries(operation).Where(x => PatchHelper.Matches(x.Rules)))
                        {
                            if (library.Local is not { } local || assets.ContainsKey(local))
                            {
                                continue;
                            }
                            // NOTE: 规则未命中的源资产无需物化，但这里仍要为命中的资产留下内容指纹，
                            //  否则本地资产变更不会让相关 region 失效。
                            var assetPath = PatchHelper.ResolvePath(directory, local);
                            FileHash? hash = null;
                            if (File.Exists(assetPath))
                            {
                                await using var stream = File.OpenRead(assetPath);
                                hash = FileHash.Sha256(await FileHelper.ComputeHashAsync(stream, HashAlgorithm.Sha256).ConfigureAwait(false));
                            }
                            assets.Add(local, new(Path.GetRelativePath(home, assetPath).Replace('\\', '/'), hash));
                        }
                        operations.Add(new(relative, operation, assets));
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    throw new InvalidDataException($"Patch '{relative}' is not valid: {ex.Message}", ex);
                }
            }
        }
        return new(operations);
    }

    public static Task<PatchIndex> ReadIndexAsync(string key, CancellationToken token = default) =>
        ReadIndexAtAsync(PathDef.Default.DirectoryOfPatches(key), token);

    public static async Task SaveIndexAsync(string key, PatchIndex index, CancellationToken token = default)
    {
        var root = PathDef.Default.DirectoryOfPatches(key);
        ValidateIndex(index);
        foreach (var (layer, entries) in new[] { ("import", index.Import), ("users", index.Users) })
        {
            ValidateEntries(root, layer, entries);
            foreach (var entry in entries)
            {
                await ReadDocumentAsync(PatchHelper.ResolvePath(root, $"{layer}/{entry.Path}"), token).ConfigureAwait(false);
            }
        }
        await WriteJsonAsync(Path.Combine(root, IndexFileName), index, token).ConfigureAwait(false);
    }

    public static async Task SaveDocumentAsync(string key, string layer, string path, PatchDocument document, CancellationToken token = default)
    {
        ValidateLayer(layer);
        ValidateDocument(document);
        var target = PatchHelper.ResolvePath(PathDef.Default.DirectoryOfPatches(key), $"{layer}/{path}");
        await WriteJsonAsync(target, document, token).ConfigureAwait(false);
    }

    public static async Task AddUserAsync(string key, string sourceDocument, string name, CancellationToken token = default)
    {
        if (name.IndexOfAny(['/', '\\']) >= 0)
        {
            throw new ArgumentException("A patch name must be one directory name.", nameof(name));
        }
        var root = PathDef.Default.DirectoryOfPatches(key);
        var target = PatchHelper.ResolvePath(root, $"users/{name}");
        if (Path.Exists(target))
        {
            throw new IOException($"Patch '{name}' already exists.");
        }
        var document = await ReadDocumentAsync(sourceDocument, token).ConfigureAwait(false);
        var source = Path.GetDirectoryName(Path.GetFullPath(sourceDocument))!;
        var temporary = Path.Combine(root, "users", ".staging-" + Guid.NewGuid().ToString("N"));
        var installed = false;
        try
        {
            Directory.CreateDirectory(temporary);
            foreach (var local in document.Operations.SelectMany(PatchSet.GetLibraries).Select(x => x.Local).OfType<string>().Distinct())
            {
                var from = PatchHelper.ResolvePath(source, local);
                var to = PatchHelper.ResolvePath(temporary, local);
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(from, to);
            }
            await WriteJsonAsync(Path.Combine(temporary, "patch.json"), document, token).ConfigureAwait(false);
            Directory.Move(temporary, target);
            installed = true;
            var index = await ReadIndexAtAsync(root, token).ConfigureAwait(false);
            await SaveIndexAsync(key, index with { Users = [.. index.Users, new($"{name}/patch.json")] }, token).ConfigureAwait(false);
        }
        catch
        {
            if (installed)
            {
                Directory.Delete(target, true);
            }
            throw;
        }
        finally
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, true);
            }
        }
    }

    public static async Task UpdateEntryAsync(string key, string layer, string path, bool? enabled = null, int? position = null, CancellationToken token = default)
    {
        ValidateLayer(layer);
        var root = PathDef.Default.DirectoryOfPatches(key);
        var target = PatchHelper.ResolvePath(root, $"{layer}/{path}");
        var index = await ReadIndexAtAsync(root, token).ConfigureAwait(false);
        var entries = (layer == "import" ? index.Import : index.Users).ToList();
        var found = entries.FindIndex(x => FileHelper.PathComparer.Equals(PatchHelper.ResolvePath(root, $"{layer}/{x.Path}"), target));
        if (found < 0)
        {
            throw new KeyNotFoundException($"Patch '{layer}/{path}' is not indexed.");
        }
        var entry = entries[found];
        entries[found] = enabled is { } value ? entry with { Enabled = value } : entry;
        if (position is { } to)
        {
            if (to < 0 || to >= entries.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(position));
            }
            entry = entries[found];
            entries.RemoveAt(found);
            entries.Insert(to, entry);
        }
        await SaveIndexAsync(key, layer == "import" ? index with { Import = entries } : index with { Users = entries }, token).ConfigureAwait(false);
    }

    public static async Task RemoveAsync(string key, string layer, string path, CancellationToken token = default)
    {
        ValidateLayer(layer);
        var root = PathDef.Default.DirectoryOfPatches(key);
        var target = PatchHelper.ResolvePath(root, $"{layer}/{path}");
        var index = await ReadIndexAtAsync(root, token).ConfigureAwait(false);
        var entries = layer == "import" ? index.Import : index.Users;
        var remaining = entries.Where(x => !FileHelper.PathComparer.Equals(
            PatchHelper.ResolvePath(root, $"{layer}/{x.Path}"), target)).ToArray();
        if (remaining.Length == entries.Count)
        {
            throw new KeyNotFoundException($"Patch '{layer}/{path}' is not indexed.");
        }
        index = layer == "import" ? index with { Import = remaining } : index with { Users = remaining };
        await WriteJsonAsync(Path.Combine(root, IndexFileName), index, token).ConfigureAwait(false);
        var directory = Path.GetDirectoryName(target)!;
        if (!FileHelper.PathComparer.Equals(directory, Path.Combine(root, layer))
         && !remaining.Any(x => FileHelper.IsInDirectory(PatchHelper.ResolvePath(root, $"{layer}/{x.Path}"), directory)))
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
        else
        {
            File.Delete(target);
        }
    }

    public static async Task<ImportUpdate> PrepareImportUpdateAsync(string home, string stagedHome, CancellationToken token = default)
    {
        var root = Path.Combine(home, "patches");
        var staged = Path.Combine(stagedHome, "patches");
        var previous = await ReadIndexAtAsync(root, token).ConfigureAwait(false);
        var incoming = await ReadIndexAtAsync(staged, token).ConfigureAwait(false);
        Directory.CreateDirectory(Path.Combine(staged, "import"));
        // 更新只原子替换导入层；用户层是本机数据，索引中的条目原样保留。
        var next = new PatchIndex { Import = incoming.Import, Users = previous.Users };
        ValidateEntries(home, next);
        await WriteJsonAsync(Path.Combine(staged, "next-index.json"), next, token).ConfigureAwait(false);
        return new(root, staged);
    }

    public sealed class ImportUpdate(string root, string staged)
    {
        private bool _oldImportMoved;
        private bool _newImportMoved;
        private bool _oldIndexMoved;
        private bool _newIndexMoved;

        public void Commit()
        {
            Directory.CreateDirectory(root);
            var import = Path.Combine(root, "import");
            var index = Path.Combine(root, IndexFileName);
            if (Directory.Exists(import))
            {
                Directory.Move(import, Path.Combine(staged, "previous-import"));
                _oldImportMoved = true;
            }
            if (File.Exists(index))
            {
                File.Move(index, Path.Combine(staged, "previous-index.json"));
                _oldIndexMoved = true;
            }
            Directory.Move(Path.Combine(staged, "import"), import);
            _newImportMoved = true;
            File.Move(Path.Combine(staged, "next-index.json"), index);
            _newIndexMoved = true;
        }

        public void Rollback()
        {
            var import = Path.Combine(root, "import");
            var index = Path.Combine(root, IndexFileName);
            if (_newIndexMoved)
            {
                File.Delete(index);
                _newIndexMoved = false;
            }
            if (_newImportMoved)
            {
                Directory.Delete(import, true);
                _newImportMoved = false;
            }
            if (_oldImportMoved)
            {
                Directory.Move(Path.Combine(staged, "previous-import"), import);
                _oldImportMoved = false;
            }
            if (_oldIndexMoved)
            {
                File.Move(Path.Combine(staged, "previous-index.json"), index);
                _oldIndexMoved = false;
            }
        }
    }

    // 只校验索引本身——条目路径合法、同层目录不重叠。不要求文件已落盘，因此可以在写盘前调用。
    public static void ValidateEntries(string home, PatchIndex index)
    {
        var root = Path.Combine(home, "patches");
        ValidateIndex(index);
        foreach (var (layer, entries) in new[] { ("import", index.Import), ("users", index.Users) })
        {
            ValidateEntries(root, layer, entries);
        }
    }

    public static async Task<PatchIndex> ReadIndexAtAsync(string root, CancellationToken token = default)
    {
        var path = Path.Combine(root, IndexFileName);
        if (!File.Exists(path))
        {
            return new();
        }
        await using var stream = File.OpenRead(path);
        var index = await JsonSerializer.DeserializeAsync<PatchIndex>(stream, PatchHelper.JsonOptions, token).ConfigureAwait(false)
                 ?? throw new InvalidDataException("Patch index is empty.");
        ValidateIndex(index);
        return index;
    }

    public static async Task WriteJsonAsync<T>(string path, T value, CancellationToken token = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(stream, value, FileHelper.SerializerOptions, token).ConfigureAwait(false);
            }
            File.Move(temporary, path, true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static async Task<PatchDocument> ReadDocumentAsync(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        var document = await JsonSerializer.DeserializeAsync<PatchDocument>(stream, PatchHelper.JsonOptions, token).ConfigureAwait(false)
                    ?? throw new InvalidDataException($"Patch '{path}' is empty.");
        document = FoldLegacyJavaMajor(document);
        ValidateDocument(document);
        return document;
    }

    // NOTE: format 1 把 Java 兼容版本写成单个主版本；这里把旧 target 折成兼容版本集合，旧文档因此仍
    //  能参与集合语义。已经写成集合的文档原样通过。移除见 POLY-167。
    private static PatchDocument FoldLegacyJavaMajor(PatchDocument document) =>
        document.Operations.Any(x => x.Target.EndsWith(".javaMajor", StringComparison.Ordinal))
            ? document with
            {
                Operations =
                [
                    .. document.Operations.Select(x => x.Target.EndsWith(".javaMajor", StringComparison.Ordinal)
                                                           ? x with
                                                           {
                                                               Target = x.Target[..^"javaMajor".Length] + "compatibleJavaMajors",
                                                               Value = x.Value is { ValueKind: JsonValueKind.Number } number
                                                                           ? JsonSerializer.SerializeToElement(new[] { number.GetUInt32() },
                                                                                                               FileHelper.SerializerOptions)
                                                                           : x.Value
                                                           }
                                                           : x)
                ]
            }
            : document;

    private static void ValidateDocument(PatchDocument document)
    {
        // 1 是 Java 兼容版本仍写作单个 major 的旧文档，2 是集合与 intersect 的文档；两者都读。见 POLY-167。
        if (document.Format is not (1 or 2) || document.Operations is null)
        {
            throw new InvalidDataException("Unsupported patch document format.");
        }
        foreach (var operation in document.Operations)
        {
            PatchSet.Validate(operation);
        }
    }

    private static void ValidateIndex(PatchIndex index)
    {
        if (index.Format != 1 || index.Import is null || index.Users is null)
        {
            throw new InvalidDataException("Unsupported patch index format.");
        }
    }

    private static void ValidateEntries(string root, string layer, IReadOnlyList<PatchIndex.Entry> entries)
    {
        var seen = new HashSet<string>(FileHelper.PathComparer);
        foreach (var entry in entries)
        {
            var relative = entry.Path.Replace('\\', '/');
            if (!relative.EndsWith("/patch.json", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Each indexed patch must own a directory containing patch.json.");
            }
            var path = PatchHelper.ResolvePath(Path.Combine(root, layer), relative);
            var directory = Path.GetDirectoryName(path)!;
            if (seen.Any(x => FileHelper.PathComparer.Equals(x, directory)
                           || FileHelper.IsInDirectory(x, directory) || FileHelper.IsInDirectory(directory, x)))
            {
                throw new InvalidDataException($"Duplicate or overlapping patch directory '{layer}/{entry.Path}'.");
            }
            seen.Add(directory);
        }
    }

    private static void ValidateLayer(string layer)
    {
        if (layer is not ("import" or "users"))
        {
            throw new ArgumentException("Patch layer must be import or users.", nameof(layer));
        }
    }
}

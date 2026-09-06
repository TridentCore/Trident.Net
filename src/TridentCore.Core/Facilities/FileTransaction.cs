using System.Text.Json;
using Microsoft.Extensions.Logging;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Facilities;

internal sealed class FileTransaction : IDisposable
{
    private const string DIRECTORY = ".update";
    private readonly string _home;
    private readonly ILogger _logger;
    private readonly FileOperations _files;
    private readonly FileStream _lease;
    private readonly List<Change> _changes = [];
    private readonly List<string> _createdParents = [];
    private State _state;

    public FileTransaction(string home, ILogger logger, FileOperations? files = null, string workspaceName = DIRECTORY)
    {
        _home = Path.GetFullPath(home);
        _logger = logger;
        _files = files ?? new();
        if (!FileHelper.IsFileName(workspaceName))
            throw new ArgumentException("Invalid transaction workspace name", nameof(workspaceName));
        EnsureReady(home, workspaceName);
        _lease = new FileStream(Path.Combine(_home, workspaceName + ".lock"), FileMode.CreateNew,
            FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
        Workspace = Path.Combine(_home, workspaceName);
        try
        {
            EnsureReady(home, workspaceName);
            if (Directory.Exists(Workspace)) _files.DeleteDirectory(Workspace);
            Directory.CreateDirectory(StagingDirectory);
        }
        catch
        {
            _lease.Dispose();
            throw;
        }
    }

    public string Workspace { get; }
    public string StagingDirectory => Path.Combine(Workspace, "staged");

    internal static string InstallWorkspaceName(string key) => ".install-" + key;

    public static void EnsureInstanceReady(string home)
    {
        EnsureReady(home);
        EnsureReady(Path.GetDirectoryName(home)!, InstallWorkspaceName(Path.GetFileName(home)));
    }

    public static void EnsureReady(string home, string workspaceName = DIRECTORY)
    {
        var workspace = Path.Combine(home, workspaceName);
        if (Path.Exists(workspace) && !IsFinalized(workspace))
        {
            throw new IOException($"Instance files are busy or awaiting recovery at '{workspace}'; let the active operation finish or inspect its journal before modifying the instance");
        }
    }

    private static bool IsFinalized(string workspace)
    {
        try
        {
            var journal = Path.Combine(workspace, "journal.json");
            if ((File.GetAttributes(workspace) & FileAttributes.ReparsePoint) != 0
             || (File.GetAttributes(journal) & FileAttributes.ReparsePoint) != 0) return false;
            using var document = JsonDocument.Parse(File.ReadAllText(journal));
            var root = document.RootElement;
            if (root.GetProperty("formatVersion").GetInt32() != 1) return false;
            var status = root.GetProperty("status").GetString();
            if (status is not ("committed" or "rolledBack")) return false;
            foreach (var change in root.GetProperty("changes").EnumerateArray())
            {
                var backedUp = change.GetProperty("backedUp").GetBoolean();
                var installed = change.GetProperty("installed").GetBoolean();
                if (status == "rolledBack" && (backedUp || installed)) return false;
                if (status == "committed" && (backedUp != change.GetProperty("hadOriginal").GetBoolean()
                    || installed != (change.GetProperty("staged").ValueKind != JsonValueKind.Null))) return false;
            }
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException
                                      or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return false;
        }
    }

    public string StagePath(string relative)
    {
        var target = TargetPath(relative);
        return Path.Combine(StagingDirectory, Path.GetRelativePath(_home, target));
    }

    public void ReplaceFile(string relative) => Add(relative, EntryKind.File, true);
    public void ReplaceDirectory(string relative) => Add(relative, EntryKind.Directory, true);
    public void RemoveFile(string relative) => Add(relative, EntryKind.File, false);
    public void CreateDirectory(string relative) => Add(relative, EntryKind.Directory, true, true);

    private void Add(string relative, EntryKind kind, bool replace, bool requireAbsent = false)
    {
        if (_state != State.Preparing) throw new InvalidOperationException("The transaction is no longer preparing");
        var target = TargetPath(relative);
        foreach (var change in _changes)
        {
            if (FileHelper.IsPathEquivalent(target, change.Target)
             || FileHelper.IsInDirectory(target, change.Target) || FileHelper.IsInDirectory(change.Target, target))
            {
                throw new InvalidOperationException($"Overlapping update targets '{change.Target}' and '{target}'");
            }
        }
        _changes.Add(new(target, kind, replace ? StagePath(relative) : null,
            Path.Combine(Workspace, "backup", Path.GetRelativePath(_home, target)), requireAbsent));
    }

    public void Commit(Action publish, CancellationToken token)
    {
        if (_state != State.Preparing) throw new InvalidOperationException("The transaction has already been applied");
        token.ThrowIfCancellationRequested();
        foreach (var change in _changes)
        {
            if (HasLinkBelow(change.Target, _home)) throw new IOException($"Update target '{change.Target}' traverses a symbolic link");
            change.HadOriginal = Path.Exists(change.Target);
            if (change.RequireAbsent && change.HadOriginal) throw new IOException($"Installation target '{change.Target}' already exists");
            if (change.HadOriginal && Directory.Exists(change.Target) != (change.Kind == EntryKind.Directory))
                throw new IOException($"Update target '{change.Target}' has an unexpected file type");
            if (change.Staged is not null && (!Path.Exists(change.Staged)
                || Directory.Exists(change.Staged) != (change.Kind == EntryKind.Directory)))
                throw new IOException($"Staged update '{change.Staged}' is missing or has an unexpected file type");
        }
        WriteJournal("prepared");
        token.ThrowIfCancellationRequested();
        _state = State.Applying;
        try
        {
            foreach (var change in _changes)
            {
                if (change.HadOriginal)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(change.Backup)!);
                    _files.Move(change.Target, change.Backup, change.Kind);
                    change.BackedUp = true;
                    WriteJournal("applying");
                }
                if (change.Staged is not null)
                {
                    EnsureParent(change.Target);
                    _files.Move(change.Staged, change.Target, change.Kind);
                    change.Installed = true;
                    WriteJournal("applying");
                }
            }
            publish();
            _state = State.Committed;
        }
        catch (Exception failure)
        {
            var errors = Rollback();
            RecordRollback(errors.Count == 0 ? "rolledBack" : "recoveryRequired", errors);
            if (errors.Count > 0)
            {
                _state = State.RecoveryRequired;
                throw new RecoveryException(Workspace, failure, errors);
            }
            _state = State.RolledBack;
            throw;
        }
        try
        {
            WriteJournal("committed");
        }
        catch (Exception error)
        {
            _state = State.RecoveryRequired;
            _logger.LogWarning(error, "Could not record the completed update; recovery files remain at {workspace}", Workspace);
        }
    }

    private List<Exception> Rollback()
    {
        var errors = new List<Exception>();
        foreach (var change in _changes.AsEnumerable().Reverse())
        {
            try
            {
                if (change.Installed)
                {
                    _files.Move(change.Target, change.Staged!, change.Kind);
                    change.Installed = false;
                    RecordRollback("rollingBack", errors);
                }
                if (change.BackedUp)
                {
                    EnsureParent(change.Target);
                    _files.Move(change.Backup, change.Target, change.Kind);
                    change.BackedUp = false;
                    RecordRollback("rollingBack", errors);
                }
            }
            catch (Exception error)
            {
                errors.Add(new IOException($"Could not restore '{change.Target}' from '{change.Backup}'", error));
            }
        }
        foreach (var directory in _createdParents.AsEnumerable().Reverse())
        {
            try
            {
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory);
            }
            catch (Exception error)
            {
                _logger.LogWarning(error, "Could not remove empty update directory {directory}", directory);
            }
        }
        return errors;
    }

    private void RecordRollback(string status, List<Exception> errors)
    {
        try
        {
            WriteJournal(status);
        }
        catch (Exception error)
        {
            errors.Add(new IOException("Could not record rollback progress", error));
        }
    }

    private void EnsureParent(string path)
    {
        var missing = new Stack<string>();
        for (var parent = Path.GetDirectoryName(path)!; !Directory.Exists(parent); parent = Path.GetDirectoryName(parent)!)
            missing.Push(parent);
        while (missing.TryPop(out var directory))
        {
            Directory.CreateDirectory(directory);
            _createdParents.Add(directory);
        }
    }

    private string TargetPath(string relative)
    {
        var target = Path.GetFullPath(Path.Combine(_home, relative));
        if (Path.IsPathRooted(relative) || !FileHelper.IsInDirectory(target, _home)
         || FileHelper.IsPathEquivalent(target, _home)
         || FileHelper.IsPathEquivalent(target, Workspace) || FileHelper.IsInDirectory(target, Workspace)
         || FileHelper.IsPathEquivalent(target, Workspace + ".lock"))
            throw new InvalidDataException($"Unsafe update target '{relative}'");
        return target;
    }

    internal static bool HasLinkBelow(string path, string root)
    {
        for (var current = Path.GetFullPath(path); !FileHelper.IsPathEquivalent(current, root); current = Path.GetDirectoryName(current)!)
        {
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
        }
        return false;
    }

    private void WriteJournal(string status) => _files.WriteJournal(Path.Combine(Workspace, "journal.json"),
        JsonSerializer.Serialize(new
        {
            FormatVersion = 1,
            Status = status,
            Changes = _changes.Select(x => new
            {
                Target = Path.GetRelativePath(_home, x.Target), Kind = x.Kind.ToString(), x.HadOriginal, x.BackedUp, x.Installed, x.RequireAbsent,
                Staged = x.Staged is null ? null : Path.GetRelativePath(Workspace, x.Staged),
                Backup = Path.GetRelativePath(Workspace, x.Backup)
            }).ToArray()
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

    public void Dispose()
    {
        if (_state == State.Disposed) return;
        try
        {
            // WARNING: Only a completed commit or rollback permits deleting recovery material.
            if (_state is State.Preparing or State.Committed or State.RolledBack)
            {
                try
                {
                    _files.DeleteDirectory(Workspace);
                }
                catch (Exception error)
                {
                    _logger.LogWarning(error, "Update files remain at {workspace}; inspect them before retrying", Workspace);
                }
            }
        }
        finally
        {
            _lease.Dispose();
            _state = State.Disposed;
        }
    }

    internal enum EntryKind { File, Directory }
    private enum State { Preparing, Applying, Committed, RolledBack, RecoveryRequired, Disposed }

    private sealed class Change(string target, EntryKind kind, string? staged, string backup, bool requireAbsent)
    {
        public string Target { get; } = target;
        public EntryKind Kind { get; } = kind;
        public string? Staged { get; } = staged;
        public string Backup { get; } = backup;
        public bool RequireAbsent { get; } = requireAbsent;
        public bool HadOriginal { get; set; }
        public bool BackedUp { get; set; }
        public bool Installed { get; set; }
    }

    internal class FileOperations
    {
        public virtual void Move(string source, string target, EntryKind kind)
        {
            if (kind == EntryKind.Directory) Directory.Move(source, target);
            else File.Move(source, target);
        }

        public virtual void WriteJournal(string path, string content)
        {
            var temporary = path + ".next";
            File.WriteAllText(temporary, content);
            File.Move(temporary, path, true);
        }

        public virtual void DeleteDirectory(string path)
        {
            foreach (var directory in Directory.GetDirectories(path)) Directory.Delete(directory, true);
            var journal = Path.Combine(path, "journal.json");
            foreach (var file in Directory.GetFiles(path).Where(x => x != journal)) File.Delete(file);
            File.Delete(journal);
            Directory.Delete(path);
        }
    }

    internal sealed class RecoveryException(string workspace, Exception failure, IReadOnlyList<Exception> rollbackErrors)
        : IOException($"Update rollback is incomplete; recovery files are retained at '{workspace}'",
            new AggregateException([failure, .. rollbackErrors]))
    {
        public string Workspace { get; } = workspace;
    }
}

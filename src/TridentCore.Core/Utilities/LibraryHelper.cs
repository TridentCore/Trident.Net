using TridentCore.Abstractions.FileModels;
using TridentCore.Core.Extensions;

namespace TridentCore.Core.Utilities;

public static class LibraryHelper
{
    public static LockData.ArtifactData Resolve(LockData.ArtifactData artifact)
    {
        var libraries = Resolve(artifact.AllLibraries());
        return artifact with
        {
            Libraries = artifact.MainJar is null ? libraries : libraries.Take(libraries.Count - 1).ToArray(),
            MainJar = artifact.MainJar is null ? null : libraries[^1],
            Agents = ResolveAgents(artifact.Agents, x => x.Library.Id)
        };
    }

    public static IReadOnlyList<LockData.Library> Resolve(IEnumerable<LockData.Library> source) =>
        Resolve(source, Identify,
                (earlier, winner) => winner with { IsPresent = earlier.IsPresent || winner.IsPresent });

    public static IReadOnlyList<T> ResolveAgents<T>(IEnumerable<T> source, Func<T, LockData.Library.Identity> identity) =>
        Resolve(source, x => Identify(identity(x), Usage.Agent), (_, winner) => winner);

    public static IReadOnlyList<LockData.Library> Insert(
        IReadOnlyList<LockData.Library> original, IReadOnlyList<LockData.Library> additions, int position) =>
        Insert(original, additions, position, Identify, Resolve);

    public static IReadOnlyList<LockData.Agent> InsertAgents(
        IReadOnlyList<LockData.Agent> original, IReadOnlyList<LockData.Agent> additions, int position) =>
        Insert(original, additions, position, x => Identify(x.Library.Id, Usage.Agent),
               source => ResolveAgents(source, x => x.Library.Id));

    private static IReadOnlyList<T> Insert<T>(
        IReadOnlyList<T> original, IReadOnlyList<T> additions, int position,
        Func<T, Slot> slot,
        Func<IEnumerable<T>, IReadOnlyList<T>> resolve)
    {
        var slots = additions.Select(slot).ToHashSet();
        var incoming = resolve(original.Concat(additions)).Where(x => slots.Contains(slot(x)));
        return [.. original.Take(position).Where(x => !slots.Contains(slot(x))),
                .. incoming,
                .. original.Skip(position).Where(x => !slots.Contains(slot(x)))];
    }

    private static IReadOnlyList<T> Resolve<T>(
        IEnumerable<T> source, Func<T, Slot> slot, Func<T, T, T> merge)
    {
        var items = source.ToList();
        var winners = new Dictionary<Slot, T>();
        var order = new List<Slot>();
        for (var i = items.Count - 1; i >= 0; i--)
        {
            var item = items[i];
            var identity = slot(item);
            if (winners.TryGetValue(identity, out var winner))
            {
                winners[identity] = merge(item, winner);
            }
            else
            {
                winners.Add(identity, item);
                order.Add(identity);
            }
        }
        order.Reverse();
        return order.Select(x => winners[x]).ToArray();
    }

    private static Slot Identify(LockData.Library library) =>
        Identify(library.Id, library.IsNative ? Usage.Native : library.IsPresent ? Usage.Classpath : Usage.Download);

    // NOTE: Installer processors reference download-only files by exact coordinates.
    private static Slot Identify(LockData.Library.Identity id, Usage usage) =>
        new(id.Namespace, id.Name, id.Platform, id.Extension, usage, usage == Usage.Download ? id.Version : null);

    private enum Usage { Classpath, Native, Agent, Download }

    private readonly record struct Slot(
        string Namespace, string Name, string? Platform, string Extension, Usage Usage, string? Version);
}

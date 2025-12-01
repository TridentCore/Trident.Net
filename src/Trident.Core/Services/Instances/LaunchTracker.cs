using System.Reactive.Subjects;
using Trident.Abstractions.Accounts;
using Trident.Abstractions.Tasks;
using Trident.Core.Engines.Launching;

namespace Trident.Core.Services.Instances;

public class LaunchTracker(
    string key,
    LaunchOptions options,
    Func<TrackerBase, Task> handler,
    Action<TrackerBase>? onCompleted,
    CancellationToken token = default) : TrackerBase(key, handler, onCompleted, token)
{
    public Subject<Scrap> ScrapStream { get; } = new();
    public LaunchOptions Options => options;

    public string? JavaHome { get; set; }
    public uint? JavaVersion { get; set; }
    public string? CommandLine { get; set; }
    public bool IsDetaching { get; set; }
}

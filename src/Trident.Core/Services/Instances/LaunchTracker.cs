using System.Reactive.Subjects;
using Trident.Core.Engines.Launching;
using Trident.Abstractions.Tasks;

namespace Trident.Core.Services.Instances;

public class LaunchTracker(
    string key,
    Func<TrackerBase, Task> handler,
    Action<TrackerBase>? onCompleted,
    CancellationToken token = default) : TrackerBase(key, handler, onCompleted, token)
{
    public Subject<Scrap> ScrapStream { get; } = new();

    public bool IsDetaching { get; set; } = false;
}

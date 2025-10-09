using System.Reactive.Subjects;
using Trident.Abstractions.Accounts;
using Trident.Abstractions.Tasks;
using Trident.Core.Engines.Launching;

namespace Trident.Core.Services.Instances;

public class LaunchTracker(
    string key,
    IAccount? account,
    Func<TrackerBase, Task> handler,
    Action<TrackerBase>? onCompleted,
    CancellationToken token = default) : TrackerBase(key, handler, onCompleted, token)
{
    public Subject<Scrap> ScrapStream { get; } = new();

    public IAccount? Account => account;
    public bool IsDetaching { get; set; }
}

using System.Reactive.Subjects;
using TridentCore.Abstractions.Tasks;

namespace TridentCore.Core.Services.Instances;

public class InstallTracker(
    string key,
    Func<TrackerBase, Task> handler,
    Action<TrackerBase>? onCompleted = null,
    CancellationToken token = default
) : TrackerBase(key, handler, onCompleted, token)
{
    public Subject<double?> ProgressStream { get; } = new();

    public string? Reference { get; set; }

    public override void Dispose()
    {
        base.Dispose();
        ProgressStream.Dispose();
    }
}

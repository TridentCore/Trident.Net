using System.Reactive.Subjects;
using Trident.Core.Engines.Deploying;
using Trident.Abstractions.Tasks;

namespace Trident.Core.Services.Instances
{
    public class DeployTracker(
        string key,
        Func<TrackerBase, Task> handler,
        Action<TrackerBase>? onCompleted = null,
        CancellationToken token = default) : TrackerBase(key, handler, onCompleted, token)
    {
        public Subject<(int, int)> ProgressStream { get; } = new();
        public Subject<DeployStage> StageStream { get; } = new();

        public DeployStage CurrentStage { get; internal set; } = DeployStage.CheckArtifact;

        public override void Dispose()
        {
            base.Dispose();
            ProgressStream.Dispose();
            StageStream.Dispose();
        }
    }
}

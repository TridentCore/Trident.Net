using System.Diagnostics;
using System.Reactive.Subjects;
using TridentCore.Abstractions;
using TridentCore.Abstractions.Tasks;
using TridentCore.Core.Engines.Launching;

namespace TridentCore.Core.Services.Instances;

public class LaunchTracker(
    string key,
    LaunchOptions options,
    Func<TrackerBase, Task> handler,
    Action<TrackerBase>? onCompleted,
    CancellationToken token = default
) : TrackerBase(key, handler, onCompleted, token)
{
    public override InstanceState Kind => InstanceState.Running;
    public Process? Process
    {
        get;
        internal set
        {
            field = value;
            if (value is not null)
            {
                ProcessAssigned?.Invoke(this, value);
            }
        }
    }
    public Subject<Scrap> ScrapStream { get; } = new();
    public LaunchOptions Options => options;
    public event EventHandler<Process>? ProcessAssigned;

    public string? JavaHome { get; set; }
    public uint? JavaVersion { get; set; }
    public string? CommandLine { get; set; }
    public bool IsDetaching { get; set; }

    protected override void OnStart()
    {
        ProcessAssigned += (_, _) => ReportProgress(new TrackerProgress.None());
        base.OnStart();
        ReportProgress(new TrackerProgress.Indeterminate("Launching"));
    }
}

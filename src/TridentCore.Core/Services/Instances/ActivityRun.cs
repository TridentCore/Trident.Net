using System.Reactive.Subjects;
using TridentCore.Abstractions.Tasks;

namespace TridentCore.Core.Services.Instances;

/// <summary>
///     一次活动的执行体：持有取消源、当前值，并把每次值变化发布出去。
///     活动的可变性收束在此，对外只流出不可变的 <see cref="InstanceActivity" />。
/// </summary>
/// <remarks>
///     <para>
///         生命周期由 <see cref="InstanceManager" /> 独占，消费方拿不到本类型。
///     </para>
///     <para>
///         WARNING: 流只 <c>OnCompleted</c>，绝不 <c>Dispose</c>。订阅者在完成时自行释放，
///         因此不存在「订阅到已释放的流」——历史上正是 Dispose 与通知同步进行导致了 UI 线程崩溃。
///     </para>
/// </remarks>
internal sealed class ActivityRun
{
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _source;
    private Task? _execution;

    // WARNING: 必须是 ReplaySubject(1) 而非 BehaviorSubject——后者在 OnCompleted 之后只给新订阅者
    //  发完成信号、不重放值，“先终结后订阅”的消费方会拿不到终态快照。
    private readonly ReplaySubject<InstanceActivity> _subject = new(1);

    public ActivityRun(InstanceActivity seed, CancellationToken token = default)
    {
        _source = CancellationTokenSource.CreateLinkedTokenSource(token);
        Current = seed;

        // 首值即当前态：订阅者一订上就拿到现状，无需另行查询。
        _subject.OnNext(seed);
    }

    public InstanceActivity Current { get; private set; }
    public IObservable<InstanceActivity> Stream => _subject;
    public CancellationToken Token => _source.Token;
    public string Key => Current.Key;

    /// <summary>由 InstanceManager 在执行体启动时登记，供等待终态落地（如退出收尾）。</summary>
    public void Track(Task task) => _execution = task;

    /// <summary>执行体完成即该活动终态（及其同步下游写入）已落地；未登记视为已完成。</summary>
    public Task Execution => _execution ?? Task.CompletedTask;

    /// <summary>按当前具体类型改写并发布。终态之后的改写被丢弃。</summary>
    public void Mutate<T>(Func<T, T> mutator) where T : InstanceActivity
    {
        InstanceActivity next;
        lock (_gate)
        {
            if (Current.IsCompleted)
            {
                return;
            }

            next = mutator((T)Current);
            Current = next;
        }

        // 在锁外发布：订阅者回调同步执行，持锁会把外部代码拖进临界区。
        _subject.OnNext(next);
    }

    public void Report(ActivityProgress progress) => Mutate<InstanceActivity>(x => x with { Progress = progress });

    public void Abort()
    {
        lock (_gate)
        {
            if (Current.IsCompleted)
            {
                return;
            }

            _source.Cancel();
        }
    }

    /// <summary>落终态并终结流。重复调用无副作用。</summary>
    public void Complete(ActivityState state, Exception? reason = null)
    {
        InstanceActivity next;
        lock (_gate)
        {
            if (Current.IsCompleted)
            {
                return;
            }

            next = Current with { State = state, FailureReason = reason };
            Current = next;
        }

        _subject.OnNext(next);
        _subject.OnCompleted();

        // 取消源已无人可用（Abort 在终态后直接返回），可安全释放。
        _source.Dispose();
    }
}

using System.Collections;
using Microsoft.Extensions.DependencyInjection;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Reactive;
using TridentCore.Core.Engines.Deploying;
using TridentCore.Core.Engines.Deploying.Stages;
using TridentCore.Core.Services.Instances;

namespace TridentCore.Core.Engines;

// Fixed linear pipeline: every stage runs in order and decides internally (against BaseLock)
// whether to migrate, rebuild, or no-op. There is no state-machine branching — DecideNext is
// gone, replaced by a static yield sequence.
public class DeployEngine(
    string key,
    Profile.Rice setup,
    IServiceProvider provider,
    DeployEngineOptions options,
    string optionsHash,
    string priorityHash,
    JavaHomeLocatorDelegate javaHomeLocator
) : IEnumerable<StageBase>
{
    #region IEnumerable<StageBase> Members

    public IEnumerator<StageBase> GetEnumerator() =>
        new DeployEngineEnumerator(
            new(key, setup, provider, options, optionsHash, priorityHash, javaHomeLocator)
        );

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    #endregion

    #region Nested type: DeployEngineEnumerator

    private class DeployEngineEnumerator(DeployContext context) : IEnumerator<StageBase>
    {
        private static readonly Type[] SEQUENCE =
        [
            typeof(LoadLockStage),
            typeof(InstallVanillaStage),
            typeof(ProcessLoaderStage),
            typeof(SyncPackagesStage),
            typeof(FlattenPackagesStage),
            typeof(EnsureRuntimeStage),
            typeof(PersistLockStage),
            typeof(GenerateManifestStage),
            typeof(SolidifyManifestStage)
        ];

        private int _index = -1;

        #region IEnumerator<StageBase> Members

        public void Reset() => throw new NotImplementedException();

        public bool MoveNext()
        {
            if (Current is IDisposableLifetime disposable)
            {
                disposable.Dispose();
            }

            _index++;
            if (_index < SEQUENCE.Length)
            {
                Current = CreateStage(SEQUENCE[_index]);
                return true;
            }

            Current = null!;
            return false;
        }

        public StageBase Current { get; private set; } = null!;

        object IEnumerator.Current => Current;

        public void Dispose()
        {
            // 中断导致没有 MoveNext
            if (Current is IDisposableLifetime disposable)
            {
                disposable.Dispose();
            }
        }

        #endregion

        private StageBase CreateStage(Type type)
        {
            var stage = (StageBase)ActivatorUtilities.CreateInstance(
                context.Provider,
                type
            );
            stage.Context = context;
            return stage;
        }
    }

    #endregion
}

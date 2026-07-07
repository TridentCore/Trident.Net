using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Utilities;
using TridentCore.Core.Services.Instances;

namespace TridentCore.Core.Utilities;

// Fingerprint of the source-overlay priority (modpack Source + SourceOrders). Kept separate
// from OptionsHash so reordering layers invalidates the FastMode gate (Verify) without falsely
// signaling an options change that would trigger floating re-resolution in SyncPackages.
public static class ViabilityHashHelper
{
    public static string PriorityOf(Profile.Rice setup) =>
        HashHelper.ComputeObjectHash(
            new { setup.Source, Order = string.Join('\n', setup.SourceOrders) }
        );

    public static string OptionsOf(DeployOptions options) => HashHelper.ComputeObjectHash(options);
}

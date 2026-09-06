using TridentCore.Abstractions.Utilities;

namespace TridentCore.Abstractions.Launching;

public sealed record LaunchAssetIndex(string Id, Uri Url, FileHash? Hash = null);

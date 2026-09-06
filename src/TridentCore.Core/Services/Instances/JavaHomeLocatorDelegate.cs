using TridentCore.Core.Utilities;

namespace TridentCore.Core.Services.Instances;

public delegate Task<JavaHelper.JavaResolution> JavaHomeLocatorDelegate(IReadOnlyList<uint> majors, CancellationToken token);

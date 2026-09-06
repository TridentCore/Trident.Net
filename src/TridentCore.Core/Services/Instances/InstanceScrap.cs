using TridentCore.Core.Engines.Launching;

namespace TridentCore.Core.Services.Instances;

/// <summary>
///     带实例归属的游戏进程输出。<see cref="InstanceManager.Scraps" /> 是全局流，
///     故每条输出自带 Key，消费方据此分流。
/// </summary>
public sealed record InstanceScrap(string Key, Scrap Scrap);

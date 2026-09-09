using TridentCore.Core.Engines.Launching;

namespace TridentCore.Core.Services.Instances;

/// <summary>
///     带实例归属的游戏进程输出。<see cref="InstanceManager.Scraps" /> 是全局流，
///     每条输出携带实例 Key 和运行活动 Id，消费方据此区分不同会话。
/// </summary>
public sealed record InstanceScrap(string Key, Guid ActivityId, Scrap Scrap);

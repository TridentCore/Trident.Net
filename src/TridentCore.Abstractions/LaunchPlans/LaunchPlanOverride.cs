using JetBrains.Annotations;

namespace TridentCore.Abstractions.LaunchPlans;

// 一次跨层覆盖的记录：Layer 声明的 Subject 顶替了下层已确立的 Previous，最终取 Current。
// 折叠不阻止覆盖（后叠加者赢就是叠加的定义），只如实产出记录供诊断呈现——用户改了 Profile
// 版本却被来源层顶回旧值时，这是唯一能解释「为什么没生效」的信息。
[PublicAPI]
public sealed record LaunchPlanOverride(LaunchLayer Layer, string Subject, string Previous, string Current);

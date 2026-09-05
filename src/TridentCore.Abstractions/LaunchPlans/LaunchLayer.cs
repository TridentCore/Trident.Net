using JetBrains.Annotations;

namespace TridentCore.Abstractions.LaunchPlans;

// 启动计划的唯一中间表示——一段有序 operation 加它的来源身份。vanilla、loader、来源层与用户层
// 同质：叠加语义对四者完全一致，Origin 只用于决定诊断口径（平台层内部的相互覆盖是设计使然）。
public sealed record LaunchLayer(
    LaunchLayer.Origin Kind,
    string Label,
    IReadOnlyList<LaunchPlanDocument.Operation> Operations)
{
    #region Nested type: Origin

    [PublicAPI]
    public enum Origin
    {
        // vanilla 与 mod loader——由 Profile 的版本与加载器声明计算得出，不落盘为计划文件。
        Platform,

        // launch/source/ 下由导入流程托管的层，随整合包更新整体替换。
        Managed,

        // launch/ 根目录下用户自维护的层，永不进出整合包。
        User
    }

    #endregion
}

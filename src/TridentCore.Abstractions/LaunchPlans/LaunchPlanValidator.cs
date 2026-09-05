using TridentCore.Abstractions.Utilities;

namespace TridentCore.Abstractions.LaunchPlans;

// operation 序列的静态校验——不需要先构造任何累加器即可判定一段 operation 是否可用。
// 空白参数在此统一判为非法：生产者（平台阶段、格式转换）负责在写入前剔除，读入侧一律拒绝。
public static class LaunchPlanValidator
{
    public static void Validate(IReadOnlyList<LaunchPlanDocument.Operation> operations)
    {
        for (var i = 0; i < operations.Count; i++)
        {
            var operation = operations[i] ?? throw new FormatException($"Launch plan operation {i} is null");
            switch (operation)
            {
                case LaunchPlanDocument.SetMainClassOperation set when string.IsNullOrWhiteSpace(set.Value):
                    throw new FormatException($"Launch plan operation {i} has an empty main class");
                case LaunchPlanDocument.SetAssetIndexOperation set when set.Value is null:
                    throw new FormatException($"Launch plan operation {i} has a null asset index");
                case LaunchPlanDocument.SetAssetIndexOperation set when !LibraryHelper.IsSafeIdentifier(set.Value.Id):
                    throw new FormatException($"Launch plan operation {i} has an invalid asset index");
                case LaunchPlanDocument.SetJavaMajorVersionOperation set when set.Value == 0:
                    throw new FormatException($"Launch plan operation {i} has an invalid Java major version");
                case LaunchPlanDocument.SetGameArgumentsOperation set when set.Values is null:
                    throw new FormatException($"Launch plan operation {i} has null game arguments");
                case LaunchPlanDocument.SetGameArgumentsOperation set when set.Values.Any(string.IsNullOrWhiteSpace):
                    throw new FormatException($"Launch plan operation {i} has an empty game argument");
                case LaunchPlanDocument.AppendGameArgumentOperation append
                    when string.IsNullOrWhiteSpace(append.Value):
                    throw new FormatException($"Launch plan operation {i} has an empty game argument");
                case LaunchPlanDocument.AppendJavaArgumentOperation append
                    when string.IsNullOrWhiteSpace(append.Value):
                    throw new FormatException($"Launch plan operation {i} has an empty JVM argument");
                case LaunchPlanDocument.AddLibraryOperation add when add.Value is null:
                    throw new FormatException($"Launch plan operation {i} has a null library");
                case LaunchPlanDocument.AddLibraryOperation add:
                    LibraryHelper.ValidateIdentity(add.Value.Id);
                    break;
                case LaunchPlanDocument.RemoveLibrariesOperation remove when remove.Selector is null:
                    throw new FormatException($"Launch plan operation {i} has a null library selector");
            }
        }
    }
}

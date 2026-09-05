using System.Collections.Immutable;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Utilities;

namespace TridentCore.Abstractions.LaunchPlans;

// 启动计划的唯一动作：把有序层折叠成一份可执行结果。层与层之间只有先后，没有种类差异——
// 后叠加者覆盖先前的声明就是叠加的定义，折叠不阻止覆盖，只如实报告。
public static class LaunchPlanFolder
{
    public static FoldResult Fold(IEnumerable<LaunchLayer> layers)
    {
        string? mainClass = null;
        uint? javaMajorVersion = null;
        LockData.AssetData? assetIndex = null;
        List<string> gameArguments = [];
        List<string> javaArguments = [];
        List<LockData.Library> libraries = [];
        var overrides = new List<LaunchPlanOverride>();

        foreach (var layer in layers)
        {
            LaunchPlanValidator.Validate(layer.Operations);
            foreach (var operation in layer.Operations)
            {
                switch (operation)
                {
                    case LaunchPlanDocument.SetMainClassOperation set:
                        Displace(overrides, layer, "main class", mainClass, set.Value);
                        mainClass = set.Value;
                        break;
                    case LaunchPlanDocument.SetJavaMajorVersionOperation set:
                        Displace(overrides,
                                 layer,
                                 "Java major version",
                                 javaMajorVersion?.ToString(),
                                 set.Value.ToString());
                        javaMajorVersion = set.Value;
                        break;
                    case LaunchPlanDocument.SetAssetIndexOperation set:
                        Displace(overrides, layer, "asset index", assetIndex?.Id, set.Value.Id);
                        assetIndex = set.Value;
                        break;
                    case LaunchPlanDocument.ClearGameArgumentsOperation:
                        gameArguments.Clear();
                        break;
                    case LaunchPlanDocument.SetGameArgumentsOperation set:
                        gameArguments = [.. set.Values];
                        break;
                    case LaunchPlanDocument.AppendGameArgumentOperation append:
                        gameArguments.Add(append.Value);
                        break;
                    case LaunchPlanDocument.ClearJavaArgumentsOperation:
                        javaArguments.Clear();
                        break;
                    case LaunchPlanDocument.AppendJavaArgumentOperation append:
                        javaArguments.Add(append.Value);
                        break;
                    case LaunchPlanDocument.AddLibraryOperation add:
                        Displace(overrides,
                                 layer,
                                 $"library {add.Value.Id.Namespace}:{add.Value.Id.Name}",
                                 LibraryHelper.Merge(libraries, add.Value)?.Id.Version,
                                 add.Value.Id.Version);
                        break;
                    case LaunchPlanDocument.RemoveLibrariesOperation remove:
                        libraries.RemoveAll(remove.Selector.Matches);
                        break;
                    default:
                        throw new
                            InvalidOperationException($"Unknown launch plan operation: {operation.GetType().Name}");
                }
            }
        }

        var result =
            new LaunchPlanResult(mainClass ?? throw new InvalidOperationException("Launch plan has no main class"),
                                 javaMajorVersion
                              ?? throw new InvalidOperationException("Launch plan has no Java major version"),
                                 [.. gameArguments],
                                 [.. javaArguments],
                                 [.. libraries],
                                 assetIndex
                              ?? throw new InvalidOperationException("Launch plan has no asset index"));
        return new(result, overrides);
    }

    // 只报告跨层覆盖：平台层内部 vanilla 与 loader 相互压制是设计使然（Forge 本就重写
    // args/mainClass），逐条报告只会淹没真正需要用户知晓的外部层覆盖。
    private static void Displace(
        ICollection<LaunchPlanOverride> overrides,
        LaunchLayer layer,
        string subject,
        string? previous,
        string current)
    {
        if (previous is not null && previous != current && layer.Kind != LaunchLayer.Origin.Platform)
        {
            overrides.Add(new(layer, subject, previous, current));
        }
    }

    #region Nested type: FoldResult

    public sealed record FoldResult(LaunchPlanResult Result, IReadOnlyList<LaunchPlanOverride> Overrides);

    #endregion
}

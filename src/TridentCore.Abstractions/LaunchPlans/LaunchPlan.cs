using System.Collections.Immutable;
using JetBrains.Annotations;
using TridentCore.Abstractions.FileModels;
using TridentCore.Abstractions.Utilities;

namespace TridentCore.Abstractions.LaunchPlans;

public sealed class LaunchPlan
{
    private readonly LaunchPlanResult? _baseResult;
    private readonly List<LaunchPlanDocument.Operation> _operations = [];

    public LaunchPlan(LaunchPlanResult? baseResult = null) => _baseResult = baseResult;

    public static LaunchPlan FromResult(LaunchPlanResult result) => new(result);

    public LaunchPlan Apply(LaunchPlanDocument document)
    {
        for (var i = 0; i < document.Operations.Count; i++)
        {
            var operation = document.Operations[i]
                           ?? throw new FormatException($"Launch plan operation {i} is null");
            Validate(operation, i);
            _operations.Add(operation);
        }

        return this;
    }

    public LaunchPlan SetMainClass(string value)
    {
        _operations.Add(new LaunchPlanDocument.SetMainClassOperation(value));
        return this;
    }

    public LaunchPlan SetJavaMajorVersion(uint value)
    {
        _operations.Add(new LaunchPlanDocument.SetJavaMajorVersionOperation(value));
        return this;
    }

    public LaunchPlan SetAssetIndex(LockData.AssetData value)
    {
        _operations.Add(new LaunchPlanDocument.SetAssetIndexOperation(value));
        return this;
    }

    public LaunchPlan ClearGameArguments()
    {
        _operations.Add(new LaunchPlanDocument.ClearGameArgumentsOperation());
        return this;
    }

    [PublicAPI]
    public LaunchPlan SetGameArguments(IEnumerable<string> values)
    {
        _operations.Add(new LaunchPlanDocument.SetGameArgumentsOperation(values.ToArray()));
        return this;
    }

    public LaunchPlan AppendGameArgument(string value)
    {
        value = value.Trim();
        if (value.Length > 0)
        {
            _operations.Add(new LaunchPlanDocument.AppendGameArgumentOperation(value));
        }

        return this;
    }

    public LaunchPlan AppendJavaArgument(string value)
    {
        value = value.Trim();
        if (value.Length > 0)
        {
            _operations.Add(new LaunchPlanDocument.AppendJavaArgumentOperation(value));
        }

        return this;
    }

    [PublicAPI]
    public LaunchPlan ClearJavaArguments()
    {
        _operations.Add(new LaunchPlanDocument.ClearJavaArgumentsOperation());
        return this;
    }

    public LaunchPlan AddLibrary(LockData.Library value)
    {
        _operations.Add(new LaunchPlanDocument.AddLibraryOperation(value));
        return this;
    }

    [PublicAPI]
    public LaunchPlan RemoveLibraries(LaunchPlanDocument.LibrarySelector selector)
    {
        _operations.Add(new LaunchPlanDocument.RemoveLibrariesOperation(selector));
        return this;
    }

    public LaunchPlanResult Resolve()
    {
        var mainClass = _baseResult?.MainClass;
        var javaMajorVersion = _baseResult?.JavaMajorVersion;
        List<string> gameArguments = _baseResult is null ? [] : [.. _baseResult.GameArguments];
        List<string> javaArguments = _baseResult is null ? [] : [.. _baseResult.JavaArguments];
        List<LockData.Library> libraries = _baseResult is null ? [] : [.. _baseResult.Libraries];
        var assetIndex = _baseResult?.AssetIndex;

        foreach (var operation in _operations)
        {
            switch (operation)
            {
                case LaunchPlanDocument.SetMainClassOperation set:
                    mainClass = set.Value;
                    break;
                case LaunchPlanDocument.SetJavaMajorVersionOperation set:
                    javaMajorVersion = set.Value;
                    break;
                case LaunchPlanDocument.SetAssetIndexOperation set:
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
                case LaunchPlanDocument.AppendJavaArgumentOperation append:
                    javaArguments.Add(append.Value);
                    break;
                case LaunchPlanDocument.ClearJavaArgumentsOperation:
                    javaArguments.Clear();
                    break;
                case LaunchPlanDocument.AddLibraryOperation add:
                    LibraryHelper.Merge(libraries, add.Value);
                    break;
                case LaunchPlanDocument.RemoveLibrariesOperation remove:
                    libraries.RemoveAll(remove.Selector.Matches);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown launch plan operation: {operation.GetType().Name}");
            }
        }

        return new(mainClass ?? throw new InvalidOperationException("Launch plan has no main class"),
                    javaMajorVersion ?? throw new InvalidOperationException("Launch plan has no Java major version"),
                    gameArguments.ToImmutableArray(),
                    javaArguments.ToImmutableArray(),
                    libraries.ToImmutableArray(),
                    assetIndex ?? throw new InvalidOperationException("Launch plan has no asset index"));
    }

    private static void Validate(LaunchPlanDocument.Operation operation, int index)
    {
        switch (operation)
        {
            case LaunchPlanDocument.SetMainClassOperation set when string.IsNullOrWhiteSpace(set.Value):
                throw new FormatException($"Launch plan operation {index} has an empty main class");
            case LaunchPlanDocument.SetAssetIndexOperation set when set.Value is null:
                throw new FormatException($"Launch plan operation {index} has a null asset index");
            case LaunchPlanDocument.SetAssetIndexOperation set when !LibraryHelper.IsSafeIdentifier(set.Value.Id):
                throw new FormatException($"Launch plan operation {index} has an invalid asset index");
            case LaunchPlanDocument.SetJavaMajorVersionOperation set when set.Value == 0:
                throw new FormatException($"Launch plan operation {index} has an invalid Java major version");
            case LaunchPlanDocument.SetGameArgumentsOperation set when set.Values is null:
                throw new FormatException($"Launch plan operation {index} has null game arguments");
            case LaunchPlanDocument.SetGameArgumentsOperation set when set.Values.Any(string.IsNullOrWhiteSpace):
                throw new FormatException($"Launch plan operation {index} has an empty game argument");
            case LaunchPlanDocument.AppendGameArgumentOperation append when string.IsNullOrWhiteSpace(append.Value):
                throw new FormatException($"Launch plan operation {index} has an empty game argument");
            case LaunchPlanDocument.AppendJavaArgumentOperation append when string.IsNullOrWhiteSpace(append.Value):
                throw new FormatException($"Launch plan operation {index} has an empty JVM argument");
            case LaunchPlanDocument.AddLibraryOperation add when add.Value is null:
                throw new FormatException($"Launch plan operation {index} has a null library");
            case LaunchPlanDocument.RemoveLibrariesOperation remove when remove.Selector is null:
                throw new FormatException($"Launch plan operation {index} has a null library selector");
        }
    }
}

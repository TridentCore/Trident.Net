using System.Text.Json.Serialization;
using JetBrains.Annotations;
using TridentCore.Abstractions.FileModels;

namespace TridentCore.Abstractions.LaunchPlans;

[PublicAPI]
public sealed class LaunchPlanDocument
{
    public IReadOnlyList<Operation> Operations { get; init; } = [];

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
    [JsonDerivedType(typeof(SetMainClassOperation), "set-main-class")]
    [JsonDerivedType(typeof(SetJavaMajorVersionOperation), "set-java-major-version")]
    [JsonDerivedType(typeof(SetAssetIndexOperation), "set-asset-index")]
    [JsonDerivedType(typeof(ClearGameArgumentsOperation), "clear-game-arguments")]
    [JsonDerivedType(typeof(AppendGameArgumentOperation), "append-game-argument")]
    [JsonDerivedType(typeof(ClearJavaArgumentsOperation), "clear-java-arguments")]
    [JsonDerivedType(typeof(AppendJavaArgumentOperation), "append-java-argument")]
    [JsonDerivedType(typeof(AddLibraryOperation), "add-library")]
    [JsonDerivedType(typeof(RemoveLibrariesOperation), "remove-libraries")]
    public abstract record Operation;

    public sealed record SetMainClassOperation(string Value) : Operation;

    public sealed record SetJavaMajorVersionOperation(uint Value) : Operation;

    public sealed record SetAssetIndexOperation(LockData.AssetData Value) : Operation;

    public sealed record ClearGameArgumentsOperation : Operation;

    public sealed record AppendGameArgumentOperation(string Value) : Operation;

    public sealed record ClearJavaArgumentsOperation : Operation;

    public sealed record AppendJavaArgumentOperation(string Value) : Operation;

    public sealed record AddLibraryOperation(LockData.Library Value) : Operation;

    public sealed record RemoveLibrariesOperation(LibrarySelector Selector) : Operation;

    [PublicAPI]
    public sealed record LibrarySelector(
        string? Namespace = null,
        string? Name = null,
        string? Version = null,
        string? Platform = null,
        string? Extension = null,
        bool? IsNative = null)
    {
        public bool Matches(LockData.Library library) =>
            (Namespace is null || library.Id.Namespace == Namespace)
         && (Name is null || library.Id.Name == Name)
         && (Version is null || library.Id.Version == Version)
         && (Platform is null || library.Id.Platform == Platform)
         && (Extension is null || library.Id.Extension == Extension)
         && (IsNative is null || library.IsNative == IsNative);
    }
}

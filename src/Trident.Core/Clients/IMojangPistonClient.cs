using Refit;
using Trident.Core.Models.MojangLauncherApi;

namespace Trident.Core.Clients;

public interface IMojangPistonClient
{
    [Get("/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json")]
    Task<IReadOnlyDictionary<string, IDictionary<string, IReadOnlyList<RuntimeEntry>>>> GetRuntimeManifestAsync();
}

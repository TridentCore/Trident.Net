using System.Reflection;
using Refit;

namespace TridentCore.Core.Repositories;

// NOTE: Refit renders bool query values as "True"/"False" (C# bool.ToString()). APIs that
//  deserialize bool strictly — e.g. Rust serde behind actix-web, which Modrinth v3 uses —
//  reject PascalCase and return 400 ("provided string was not `true` or `false`"). Normalize
//  bool to lowercase and defer everything else to the default formatter.
public class LowercaseBoolUrlParameterFormatter : DefaultUrlParameterFormatter
{
    public override string? Format(object? value, ICustomAttributeProvider attributeProvider, Type type) =>
        value is bool b ? b ? "true" : "false" : base.Format(value, attributeProvider, type);
}

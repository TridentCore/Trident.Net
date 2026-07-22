using System.Text;
using IBuilder;

namespace TridentCore.Pref.Building;

public class Builder : IBuilder<string>
{
    public required string Repository { get; set; }
    public required string Identity { get; set; }
    public string? Namespace { get; set; }
    public string? Version { get; set; }
    public IList<(string, string?)> Filters { get; } = [];

    #region IBuilder<string> Members

    public string Build() => Build(Repository, Namespace, Identity, Version, Filters.ToArray().AsSpan());

    #endregion

    public static string Build(
        string repository,
        string? @namespace,
        string identity,
        string? version,
        ReadOnlySpan<(string, string?)> filters = default)
    {
        var builder = new StringBuilder();
        builder.Append("pref://").Append(repository);
        builder.Append(@namespace != null ? $"/{@namespace}/{identity}" : $"/{identity}");
        if (version != null)
        {
            builder.Append('@');
            builder.Append(version);
        }

        if (!filters.IsEmpty)
        {
            var query = new StringBuilder();
            foreach (var (key, value) in filters)
            {
                if (value == null)
                {
                    continue;
                }

                if (query.Length > 0)
                {
                    query.Append('&');
                }

                query.Append(key).Append('=').Append(value);
            }

            if (query.Length > 0)
            {
                builder.Append('?');
                builder.Append(query);
            }
        }

        return builder.ToString();
    }
}

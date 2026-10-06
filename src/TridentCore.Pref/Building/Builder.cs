using System.Text;
using IBuilder;
using TridentCore.Pref.Utilities;

namespace TridentCore.Pref.Building;

public class Builder : IBuilder<string>
{
    public const string Scheme = "pref";

    public required string Repository { get; set; }
    public required string Identity { get; set; }
    public string? Namespace { get; set; }
    public string? Version { get; set; }
    public IList<(string, string?)> Filters { get; } = [];

    #region IBuilder<string> Members

    public string Build() => Build(Repository, Namespace, Identity, Version, [.. Filters]);

    #endregion

    public static string Build(
        string repository,
        string? @namespace,
        string identity,
        string? version,
        ReadOnlySpan<(string, string?)> filters = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        if (!InternalUriHelper.TryParse($"{Scheme}://{repository}/", Scheme, out var uri)
            || !uri.Host.Equals(repository, StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath != "/" || uri.Query.Length != 0)
        {
            throw new ArgumentException("A package repository must be a URI host.", nameof(repository));
        }

        if (@namespace is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(@namespace);
        }

        if (version is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(version);
        }

        var builder = new StringBuilder();
        builder.Append(Scheme).Append("://").Append(uri.Host);
        if (@namespace is not null)
        {
            builder.Append('/').Append(Uri.EscapeDataString(@namespace));
        }

        builder.Append('/').Append(Uri.EscapeDataString(identity));
        if (version != null)
        {
            builder.Append('@');
            builder.Append(Uri.EscapeDataString(version));
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

                ArgumentException.ThrowIfNullOrWhiteSpace(key);
                query.Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value));
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

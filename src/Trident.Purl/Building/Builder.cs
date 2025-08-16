using System.Text;

namespace Trident.Purl.Building
{
    public class Builder : IBuilder.IBuilder<string>
    {
        public required string Repository { get; set; }
        public required string Identity { get; set; }
        public string? Namespace { get; set; }
        public string? Version { get; set; }
        public IList<(string, string)> Filters { get; } = [];

        public string Build() => Build(Repository, Identity, Namespace, Version, Filters.ToArray().AsSpan());

        public static string Build(
            string repository,
            string identity,
            string? @namespace = null,
            string? version = null,
            ReadOnlySpan<(string, string)> filters = default)
        {
            var builder = new StringBuilder();
            builder.Append(repository);
            builder.Append(':');
            if (@namespace != null)
            {
                builder.Append(@namespace);
                builder.Append('/');
            }

            builder.Append(identity);
            if (version != null)
            {
                builder.Append('@');
                builder.Append(version);
            }

            if (!filters.IsEmpty)
            {
                foreach (var (key, value) in filters)
                {
                    builder.Append('#');
                    builder.Append(key);
                    builder.Append('=');
                    builder.Append(value);
                }
            }

            return builder.ToString();
        }
    }
}
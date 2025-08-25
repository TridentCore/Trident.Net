using System.Text.RegularExpressions;
using IParser;

namespace Trident.Purl.Parsing
{
    public partial class Parser : IParser<string, PackageDescriptor>
    {
        public static readonly Parser Default = new();
        private readonly Regex _pattern = GenerateRegex();

        #region IParser<string,PackageDescriptor> Members

        public PackageDescriptor Parse(string input)
        {
            var match = _pattern.Match(input);
            if (match.Success)
            {
                if (match.Groups.TryGetValue("label", out var label)
                 && match.Groups.TryGetValue("identity", out var identity))
                {
                    match.Groups.TryGetValue("namespace", out var @namespace);
                    match.Groups.TryGetValue("version", out var version);
                    match.Groups.TryGetValue("filter", out var filters);
                    match.Groups.TryGetValue("value", out var values);

                    if (filters is { Captures.Count: > 0 }
                     && values is { Captures.Count: > 0 }
                     && filters.Captures.Count == values.Captures.Count)
                    {
                        return new(label.Value,
                                   @namespace?.Value,
                                   identity.Value,
                                   version?.Value,
                                   [.. filters.Captures.Zip(values.Captures, (x, y) => (x.Value, y.Value))]);
                    }

                    return new(label.Value, @namespace?.Value, identity.Value, version?.Value, []);
                }
            }

            throw new FormatException();
        }

        #endregion

        [GeneratedRegex("^(?<label>[a-zA-Z0-9._-]+):((?<namespace>[a-zA-Z0-9._-]+)/)?(?<identity>[a-zA-Z0-9._-]+)(@(?<version>[a-zA-Z0-9._-]+))?(#(?<filter>[a-zA-Z0-9._-]+)=(?<value>[a-zA-Z0-9._-]+))*$",
                        RegexOptions.Compiled | RegexOptions.Singleline)]
        private static partial Regex GenerateRegex();
    }
}

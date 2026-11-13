using System.Text.Json;
using System.Text.Json.Nodes;

namespace TridentCore.Core.Utilities;

public static class JsonMigrationHelper
{
    public sealed class Format<T>
    {
        private readonly int _version;
        private readonly Action<JsonObject>[] _steps;

        public Format(int version, params Action<JsonObject>[] steps)
        {
            if (version < 0 || steps.Length != version || steps.Any(x => x is null))
            {
                throw new ArgumentException("Each format version must have one migration from its preceding version.", nameof(steps));
            }
            _version = version;
            _steps = [.. steps];
        }

        public T Deserialize(string json, JsonSerializerOptions options) =>
            Deserialize(JsonNode.Parse(json, NodeOptions(options), DocumentOptions(options)) as JsonObject
                        ?? throw new JsonException($"{typeof(T).Name} must be a JSON object."), options);

        public async ValueTask<T> DeserializeAsync(Stream stream, JsonSerializerOptions options, CancellationToken token = default)
        {
            var document = await JsonNode.ParseAsync(stream, NodeOptions(options), DocumentOptions(options), token).ConfigureAwait(false);
            return Deserialize(document as JsonObject
                               ?? throw new JsonException($"{typeof(T).Name} must be a JSON object."), options);
        }

        public T Deserialize(JsonObject document, JsonSerializerOptions options)
        {
            var keys = document.Select(x => x.Key).Where(x => x.Equals("Format", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (keys.Length > 1)
            {
                throw new JsonException($"{typeof(T).Name} has multiple format fields.");
            }
            var key = keys.SingleOrDefault() ?? options.PropertyNamingPolicy?.ConvertName("Format") ?? "Format";
            var version = 0;
            if (keys.Length != 0
                && (document[key] is not JsonValue value || !value.TryGetValue(out version) || version < 0))
            {
                throw new JsonException($"{typeof(T).Name} format must be a non-negative integer.");
            }
            if (version > _version)
            {
                throw new JsonException($"{typeof(T).Name} format {version} is newer than supported format {_version}.");
            }
            while (version < _version)
            {
                _steps[version](document);
                document[key] = ++version;
            }
            return document.Deserialize<T>(options)
                   ?? throw new JsonException($"{typeof(T).Name} must not be null.");
        }

        private static JsonNodeOptions NodeOptions(JsonSerializerOptions options) =>
            new() { PropertyNameCaseInsensitive = options.PropertyNameCaseInsensitive };

        private static JsonDocumentOptions DocumentOptions(JsonSerializerOptions options) =>
            new() { AllowTrailingCommas = options.AllowTrailingCommas, CommentHandling = options.ReadCommentHandling, MaxDepth = options.MaxDepth };
    }
}

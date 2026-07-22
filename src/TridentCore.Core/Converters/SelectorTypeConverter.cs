using System.Text.Json;
using System.Text.Json.Serialization;
using SelectorType = TridentCore.Abstractions.FileModels.Profile.Rice.Rule.RuleSelector.SelectorType;

namespace TridentCore.Core.Converters;

// COMPAT: maps the legacy "purl" string to SelectorType.Pref during JSON read. Drop the
// "purl" branch once on-disk profiles no longer carry it.
internal sealed class SelectorTypeConverter : JsonConverter<SelectorType>
{
    public override SelectorType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            // COMPAT: legacy "purl" alias; remove this branch once profiles have migrated
            if (value is "purl" or "pref")
            {
                return SelectorType.Pref;
            }

            return Enum.Parse<SelectorType>(value!, true);
        }

        if (reader.TokenType == JsonTokenType.Number)
        {
            return (SelectorType)reader.GetInt32();
        }

        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, SelectorType value, JsonSerializerOptions options) =>
        writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));
}

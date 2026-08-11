using System.Text.Json;
using System.Text.Json.Serialization;
using SelectorType = TridentCore.Abstractions.FileModels.Profile.Rice.Rule.RuleSelector.SelectorType;

namespace TridentCore.Core.Converters;

// TODO: JSON 读取时把遗留 "purl" 串映射为 SelectorType.Pref；磁盘 profile 不再携带后删该分支。
internal sealed class SelectorTypeConverter : JsonConverter<SelectorType>
{
    public override SelectorType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            // TODO: 遗留 "purl" 别名——profile 迁移完成后移除。
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

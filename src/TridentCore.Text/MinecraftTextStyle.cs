namespace TridentCore.Text;

public sealed record MinecraftTextStyle
{
    public MinecraftColor? Color { get; init; }

    public bool? Bold { get; init; }

    public bool? Italic { get; init; }

    public bool? Underlined { get; init; }

    public bool? Strikethrough { get; init; }

    public bool? Obfuscated { get; init; }

    public static MinecraftTextStyle Default { get; } = new();

    // NOTE: `null` on the child means "inherit the parent"; an explicit value
    // (including `false`) overrides. This is what lets `"italic": false` cancel
    // an inherited italic on things like custom item names.
    public MinecraftTextStyle Merge(MinecraftTextStyle? overrider) =>
        overrider is null
            ? this
            : new()
            {
                Color = overrider.Color ?? Color,
                Bold = overrider.Bold ?? Bold,
                Italic = overrider.Italic ?? Italic,
                Underlined = overrider.Underlined ?? Underlined,
                Strikethrough = overrider.Strikethrough ?? Strikethrough,
                Obfuscated = overrider.Obfuscated ?? Obfuscated
            };

    // Resolve inherited nulls to concrete render values: bools default to false;
    // color stays null so the renderer can fall back to its own foreground.
    public MinecraftTextStyle Resolve() =>
        new()
        {
            Color = Color,
            Bold = Bold ?? false,
            Italic = Italic ?? false,
            Underlined = Underlined ?? false,
            Strikethrough = Strikethrough ?? false,
            Obfuscated = Obfuscated ?? false
        };
}

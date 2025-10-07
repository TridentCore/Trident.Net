namespace Trident.Abstractions.Exporters;

public class ExporterNotFoundException(string label, string? message = null)
    : Exception(message ?? $"The exporter label {label} has no match")
{
    public string Label => label;
}

namespace Trident.Core.Engines.Launching;

public record Scrap
{
    public Scrap(
        string message,
        ScrapLevel? level,
        string? date,
        string? time,
        string? thread,
        string? sender
    )
    {
        Level = level;
        Date = date;
        Time = time;
        Thread = thread;
        Sender = sender;
        Message = message;
    }

    public Scrap(string message) => Message = message;

    public string Message { get; init; }
    public ScrapLevel? Level { get; init; }
    public string? Date { get; init; }
    public string? Time { get; init; }
    public string? Thread { get; init; }
    public string? Sender { get; init; }
}

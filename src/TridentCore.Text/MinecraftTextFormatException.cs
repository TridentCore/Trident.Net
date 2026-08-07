namespace TridentCore.Text;

public class MinecraftTextFormatException : FormatException
{
    public MinecraftTextFormatException(string message)
        : base(message)
    {
    }

    public MinecraftTextFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

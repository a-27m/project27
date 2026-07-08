namespace Project27.Cli;

/// <summary>A user-facing CLI error: printed to stderr as `error: …`, exit code 1.</summary>
public sealed class CliException : Exception
{
    public CliException()
    {
    }

    public CliException(string message)
        : base(message)
    {
    }

    public CliException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

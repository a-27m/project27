namespace Project27.Mcp.Session;

/// <summary>A user-facing session failure (bad reference, server error, version conflict, ...).</summary>
public sealed class ProjectSessionException : Exception
{
    public ProjectSessionException(string message)
        : base(message)
    {
    }

    public ProjectSessionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

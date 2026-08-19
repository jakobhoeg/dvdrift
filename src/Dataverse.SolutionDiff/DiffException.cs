namespace Dataverse.SolutionDiff;

/// <summary>
/// A user-facing error (bad input, unreadable config, ...). The CLI prints the
/// message and exits with code 3.
/// </summary>
public sealed class DiffException : Exception
{
    public DiffException(string message)
        : base(message)
    {
    }

    public DiffException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

namespace PcTest.Runner.Process;

/// <summary>
/// Describes the parameters needed to launch an external process.
/// </summary>
public record ProcessStartRequest
{
    /// <summary>
    /// Gets or sets the executable path.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Gets or sets the arguments to supply as discrete entries.
    /// </summary>
    public required IReadOnlyList<string> Arguments { get; init; }

    /// <summary>
    /// Gets or sets the working directory for the process.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Gets or sets the timeout for the process execution.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets a value indicating whether stdout and stderr should be captured in-memory.
    /// </summary>
    public bool CaptureOutput { get; init; }

    /// <summary>
    /// Gets or sets an optional target stream for stdout mirroring.
    /// </summary>
    public Stream? Stdout { get; init; }

    /// <summary>
    /// Gets or sets an optional target stream for stderr mirroring.
    /// </summary>
    public Stream? Stderr { get; init; }

    /// <summary>
    /// Gets or sets environment variables to overlay for the process.
    /// </summary>
    public IDictionary<string, string>? Environment { get; init; }
}

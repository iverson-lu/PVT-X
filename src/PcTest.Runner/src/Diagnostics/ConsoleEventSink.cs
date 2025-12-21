using PcTest.Runner.Diagnostics.Model;

namespace PcTest.Runner.Diagnostics;

/// <summary>
/// Event sink that writes events to the console only.
/// </summary>
public sealed class ConsoleEventSink : IEventSink
{
    /// <inheritdoc />
    public void Write(EventEntry entry)
    {
        var prefix = entry.Level switch
        {
            EventLevel.Error => "[ERROR]",
            EventLevel.Warning => "[WARN]",
            _ => "[INFO]"
        };

        Console.Error.WriteLine($"{prefix} {entry.Time:O} {entry.Type}: {entry.Message}");
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}

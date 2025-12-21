using System.Text.Json;
using PcTest.Contracts.Serialization;
using PcTest.Runner.Diagnostics.Model;

namespace PcTest.Runner.Diagnostics;

/// <summary>
/// Event sink that writes events to a jsonl file and optionally mirrors to the console.
/// </summary>
public sealed class EventLogSink : IEventSink
{
    private readonly StreamWriter _writer;
    private readonly object _gate = new();
    private readonly bool _echoToConsole;

    /// <summary>
    /// Initializes a new instance of the <see cref="EventLogSink"/> class.
    /// </summary>
    /// <param name="path">Destination file for events.</param>
    /// <param name="echoToConsole">When true, also writes a console-friendly line.</param>
    public EventLogSink(string path, bool echoToConsole = false)
    {
        var stream = File.Open(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream) { AutoFlush = true };
        _echoToConsole = echoToConsole;
    }

    /// <inheritdoc />
    public void Write(EventEntry entry)
    {
        var json = JsonSerializer.Serialize(entry, JsonDefaults.Options);
        lock (_gate)
        {
            _writer.WriteLine(json);
            if (_echoToConsole)
            {
                WriteConsole(entry);
            }
        }
    }

    private static void WriteConsole(EventEntry entry)
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
        _writer.Dispose();
    }
}

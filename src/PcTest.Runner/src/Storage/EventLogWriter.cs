using System.Text.Json;
using PcTest.Contracts.Serialization;

namespace PcTest.Runner.Storage;

/// <summary>
/// Writes structured event entries to a log file.
/// </summary>
public sealed class EventLogWriter : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new event log writer targeting the specified path.
    /// </summary>
    /// <param name="path">Destination file for event logs.</param>
    public EventLogWriter(string path)
    {
        var stream = File.Open(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream) { AutoFlush = true };
    }

    /// <summary>
    /// Writes an event entry with optional payload data.
    /// </summary>
    /// <param name="type">Event type identifier.</param>
    /// <param name="message">Human readable description of the event.</param>
    /// <param name="data">Optional structured data payload.</param>
    public void WriteEvent(string type, string message, object? data = null)
    {
        var payload = new
        {
            time = DateTimeOffset.UtcNow,
            type,
            message,
            data
        };

        var json = JsonSerializer.Serialize(payload, JsonDefaults.Options);
        lock (_lock)
        {
            _writer.WriteLine(json);
        }
    }

    /// <summary>
    /// Disposes the underlying writer and releases resources.
    /// </summary>
    public void Dispose()
    {
        _writer.Dispose();
    }
}

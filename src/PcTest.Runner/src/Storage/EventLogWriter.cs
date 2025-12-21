using System.Text.Json;
using PcTest.Contracts.Serialization;

namespace PcTest.Runner.Storage;

public sealed class EventLogWriter : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _lock = new();

    public EventLogWriter(string path)
    {
        var stream = File.Open(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream) { AutoFlush = true };
    }

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

    public void Dispose()
    {
        _writer.Dispose();
    }
}

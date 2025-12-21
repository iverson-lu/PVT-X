using PcTest.Runner.Diagnostics.Model;

namespace PcTest.Runner.Diagnostics;

/// <summary>
/// Contract for emitting structured diagnostic events.
/// </summary>
public interface IEventSink : IDisposable
{
    /// <summary>
    /// Writes the provided event to the sink.
    /// </summary>
    /// <param name="entry">Structured event entry.</param>
    void Write(EventEntry entry);

    /// <summary>
    /// Emits an informational event entry.
    /// </summary>
    /// <param name="type">Short identifier for the event.</param>
    /// <param name="message">Human readable description.</param>
    /// <param name="data">Optional structured payload.</param>
    void Info(string type, string message, object? data = null) =>
        Write(new EventEntry(DateTimeOffset.UtcNow, EventLevel.Information, type, message, data));

    /// <summary>
    /// Emits a warning event entry.
    /// </summary>
    /// <param name="type">Short identifier for the event.</param>
    /// <param name="message">Human readable description.</param>
    /// <param name="data">Optional structured payload.</param>
    void Warn(string type, string message, object? data = null) =>
        Write(new EventEntry(DateTimeOffset.UtcNow, EventLevel.Warning, type, message, data));

    /// <summary>
    /// Emits an error event entry.
    /// </summary>
    /// <param name="type">Short identifier for the event.</param>
    /// <param name="message">Human readable description.</param>
    /// <param name="data">Optional structured payload.</param>
    void Error(string type, string message, object? data = null) =>
        Write(new EventEntry(DateTimeOffset.UtcNow, EventLevel.Error, type, message, data));
}

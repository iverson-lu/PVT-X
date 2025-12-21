namespace PcTest.Runner.Diagnostics.Model;

/// <summary>
/// Represents a structured diagnostic event entry.
/// </summary>
/// <param name="Time">Timestamp of the event.</param>
/// <param name="Level">Severity level of the event.</param>
/// <param name="Type">Short event type identifier.</param>
/// <param name="Message">Human readable description.</param>
/// <param name="Data">Optional structured payload.</param>
public record EventEntry(DateTimeOffset Time, EventLevel Level, string Type, string Message, object? Data);

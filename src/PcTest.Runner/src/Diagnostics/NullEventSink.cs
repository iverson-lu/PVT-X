using PcTest.Runner.Diagnostics.Model;

namespace PcTest.Runner.Diagnostics;

/// <summary>
/// No-op event sink for scenarios where diagnostics are optional.
/// </summary>
public sealed class NullEventSink : IEventSink
{
    /// <inheritdoc />
    public void Write(EventEntry entry)
    {
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}

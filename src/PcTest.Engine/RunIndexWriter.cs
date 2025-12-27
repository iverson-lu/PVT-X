using System.Text.Json;
using PcTest.Contracts;

namespace PcTest.Engine;

public sealed class RunIndexWriter
{
    private readonly string _indexPath;
    private readonly object _lock = new();

    public RunIndexWriter(string runsRoot)
    {
        Directory.CreateDirectory(runsRoot);
        _indexPath = Path.Combine(runsRoot, "index.jsonl");
    }

    public void Append(RunIndexEntry entry)
    {
        var json = JsonSerializer.Serialize(entry, JsonDefaults.Options);
        lock (_lock)
        {
            using var stream = new FileStream(_indexPath, FileMode.Append, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream);
            writer.WriteLine(json);
        }
    }
}

using System.Text.Json;

namespace PcTest.Engine;

public sealed class IndexWriter
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task AppendAsync(string indexPath, object entry)
    {
        var json = JsonSerializer.Serialize(entry, PcTest.Contracts.JsonUtilities.SerializerOptions);
        await Gate.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(indexPath, json + Environment.NewLine);
        }
        finally
        {
            Gate.Release();
        }
    }
}

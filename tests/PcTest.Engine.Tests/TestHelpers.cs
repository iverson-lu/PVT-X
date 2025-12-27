namespace PcTest.Engine.Tests;

public sealed class TempFolder : IDisposable
{
    public string Path { get; }

    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"pctest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string CreateSubfolder(string name)
    {
        var folder = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, true);
        }
        catch
        {
        }
    }
}

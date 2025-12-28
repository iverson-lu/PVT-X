namespace PcTest.Engine;

public static class RunIdFactory
{
    public static string CreateGroupRunId(string runsRoot) => CreateUniqueId(runsRoot, "G");

    private static string CreateUniqueId(string runsRoot, string prefix)
    {
        while (true)
        {
            string id = $"{prefix}-{Guid.NewGuid():N}";
            string path = Path.Combine(runsRoot, id);
            if (!Directory.Exists(path))
            {
                return id;
            }
        }
    }
}

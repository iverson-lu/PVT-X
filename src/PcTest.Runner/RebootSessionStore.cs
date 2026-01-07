using System.Text;
using PcTest.Contracts;

namespace PcTest.Runner;

public static class RebootSessionStore
{
    public static string GetSessionPath(string caseRunFolder)
    {
        return Path.Combine(caseRunFolder, "artifacts", "session.json");
    }

    public static async Task SaveAsync(string caseRunFolder, RebootSession session)
    {
        var artifactsDir = Path.Combine(caseRunFolder, "artifacts");
        Directory.CreateDirectory(artifactsDir);
        var path = GetSessionPath(caseRunFolder);
        var json = JsonDefaults.Serialize(session);
        await File.WriteAllTextAsync(path, json, Encoding.UTF8);
    }

    public static RebootSession Load(string caseRunFolder)
    {
        var path = GetSessionPath(caseRunFolder);
        var json = File.ReadAllText(path, Encoding.UTF8);
        var session = JsonDefaults.Deserialize<RebootSession>(json);
        if (session is null)
        {
            throw new InvalidOperationException($"Failed to deserialize session.json at {path}");
        }

        return session;
    }
}

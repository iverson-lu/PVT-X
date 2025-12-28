using PcTest.Contracts;

namespace PcTest.Runner;

public static class RunnerEventWriter
{
    public static void AppendSecretWarnings(string runFolder, IEnumerable<string> secretInputs, string? nodeId)
    {
        foreach (var secretInput in secretInputs)
        {
            var payload = new
            {
                timestamp = DateTimeOffset.UtcNow.ToString("O"),
                code = "EnvRef.SecretOnCommandLine",
                message = $"Secret input {secretInput} was passed via command line.",
                nodeId
            };
            JsonUtil.AppendJsonLine(Path.Combine(runFolder, "events.jsonl"), payload);
        }
    }
}

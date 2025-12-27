using System.Diagnostics;
using PcTest.Runner;
using Xunit;

namespace PcTest.Runner.Tests;

public class RunnerUtilitiesTests
{
    [Fact]
    public void AppendArguments_FormatsPowerShellParameters()
    {
        var startInfo = new ProcessStartInfo();
        var inputs = new Dictionary<string, object?>
        {
            ["Duration"] = 5,
            ["Enabled"] = true,
            ["Modes"] = new[] { "A", "B" }
        };

        RunnerUtilities.AppendArguments(startInfo, inputs);

        Assert.Equal(new[] { "-Duration", "5", "-Enabled", "$true", "-Modes", "A", "B" }, startInfo.ArgumentList.ToArray());
    }

    [Theory]
    [InlineData(0, "Passed", null)]
    [InlineData(1, "Failed", null)]
    [InlineData(2, "Error", "ScriptError")]
    public void MapExitCode_UsesSpecMapping(int exitCode, string status, string? errorType)
    {
        var result = RunnerUtilities.MapExitCode(exitCode);
        Assert.Equal(status, result.status);
        Assert.Equal(errorType, result.errorType);
    }

    [Fact]
    public void ValidateWorkingDir_RejectsEscapes()
    {
        var runFolder = Path.Combine(Path.GetTempPath(), "run");
        var error = RunnerUtilities.ValidateWorkingDir(runFolder, "..\\outside");
        Assert.NotNull(error);
    }

    [Fact]
    public void Redaction_ReplacesSecretValues()
    {
        var inputs = new Dictionary<string, object?> { ["Secret"] = "topsecret", ["Normal"] = "ok" };
        var secrets = new HashSet<string> { "Secret" };
        var redacted = RunnerUtilities.RedactInputs(inputs, secrets);
        Assert.Equal("***", redacted["Secret"]);
        var text = RunnerUtilities.RedactText("Value=topsecret", secrets, inputs);
        Assert.Equal("Value=***", text);
    }
}

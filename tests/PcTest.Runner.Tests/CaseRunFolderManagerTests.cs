using PcTest.Contracts;
using PcTest.Contracts.Manifests;
using PcTest.Contracts.Results;
using PcTest.Runner;
using Xunit;

namespace PcTest.Runner.Tests;

/// <summary>
/// Tests for CaseRunFolderManager per spec section 12.
/// Runner is the exclusive owner of Case Run Folders.
/// </summary>
public class CaseRunFolderManagerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly CaseRunFolderManager _manager;

    public CaseRunFolderManagerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"PcTestRunner_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _manager = new CaseRunFolderManager(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public void CreateRunFolder_CreatesDirectoryWithArtifacts()
    {
        var runId = $"test-run-{Guid.NewGuid():N}";
        var folderPath = _manager.CreateRunFolder(runId);

        Assert.True(Directory.Exists(folderPath));
        Assert.True(Directory.Exists(Path.Combine(folderPath, "artifacts")));
    }

    [Fact]
    public void CreateRunFolder_HandlesCollision()
    {
        var runId = "collision-test";
        var firstPath = _manager.CreateRunFolder(runId);
        var secondPath = _manager.CreateRunFolder(runId);

        Assert.NotEqual(firstPath, secondPath);
        Assert.True(Directory.Exists(firstPath));
        Assert.True(Directory.Exists(secondPath));
    }

    [Fact]
    public void PrepareWorkingDir_DefaultsToRunFolder()
    {
        var runId = $"workdir-test-{Guid.NewGuid():N}";
        var runFolder = _manager.CreateRunFolder(runId);

        var (success, resolvedPath, error) = _manager.PrepareWorkingDir(runFolder, null);

        Assert.True(success);
        Assert.Equal(runFolder, resolvedPath);
        Assert.Null(error);
    }

    [Fact]
    public void PrepareWorkingDir_RelativePathContained()
    {
        var runId = $"workdir-relative-{Guid.NewGuid():N}";
        var runFolder = _manager.CreateRunFolder(runId);

        var (success, resolvedPath, error) = _manager.PrepareWorkingDir(runFolder, "subdir");

        Assert.True(success);
        Assert.NotNull(resolvedPath);
        Assert.StartsWith(runFolder, resolvedPath);
        Assert.True(Directory.Exists(resolvedPath));
    }

    [Fact]
    public void PrepareWorkingDir_OutOfRoot_Fails()
    {
        var runId = $"workdir-escape-{Guid.NewGuid():N}";
        var runFolder = _manager.CreateRunFolder(runId);

        var (success, resolvedPath, error) = _manager.PrepareWorkingDir(runFolder, "..\\..\\escape");

        Assert.False(success);
        Assert.Null(resolvedPath);
        Assert.Contains("OutOfRoot", error);
    }

    [Fact]
    public async Task WriteManifestSnapshot_SecretRedaction()
    {
        var runId = $"manifest-test-{Guid.NewGuid():N}";
        var runFolder = _manager.CreateRunFolder(runId);

        var snapshot = new CaseManifestSnapshot
        {
            SourceManifest = new TestCaseManifest
            {
                Id = "TestCase",
                Version = "1.0.0",
                Name = "Test",
                Category = "Test"
            },
            ResolvedIdentity = new IdentityInfo
            {
                Id = "TestCase",
                Version = "1.0.0"
            },
            EffectiveInputs = new Dictionary<string, object?>
            {
                ["Password"] = "super-secret",
                ["Duration"] = 10
            }
        };
        var secrets = new Dictionary<string, bool> { ["Password"] = true };

        await _manager.WriteManifestSnapshotAsync(runFolder, snapshot, secrets);

        var manifestPath = Path.Combine(runFolder, "manifest.json");
        Assert.True(File.Exists(manifestPath));
        var content = await File.ReadAllTextAsync(manifestPath);
        Assert.DoesNotContain("super-secret", content);
        Assert.Contains("***", content);
    }

    [Fact]
    public async Task WriteParams_SecretRedaction()
    {
        var runId = $"params-test-{Guid.NewGuid():N}";
        var runFolder = _manager.CreateRunFolder(runId);

        var inputs = new Dictionary<string, object?>
        {
            ["Password"] = "secret-password-123",
            ["Duration"] = 10
        };
        var secrets = new Dictionary<string, bool> { ["Password"] = true };

        await _manager.WriteParamsAsync(runFolder, inputs, secrets);

        var paramsPath = Path.Combine(runFolder, "params.json");
        Assert.True(File.Exists(paramsPath));
        var content = await File.ReadAllTextAsync(paramsPath);
        Assert.DoesNotContain("secret-password-123", content);
        Assert.Contains("***", content);
    }

    [Fact]
    public async Task WriteEnvSnapshot_UsesSnapshotClass()
    {
        var runId = $"env-test-{Guid.NewGuid():N}";
        var runFolder = _manager.CreateRunFolder(runId);

        var snapshot = new EnvironmentSnapshot
        {
            OsVersion = "Windows 11",
            RunnerVersion = "1.0.0",
            PwshVersion = "7.4.0",
            IsElevated = false
        };

        await _manager.WriteEnvSnapshotAsync(runFolder, snapshot);

        var envPath = Path.Combine(runFolder, "env.json");
        Assert.True(File.Exists(envPath));
        var content = await File.ReadAllTextAsync(envPath);
        Assert.Contains("Windows 11", content);
        Assert.Contains("7.4.0", content);
    }

    [Fact]
    public async Task WriteStdout_RedactsSecrets()
    {
        var runId = $"stdout-test-{Guid.NewGuid():N}";
        var runFolder = _manager.CreateRunFolder(runId);
        var secretValues = new[] { "my-secret-token" };

        await _manager.WriteStdoutAsync(
            runFolder,
            "Output contains my-secret-token value",
            secretValues);

        var stdoutPath = Path.Combine(runFolder, "stdout.log");
        Assert.True(File.Exists(stdoutPath));
        var content = await File.ReadAllTextAsync(stdoutPath);
        Assert.DoesNotContain("my-secret-token", content);
        Assert.Contains("***", content);
    }

    [Fact]
    public async Task WriteStderr_RedactsSecrets()
    {
        var runId = $"stderr-test-{Guid.NewGuid():N}";
        var runFolder = _manager.CreateRunFolder(runId);
        var secretValues = new[] { "another-secret" };

        await _manager.WriteStderrAsync(
            runFolder,
            "Error: another-secret leaked",
            secretValues);

        var stderrPath = Path.Combine(runFolder, "stderr.log");
        Assert.True(File.Exists(stderrPath));
        var content = await File.ReadAllTextAsync(stderrPath);
        Assert.DoesNotContain("another-secret", content);
        Assert.Contains("***", content);
    }

    [Fact]
    public async Task AppendStdoutLine_WritesIncrementally()
    {
        var runId = $"append-stdout-{Guid.NewGuid():N}";
        var runFolder = _manager.CreateRunFolder(runId);
        var stdoutPath = Path.Combine(runFolder, "stdout.log");

        // Write multiple lines incrementally
        await _manager.AppendStdoutLineAsync(runFolder, "Line 1", null);
        await _manager.AppendStdoutLineAsync(runFolder, "Line 2", null);
        await _manager.AppendStdoutLineAsync(runFolder, "Line 3", null);

        // Flush before reading
        _manager.FlushAndCloseWriters(runFolder);

        Assert.True(File.Exists(stdoutPath));
        var content = await File.ReadAllTextAsync(stdoutPath);
        Assert.Contains("Line 1", content);
        Assert.Contains("Line 2", content);
        Assert.Contains("Line 3", content);
    }

    [Fact]
    public async Task AppendStdoutLine_RedactsSecrets()
    {
        var runId = $"append-secret-{Guid.NewGuid():N}";
        var runFolder = _manager.CreateRunFolder(runId);
        var secretValues = new[] { "my-password" };
        var stdoutPath = Path.Combine(runFolder, "stdout.log");

        await _manager.AppendStdoutLineAsync(runFolder, "Password is my-password", secretValues);
        _manager.FlushAndCloseWriters(runFolder);

        var content = await File.ReadAllTextAsync(stdoutPath);
        Assert.DoesNotContain("my-password", content);
        Assert.Contains("***", content);
    }

    [Fact]
    public async Task AppendStderrLine_WritesIncrementally()
    {
        var runId = $"append-stderr-{Guid.NewGuid():N}";
        var runFolder = _manager.CreateRunFolder(runId);
        var stderrPath = Path.Combine(runFolder, "stderr.log");

        await _manager.AppendStderrLineAsync(runFolder, "Error 1", null);
        await _manager.AppendStderrLineAsync(runFolder, "Error 2", null);
        _manager.FlushAndCloseWriters(runFolder);

        Assert.True(File.Exists(stderrPath));
        var content = await File.ReadAllTextAsync(stderrPath);
        Assert.Contains("Error 1", content);
        Assert.Contains("Error 2", content);
    }

    [Fact]
    public async Task AppendStdoutLine_FileShareReadWrite_AllowsSimultaneousRead()
    {
        var runId = $"share-test-{Guid.NewGuid():N}";
        var runFolder = _manager.CreateRunFolder(runId);
        var stdoutPath = Path.Combine(runFolder, "stdout.log");

        // Start writing
        await _manager.AppendStdoutLineAsync(runFolder, "First line", null);

        // Should be able to read while writing (FileShare.ReadWrite)
        using (var readStream = new FileStream(stdoutPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(readStream))
        {
            var content = await reader.ReadToEndAsync();
            Assert.Contains("First line", content);
        }

        // Continue writing
        await _manager.AppendStdoutLineAsync(runFolder, "Second line", null);
        _manager.FlushAndCloseWriters(runFolder);

        var finalContent = await File.ReadAllTextAsync(stdoutPath);
        Assert.Contains("First line", finalContent);
        Assert.Contains("Second line", finalContent);
    }

    [Fact]
    public async Task AppendStdoutLine_NullLine_Ignored()
    {
        var runId = $"null-line-{Guid.NewGuid():N}";
        var runFolder = _manager.CreateRunFolder(runId);
        var stdoutPath = Path.Combine(runFolder, "stdout.log");

        await _manager.AppendStdoutLineAsync(runFolder, null, null);
        await _manager.AppendStdoutLineAsync(runFolder, "Valid line", null);
        _manager.FlushAndCloseWriters(runFolder);

        var content = await File.ReadAllTextAsync(stdoutPath);
        // Should only have the valid line, null should not cause issues
        Assert.Contains("Valid line", content);
    }

    [Fact]
    public void FlushAndCloseWriters_CanBeCalledMultipleTimes()
    {
        var runId = $"flush-multiple-{Guid.NewGuid():N}";
        var runFolder = _manager.CreateRunFolder(runId);

        // Should not throw even when called multiple times or without writers
        _manager.FlushAndCloseWriters(runFolder);
        _manager.FlushAndCloseWriters(runFolder);
    }
}

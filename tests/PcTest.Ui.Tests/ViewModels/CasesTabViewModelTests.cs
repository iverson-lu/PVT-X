using System.IO;
using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using Moq;
using PcTest.Contracts;
using PcTest.Contracts.Manifests;
using PcTest.Engine.Discovery;
using PcTest.Ui.Services;
using PcTest.Ui.ViewModels;
using Xunit;

namespace PcTest.Ui.Tests.ViewModels;

/// <summary>
/// Tests for CasesTabViewModel.
/// </summary>
public class CasesTabViewModelTests : IDisposable
{
    private readonly Mock<IDiscoveryService> _discoveryMock;
    private readonly Mock<IFileSystemService> _fileSystemMock;
    private readonly Mock<IFileDialogService> _fileDialogMock;
    private readonly Mock<INavigationService> _navigationMock;
    private readonly string _testTempDir;

    public CasesTabViewModelTests()
    {
        _discoveryMock = new Mock<IDiscoveryService>();
        _fileSystemMock = new Mock<IFileSystemService>();
        _fileDialogMock = new Mock<IFileDialogService>();
        _navigationMock = new Mock<INavigationService>();
        _testTempDir = Path.Combine(Path.GetTempPath(), $"PcTestUnitTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testTempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testTempDir))
        {
            Directory.Delete(_testTempDir, true);
        }
    }

    private CasesTabViewModel CreateViewModel() =>
        new(_discoveryMock.Object, _fileSystemMock.Object, _fileDialogMock.Object, _navigationMock.Object);

    [Fact]
    public void Constructor_ShouldCreateViewModel()
    {
        // Act
        var vm = CreateViewModel();

        // Assert
        vm.Should().NotBeNull();
        vm.Cases.Should().BeEmpty();
        vm.IsDiscovering.Should().BeFalse();
    }

    [Fact]
    public async Task ImportAsync_ShouldShowError_WhenNoFileSelected()
    {
        // Arrange
        var vm = CreateViewModel();
        _fileDialogMock.Setup(x => x.ShowOpenFileDialog(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string?)null);

        // Act
        await vm.ImportCommand.ExecuteAsync(null);

        // Assert
        _fileDialogMock.Verify(x => x.ShowError(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ImportAsync_ShouldShowError_WhenZipHasNoManifest()
    {
        // Arrange
        var vm = CreateViewModel();
        var zipPath = CreateEmptyZip();
        
        _fileDialogMock.Setup(x => x.ShowOpenFileDialog(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(zipPath);

        // Act
        await vm.ImportCommand.ExecuteAsync(null);

        // Assert
        _fileDialogMock.Verify(x => x.ShowError("Import Failed", "No test.manifest.json was found in the zip file."), Times.Once);
    }

    [Fact]
    public async Task ImportAsync_ShouldShowError_WhenZipHasMultipleManifests()
    {
        // Arrange
        var vm = CreateViewModel();
        var zipPath = CreateZipWithMultipleManifests();
        
        _fileDialogMock.Setup(x => x.ShowOpenFileDialog(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(zipPath);

        // Act
        await vm.ImportCommand.ExecuteAsync(null);

        // Assert
        _fileDialogMock.Verify(x => x.ShowError("Import Failed", "Multiple test.manifest.json files were found in the zip file."), Times.Once);
    }

    [Fact]
    public async Task ImportAsync_ShouldShowError_WhenManifestIsInvalid()
    {
        // Arrange
        var vm = CreateViewModel();
        var zipPath = CreateZipWithInvalidManifest();
        
        _fileDialogMock.Setup(x => x.ShowOpenFileDialog(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(zipPath);

        // Act
        await vm.ImportCommand.ExecuteAsync(null);

        // Assert
        _fileDialogMock.Verify(x => x.ShowError("Import Failed", "The test case manifest is invalid."), Times.Once);
    }

    [Fact]
    public async Task ImportAsync_ShouldShowError_WhenTestCaseRootNotConfigured()
    {
        // Arrange
        var vm = CreateViewModel();
        var manifest = CreateValidManifest("TestCase1", "1.0.0");
        var zipPath = CreateZipWithManifest(manifest);
        
        _fileDialogMock.Setup(x => x.ShowOpenFileDialog(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(zipPath);

        var discovery = new DiscoveryResult { ResolvedTestCaseRoot = string.Empty };
        _discoveryMock.Setup(x => x.CurrentDiscovery).Returns(discovery);

        // Act
        await vm.ImportCommand.ExecuteAsync(null);

        // Assert
        _fileDialogMock.Verify(x => x.ShowError("Import Failed", "Test case root is not configured."), Times.Once);
    }

    [Fact]
    public async Task ImportAsync_ShouldShowError_WhenIdentityConflictExists()
    {
        // Arrange
        var vm = CreateViewModel();
        var manifest = CreateValidManifest("TestCase1", "1.0.0");
        var zipPath = CreateZipWithManifest(manifest);
        
        _fileDialogMock.Setup(x => x.ShowOpenFileDialog(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(zipPath);

        var discovery = new DiscoveryResult 
        { 
            ResolvedTestCaseRoot = Path.Combine(_testTempDir, "TestCases")
        };
        discovery.TestCases.Add("TestCase1@1.0.0", new DiscoveredTestCase
        {
            Manifest = manifest,
            FolderPath = "existing",
            ManifestPath = "existing/test.manifest.json"
        });
        _discoveryMock.Setup(x => x.CurrentDiscovery).Returns(discovery);

        // Act
        await vm.ImportCommand.ExecuteAsync(null);

        // Assert
        _fileDialogMock.Verify(x => x.ShowError("Import Failed", "A test case with identity 'TestCase1@1.0.0' already exists."), Times.Once);
    }

    [Fact]
    public async Task ImportAsync_ShouldShowError_WhenNameConflictExists()
    {
        // Arrange
        var vm = CreateViewModel();
        var manifest = CreateValidManifest("TestCase1", "1.0.0");
        var zipPath = CreateZipWithManifest(manifest);
        
        _fileDialogMock.Setup(x => x.ShowOpenFileDialog(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(zipPath);

        var existingManifest = CreateValidManifest("TestCase1", "2.0.0");
        var discovery = new DiscoveryResult 
        { 
            ResolvedTestCaseRoot = Path.Combine(_testTempDir, "TestCases")
        };
        discovery.TestCases.Add("TestCase1@2.0.0", new DiscoveredTestCase
        {
            Manifest = existingManifest,
            FolderPath = "existing",
            ManifestPath = "existing/test.manifest.json"
        });
        _discoveryMock.Setup(x => x.CurrentDiscovery).Returns(discovery);

        // Act
        await vm.ImportCommand.ExecuteAsync(null);

        // Assert
        _fileDialogMock.Verify(x => x.ShowError("Import Failed", "A test case named 'TestCase1' already exists."), Times.Once);
    }

    [Fact]
    public async Task ImportAsync_ShouldShowError_WhenDestinationFolderExists()
    {
        // Arrange
        var vm = CreateViewModel();
        var manifest = CreateValidManifest("TestCase1", "1.0.0");
        var zipPath = CreateZipWithManifest(manifest);
        
        _fileDialogMock.Setup(x => x.ShowOpenFileDialog(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(zipPath);

        var testCaseRoot = Path.Combine(_testTempDir, "TestCases");
        Directory.CreateDirectory(testCaseRoot);
        var destinationFolder = Path.Combine(testCaseRoot, "TestCase1");
        Directory.CreateDirectory(destinationFolder);

        var discovery = new DiscoveryResult 
        { 
            ResolvedTestCaseRoot = testCaseRoot
        };
        _discoveryMock.Setup(x => x.CurrentDiscovery).Returns(discovery);

        // Act
        await vm.ImportCommand.ExecuteAsync(null);

        // Assert
        _fileDialogMock.Verify(x => x.ShowError("Import Failed", 
            It.Is<string>(msg => msg.Contains($"A folder already exists at"))), Times.Once);
    }

    [Fact]
    public async Task ImportAsync_ShouldSucceed_WhenValidZipAndNoConflicts()
    {
        // Arrange
        var vm = CreateViewModel();
        var manifest = CreateValidManifest("TestCase1", "1.0.0");
        var zipPath = CreateZipWithManifest(manifest);
        
        _fileDialogMock.Setup(x => x.ShowOpenFileDialog(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(zipPath);

        var testCaseRoot = Path.Combine(_testTempDir, "TestCases");
        var discovery = new DiscoveryResult 
        { 
            ResolvedTestCaseRoot = testCaseRoot
        };
        _discoveryMock.Setup(x => x.CurrentDiscovery).Returns(discovery);
        _discoveryMock.Setup(x => x.DiscoverAsync()).ReturnsAsync(discovery);

        // Act
        await vm.ImportCommand.ExecuteAsync(null);

        // Assert
        _fileDialogMock.Verify(x => x.ShowInfo("Import Successful", "Test case 'TestCase1' imported successfully."), Times.Once);
        _discoveryMock.Verify(x => x.DiscoverAsync(), Times.Once);
        Directory.Exists(Path.Combine(testCaseRoot, "TestCase1")).Should().BeTrue();
    }

    [Fact]
    public async Task ImportAsync_ShouldCopyAllFiles_WhenImportingZip()
    {
        // Arrange
        var vm = CreateViewModel();
        var manifest = CreateValidManifest("TestCase1", "1.0.0");
        var zipPath = CreateZipWithManifestAndFiles(manifest, new[] { "script.ps1", "data.txt" });
        
        _fileDialogMock.Setup(x => x.ShowOpenFileDialog(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(zipPath);

        var testCaseRoot = Path.Combine(_testTempDir, "TestCases");
        var discovery = new DiscoveryResult 
        { 
            ResolvedTestCaseRoot = testCaseRoot
        };
        _discoveryMock.Setup(x => x.CurrentDiscovery).Returns(discovery);
        _discoveryMock.Setup(x => x.DiscoverAsync()).ReturnsAsync(discovery);

        // Act
        await vm.ImportCommand.ExecuteAsync(null);

        // Assert
        var destinationFolder = Path.Combine(testCaseRoot, "TestCase1");
        File.Exists(Path.Combine(destinationFolder, "test.manifest.json")).Should().BeTrue();
        File.Exists(Path.Combine(destinationFolder, "script.ps1")).Should().BeTrue();
        File.Exists(Path.Combine(destinationFolder, "data.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task ImportAsync_ShouldHandleException_WhenImportFails()
    {
        // Arrange
        var vm = CreateViewModel();
        _fileDialogMock.Setup(x => x.ShowOpenFileDialog(It.IsAny<string>(), It.IsAny<string>()))
            .Returns("nonexistent.zip");

        // Act
        await vm.ImportCommand.ExecuteAsync(null);

        // Assert
        _fileDialogMock.Verify(x => x.ShowError("Import Failed", It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ImportAsync_ShouldUseDiscoverAsync_WhenCurrentDiscoveryIsNull()
    {
        // Arrange
        var vm = CreateViewModel();
        var manifest = CreateValidManifest("TestCase1", "1.0.0");
        var zipPath = CreateZipWithManifest(manifest);
        
        _fileDialogMock.Setup(x => x.ShowOpenFileDialog(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(zipPath);

        var testCaseRoot = Path.Combine(_testTempDir, "TestCases");
        var discovery = new DiscoveryResult 
        { 
            ResolvedTestCaseRoot = testCaseRoot
        };
        _discoveryMock.Setup(x => x.CurrentDiscovery).Returns((DiscoveryResult?)null);
        _discoveryMock.Setup(x => x.DiscoverAsync()).ReturnsAsync(discovery);

        // Act
        await vm.ImportCommand.ExecuteAsync(null);

        // Assert
        _discoveryMock.Verify(x => x.DiscoverAsync(), Times.AtLeast(2)); // Once for getting discovery, once after import
        _fileDialogMock.Verify(x => x.ShowInfo("Import Successful", "Test case 'TestCase1' imported successfully."), Times.Once);
    }

    // Helper methods
    private string CreateEmptyZip()
    {
        var zipPath = Path.Combine(_testTempDir, $"empty_{Guid.NewGuid():N}.zip");
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        return zipPath;
    }

    private string CreateZipWithMultipleManifests()
    {
        var zipPath = Path.Combine(_testTempDir, $"multiple_{Guid.NewGuid():N}.zip");
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        archive.CreateEntry("TestCase1/test.manifest.json");
        archive.CreateEntry("TestCase2/test.manifest.json");
        return zipPath;
    }

    private string CreateZipWithInvalidManifest()
    {
        var zipPath = Path.Combine(_testTempDir, $"invalid_{Guid.NewGuid():N}.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("test.manifest.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("{\"invalid\": \"manifest\"}");
        }
        return zipPath;
    }

    private string CreateZipWithManifest(TestCaseManifest manifest)
    {
        return CreateZipWithManifestAndFiles(manifest, Array.Empty<string>());
    }

    private string CreateZipWithManifestAndFiles(TestCaseManifest manifest, string[] additionalFiles)
    {
        var zipPath = Path.Combine(_testTempDir, $"valid_{Guid.NewGuid():N}.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("test.manifest.json");
            using (var writer = new StreamWriter(entry.Open()))
            {
                var json = JsonDefaults.Serialize(manifest);
                writer.Write(json);
            }

            foreach (var file in additionalFiles)
            {
                var fileEntry = archive.CreateEntry(file);
                using var fileWriter = new StreamWriter(fileEntry.Open());
                fileWriter.Write($"// Content of {file}");
            }
        }
        return zipPath;
    }

    private TestCaseManifest CreateValidManifest(string id, string version)
    {
        return new TestCaseManifest
        {
            SchemaVersion = "1.5.0",
            Id = id,
            Name = id,
            Version = version,
            Category = "Test",
            Privilege = Privilege.User,
            TimeoutSec = 300,
            Description = $"Test case {id}",
            Tags = new List<string> { "test" },
            Parameters = new List<ParameterDefinition>()
        };
    }
}

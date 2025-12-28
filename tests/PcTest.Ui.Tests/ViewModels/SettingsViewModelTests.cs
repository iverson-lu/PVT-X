using FluentAssertions;
using Moq;
using PcTest.Ui.Services;
using PcTest.Ui.ViewModels;
using Xunit;

namespace PcTest.Ui.Tests.ViewModels;

/// <summary>
/// Tests for SettingsViewModel.
/// </summary>
public class SettingsViewModelTests
{
    private readonly Mock<ISettingsService> _settingsServiceMock;
    private readonly Mock<IFileDialogService> _fileDialogMock;
    private readonly Mock<IDiscoveryService> _discoveryMock;

    public SettingsViewModelTests()
    {
        _settingsServiceMock = new Mock<ISettingsService>();
        _fileDialogMock = new Mock<IFileDialogService>();
        _discoveryMock = new Mock<IDiscoveryService>();
    }

    [Fact]
    public void Load_ShouldPopulateSettingsFromService()
    {
        // Arrange
        var settings = new AppSettings
        {
            WorkspaceRoot = @"C:\TestWorkspace",
            TestCasesRoot = "TestCases",
            Theme = "Light"
        };
        _settingsServiceMock.Setup(x => x.CurrentSettings).Returns(settings);

        var vm = new SettingsViewModel(
            _settingsServiceMock.Object,
            _fileDialogMock.Object,
            _discoveryMock.Object);

        // Act
        vm.Load();

        // Assert
        vm.WorkspaceRoot.Should().Be(@"C:\TestWorkspace");
        vm.TestCasesRoot.Should().Be("TestCases");
        vm.Theme.Should().Be("Light");
    }

    [Fact]
    public void HasChanges_ShouldBeFalse_AfterLoad()
    {
        // Arrange
        var settings = new AppSettings();
        _settingsServiceMock.Setup(x => x.CurrentSettings).Returns(settings);

        var vm = new SettingsViewModel(
            _settingsServiceMock.Object,
            _fileDialogMock.Object,
            _discoveryMock.Object);

        // Act
        vm.Load();

        // Assert
        vm.HasChanges.Should().BeFalse();
    }

    [Fact]
    public void HasChanges_ShouldBeTrue_AfterPropertyChange()
    {
        // Arrange
        var settings = new AppSettings { WorkspaceRoot = @"C:\Original" };
        _settingsServiceMock.Setup(x => x.CurrentSettings).Returns(settings);

        var vm = new SettingsViewModel(
            _settingsServiceMock.Object,
            _fileDialogMock.Object,
            _discoveryMock.Object);
        vm.Load();

        // Act
        vm.WorkspaceRoot = @"C:\Changed";

        // Assert
        vm.HasChanges.Should().BeTrue();
    }

    [Fact]
    public void Theme_ShouldDefaultToDark()
    {
        // Arrange
        var settings = new AppSettings { Theme = "Dark" };
        _settingsServiceMock.Setup(x => x.CurrentSettings).Returns(settings);

        var vm = new SettingsViewModel(
            _settingsServiceMock.Object,
            _fileDialogMock.Object,
            _discoveryMock.Object);

        // Act
        vm.Load();

        // Assert
        vm.Theme.Should().Be("Dark");
    }

    [Fact]
    public void DiscardCommand_ShouldReloadSettings()
    {
        // Arrange
        var settings = new AppSettings { WorkspaceRoot = @"C:\Original" };
        _settingsServiceMock.Setup(x => x.CurrentSettings).Returns(settings);

        var vm = new SettingsViewModel(
            _settingsServiceMock.Object,
            _fileDialogMock.Object,
            _discoveryMock.Object);
        vm.Load();
        vm.WorkspaceRoot = @"C:\Changed";

        // Act
        vm.DiscardCommand.Execute(null);

        // Assert
        vm.WorkspaceRoot.Should().Be(@"C:\Original");
        vm.HasChanges.Should().BeFalse();
    }

    [Fact]
    public void BrowseWorkspaceRootCommand_ShouldSetPathFromDialog()
    {
        // Arrange
        var settings = new AppSettings();
        _settingsServiceMock.Setup(x => x.CurrentSettings).Returns(settings);
        _fileDialogMock.Setup(x => x.ShowFolderBrowserDialog(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(@"C:\SelectedFolder");

        var vm = new SettingsViewModel(
            _settingsServiceMock.Object,
            _fileDialogMock.Object,
            _discoveryMock.Object);
        vm.Load();

        // Act
        vm.BrowseWorkspaceRootCommand.Execute(null);

        // Assert
        vm.WorkspaceRoot.Should().Be(@"C:\SelectedFolder");
    }

    [Fact]
    public void BrowseWorkspaceRootCommand_ShouldNotChangeValue_WhenDialogCancelled()
    {
        // Arrange
        var settings = new AppSettings { WorkspaceRoot = @"C:\Original" };
        _settingsServiceMock.Setup(x => x.CurrentSettings).Returns(settings);
        _fileDialogMock.Setup(x => x.ShowFolderBrowserDialog(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns((string?)null);

        var vm = new SettingsViewModel(
            _settingsServiceMock.Object,
            _fileDialogMock.Object,
            _discoveryMock.Object);
        vm.Load();

        // Act
        vm.BrowseWorkspaceRootCommand.Execute(null);

        // Assert
        vm.WorkspaceRoot.Should().Be(@"C:\Original");
    }

    [Fact]
    public void DefaultTimeoutSec_ShouldTrackChanges()
    {
        // Arrange
        var settings = new AppSettings { DefaultTimeoutSec = 300 };
        _settingsServiceMock.Setup(x => x.CurrentSettings).Returns(settings);

        var vm = new SettingsViewModel(
            _settingsServiceMock.Object,
            _fileDialogMock.Object,
            _discoveryMock.Object);
        vm.Load();

        // Act
        vm.DefaultTimeoutSec = 600;

        // Assert
        vm.HasChanges.Should().BeTrue();
    }
}

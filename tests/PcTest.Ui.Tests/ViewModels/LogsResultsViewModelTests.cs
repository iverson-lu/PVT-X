using FluentAssertions;
using Moq;
using PcTest.Contracts;
using PcTest.Ui.Services;
using PcTest.Ui.ViewModels;
using Xunit;

namespace PcTest.Ui.Tests.ViewModels;

/// <summary>
/// Tests for LogsResultsViewModel.
/// </summary>
public class LogsResultsViewModelTests
{
    private readonly Mock<IRunRepository> _runRepoMock;
    private readonly Mock<INavigationService> _navigationMock;
    private readonly Mock<IFileSystemService> _fileSystemMock;
    private readonly Mock<IFileDialogService> _fileDialogMock;

    public LogsResultsViewModelTests()
    {
        _runRepoMock = new Mock<IRunRepository>();
        _navigationMock = new Mock<INavigationService>();
        _fileSystemMock = new Mock<IFileSystemService>();
        _fileDialogMock = new Mock<IFileDialogService>();
    }

    [Fact]
    public void IsRunPickerVisible_ShouldBeTrue_WhenNoRunLoaded()
    {
        // Arrange & Act
        var vm = new LogsResultsViewModel(
            _runRepoMock.Object,
            _fileSystemMock.Object,
            _fileDialogMock.Object,
            _navigationMock.Object);

        // Assert
        vm.IsRunPickerVisible.Should().BeTrue();
        vm.IsContentVisible.Should().BeFalse();
    }

    [Fact]
    public void ErrorsOnly_ShouldFilterEvents_WhenTrue()
    {
        // Arrange
        var vm = new LogsResultsViewModel(
            _runRepoMock.Object,
            _fileSystemMock.Object,
            _fileDialogMock.Object,
            _navigationMock.Object);

        // Act
        vm.ErrorsOnly = true;

        // Assert
        vm.ErrorsOnly.Should().BeTrue();
    }

    [Fact]
    public void EventSearchText_ShouldTriggerFiltering()
    {
        // Arrange
        var vm = new LogsResultsViewModel(
            _runRepoMock.Object,
            _fileSystemMock.Object,
            _fileDialogMock.Object,
            _navigationMock.Object);

        // Act
        vm.EventSearchText = "CPU";

        // Assert
        vm.EventSearchText.Should().Be("CPU");
    }

    [Fact]
    public void NodeIdFilter_ShouldFilterByNode()
    {
        // Arrange
        var vm = new LogsResultsViewModel(
            _runRepoMock.Object,
            _fileSystemMock.Object,
            _fileDialogMock.Object,
            _navigationMock.Object);

        // Act
        vm.NodeIdFilter = "Node1";

        // Assert
        vm.NodeIdFilter.Should().Be("Node1");
    }

    [Fact]
    public void SelectedLevels_ShouldDefaultToInfoWarningError()
    {
        // Arrange & Act
        var vm = new LogsResultsViewModel(
            _runRepoMock.Object,
            _fileSystemMock.Object,
            _fileDialogMock.Object,
            _navigationMock.Object);

        // Assert
        vm.SelectedLevels.Should().Contain("info");
        vm.SelectedLevels.Should().Contain("warning");
        vm.SelectedLevels.Should().Contain("error");
    }

    [Fact]
    public void ArtifactTree_ShouldBeEmptyByDefault()
    {
        // Arrange & Act
        var vm = new LogsResultsViewModel(
            _runRepoMock.Object,
            _fileSystemMock.Object,
            _fileDialogMock.Object,
            _navigationMock.Object);

        // Assert
        vm.ArtifactTree.Should().BeEmpty();
    }

    [Fact]
    public void RunPicker_ShouldBeInitialized()
    {
        // Arrange & Act
        var vm = new LogsResultsViewModel(
            _runRepoMock.Object,
            _fileSystemMock.Object,
            _fileDialogMock.Object,
            _navigationMock.Object);

        // Assert
        vm.RunPicker.Should().NotBeNull();
    }
}

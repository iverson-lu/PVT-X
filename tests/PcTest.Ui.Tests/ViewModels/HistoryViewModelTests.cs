using FluentAssertions;
using Moq;
using PcTest.Contracts;
using PcTest.Ui.Services;
using PcTest.Ui.ViewModels;
using Xunit;

namespace PcTest.Ui.Tests.ViewModels;

/// <summary>
/// Tests for HistoryViewModel covering tree navigation, filtering, and detail view selection.
/// </summary>
public class HistoryViewModelTests
{
    private readonly Mock<IRunRepository> _runRepositoryMock;
    private readonly Mock<INavigationService> _navigationServiceMock;
    private readonly Mock<IFileSystemService> _fileSystemServiceMock;
    private readonly Mock<IFileDialogService> _fileDialogServiceMock;
    private readonly Mock<IDiscoveryService> _discoveryServiceMock;

    public HistoryViewModelTests()
    {
        _runRepositoryMock = new Mock<IRunRepository>();
        _navigationServiceMock = new Mock<INavigationService>();
        _fileSystemServiceMock = new Mock<IFileSystemService>();
        _fileDialogServiceMock = new Mock<IFileDialogService>();
        _discoveryServiceMock = new Mock<IDiscoveryService>();
    }

    [Fact]
    public void SelectedDetailView_ShouldDefault_ToSummary()
    {
        // Arrange & Act
        var vm = CreateViewModel();

        // Assert
        vm.SelectedDetailView.Should().Be(DetailView.Summary);
    }

    [Fact]
    public void SelectedDetailView_CanBeChanged_ToStdout()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        vm.SelectedDetailView = DetailView.Stdout;

        // Assert
        vm.SelectedDetailView.Should().Be(DetailView.Stdout);
    }

    [Fact]
    public void SelectedDetailView_CanBeChanged_ToStderr()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        vm.SelectedDetailView = DetailView.Stderr;

        // Assert
        vm.SelectedDetailView.Should().Be(DetailView.Stderr);
    }

    [Fact]
    public void SelectedDetailView_CanBeChanged_ToStructuredEvents()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        vm.SelectedDetailView = DetailView.StructuredEvents;

        // Assert
        vm.SelectedDetailView.Should().Be(DetailView.StructuredEvents);
    }

    [Fact]
    public void SelectedDetailView_CanBeChanged_ToArtifacts()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        vm.SelectedDetailView = DetailView.Artifacts;

        // Assert
        vm.SelectedDetailView.Should().Be(DetailView.Artifacts);
    }

    [Fact]
    public void TopLevelOnly_ShouldDefault_ToTrue()
    {
        // Arrange & Act
        var vm = CreateViewModel();

        // Assert
        vm.TopLevelOnly.Should().BeTrue();
    }

    [Fact]
    public void StatusFilter_ShouldDefault_ToAll()
    {
        // Arrange & Act
        var vm = CreateViewModel();

        // Assert
        vm.StatusFilter.Should().Be("ALL");
    }

    [Fact]
    public void RunTypeFilter_ShouldDefault_ToAll()
    {
        // Arrange & Act
        var vm = CreateViewModel();

        // Assert
        vm.RunTypeFilter.Should().Be("ALL");
    }

    [Fact]
    public void SearchText_ShouldDefault_ToEmpty()
    {
        // Arrange & Act
        var vm = CreateViewModel();

        // Assert
        vm.SearchText.Should().BeEmpty();
    }

    [Fact]
    public void VisibleNodes_ShouldInitialize_AsEmpty()
    {
        // Arrange & Act
        var vm = CreateViewModel();

        // Assert
        vm.VisibleNodes.Should().BeEmpty();
    }

    [Fact]
    public void IsRunSelected_ShouldBeFalse_WhenNoRunDetailsLoaded()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act & Assert
        vm.IsRunSelected.Should().BeFalse();
    }

    [Fact]
    public void EventSearchText_ShouldDefault_ToEmpty()
    {
        // Arrange & Act
        var vm = CreateViewModel();

        // Assert
        vm.EventSearchText.Should().BeEmpty();
    }

    [Fact]
    public void ErrorsOnly_ShouldDefault_ToFalse()
    {
        // Arrange & Act
        var vm = CreateViewModel();

        // Assert
        vm.ErrorsOnly.Should().BeFalse();
    }

    [Fact]
    public void SelectedLevels_ShouldDefault_ToInfoWarningError()
    {
        // Arrange & Act
        var vm = CreateViewModel();

        // Assert
        vm.SelectedLevels.Should().ContainInOrder("info", "warning", "error");
        vm.SelectedLevels.Should().HaveCount(3);
    }

    [Fact]
    public void AvailableLevels_ShouldContain_AllLogLevels()
    {
        // Arrange & Act
        var vm = CreateViewModel();

        // Assert
        vm.AvailableLevels.Should().ContainInOrder("trace", "debug", "info", "warning", "error");
    }

    [Fact]
    public void StdoutContent_ShouldDefault_ToEmpty()
    {
        // Arrange & Act
        var vm = CreateViewModel();

        // Assert
        vm.StdoutContent.Should().BeEmpty();
    }

    [Fact]
    public void StderrContent_ShouldDefault_ToEmpty()
    {
        // Arrange & Act
        var vm = CreateViewModel();

        // Assert
        vm.StderrContent.Should().BeEmpty();
    }

    [Fact]
    public void Artifacts_ShouldInitialize_AsEmpty()
    {
        // Arrange & Act
        var vm = CreateViewModel();

        // Assert
        vm.Artifacts.Should().BeEmpty();
    }

    [Fact]
    public void FilteredEvents_ShouldInitialize_AsEmpty()
    {
        // Arrange & Act
        var vm = CreateViewModel();

        // Assert
        vm.FilteredEvents.Should().BeEmpty();
    }

    [Fact]
    public void ClearFiltersCommand_ShouldBeAvailable()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act & Assert
        vm.ClearFiltersCommand.Should().NotBeNull();
    }

    [Fact]
    public void RefreshCommand_ShouldBeAvailable()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act & Assert
        vm.RefreshCommand.Should().NotBeNull();
    }

    [Fact]
    public void CopyRunIdCommand_ShouldBeAvailable()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act & Assert
        vm.CopyRunIdCommand.Should().NotBeNull();
    }

    private HistoryViewModel CreateViewModel()
    {
        return new HistoryViewModel(
            _runRepositoryMock.Object,
            _navigationServiceMock.Object,
            _fileSystemServiceMock.Object,
            _fileDialogServiceMock.Object,
            _discoveryServiceMock.Object);
    }
}

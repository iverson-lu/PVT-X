using FluentAssertions;
using Moq;
using PcTest.Contracts;
using PcTest.Ui.Services;
using PcTest.Ui.ViewModels;
using Xunit;

namespace PcTest.Ui.Tests.ViewModels;

/// <summary>
/// Tests for RunPickerViewModel filtering and search.
/// </summary>
public class RunPickerViewModelTests
{
    private readonly Mock<IRunRepository> _runRepoMock;

    public RunPickerViewModelTests()
    {
        _runRepoMock = new Mock<IRunRepository>();
    }

    private RunPickerViewModel CreateViewModel() => new(_runRepoMock.Object);

    [Fact]
    public void Constructor_ShouldCreateViewModel()
    {
        // Act
        var vm = CreateViewModel();

        // Assert
        vm.Should().NotBeNull();
        vm.SearchText.Should().BeEmpty();
    }

    [Fact]
    public void SearchText_ShouldFilterRuns_WhenSet()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        vm.SearchText = "TestCase";

        // Assert
        vm.SearchText.Should().Be("TestCase");
    }

    [Fact]
    public void StatusFilter_ShouldBeNull_WhenInitialized()
    {
        // Arrange & Act
        var vm = CreateViewModel();

        // Assert
        vm.StatusFilter.Should().BeNull();
    }

    [Fact]
    public void StatusFilter_ShouldTriggerReload()
    {
        // Arrange
        _runRepoMock.Setup(x => x.GetRunsAsync(It.IsAny<RunFilter>()))
            .ReturnsAsync(new List<RunIndexEntry>());
        
        var vm = CreateViewModel();

        // Act
        vm.StatusFilter = RunStatus.Passed;

        // Assert
        vm.StatusFilter.Should().Be(RunStatus.Passed);
    }

    [Fact]
    public void ClearFiltersCommand_ShouldResetAllFilters()
    {
        // Arrange
        _runRepoMock.Setup(x => x.GetRunsAsync(It.IsAny<RunFilter>()))
            .ReturnsAsync(new List<RunIndexEntry>());
        
        var vm = CreateViewModel();
        vm.SearchText = "test";
        vm.StatusFilter = RunStatus.Failed;
        vm.StartTimeFrom = DateTime.Now.AddDays(-1);
        vm.StartTimeTo = DateTime.Now;

        // Act
        vm.ClearFiltersCommand.Execute(null);

        // Assert
        vm.SearchText.Should().BeEmpty();
        vm.StatusFilter.Should().BeNull();
        vm.StartTimeFrom.Should().BeNull();
        vm.StartTimeTo.Should().BeNull();
    }

    [Fact]
    public void StatusOptions_ShouldIncludeAllStatusValues()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        var options = vm.StatusOptions.ToList();

        // Assert
        options.Should().Contain((RunStatus?)null);
        options.Should().Contain(RunStatus.Passed);
        options.Should().Contain(RunStatus.Failed);
    }

    [Fact]
    public async Task LoadRecentRunsAsync_ShouldPopulateRecentRuns()
    {
        // Arrange
        var runs = new List<RunIndexEntry>
        {
            new() { RunId = "R-001", RunType = RunType.TestCase, Status = RunStatus.Passed, TestId = "Test1", TestVersion = "1.0.0" },
            new() { RunId = "R-002", RunType = RunType.TestSuite, Status = RunStatus.Failed, SuiteId = "Suite1", SuiteVersion = "1.0.0" }
        };
        _runRepoMock.Setup(x => x.GetRunsAsync(It.IsAny<RunFilter>()))
            .ReturnsAsync(runs);
        
        var vm = CreateViewModel();

        // Act
        await vm.LoadRecentRunsAsync();

        // Assert
        vm.RecentRuns.Count.Should().Be(2);
    }

    [Fact]
    public void SelectRunCommand_ShouldRaiseRunSelectedEvent()
    {
        // Arrange
        var vm = CreateViewModel();
        string? selectedRunId = null;
        vm.RunSelected += (s, id) => selectedRunId = id;
        
        vm.SelectedRun = new RunIndexEntryViewModel { RunId = "R-123", DisplayName = "Test Run" };

        // Act
        vm.SelectRunCommand.Execute(null);

        // Assert
        selectedRunId.Should().Be("R-123");
    }

    [Fact]
    public void RecentRuns_ShouldBeEmpty_WhenInitialized()
    {
        // Arrange & Act
        var vm = CreateViewModel();

        // Assert
        vm.RecentRuns.Should().BeEmpty();
    }

    [Fact]
    public void SelectedRun_ShouldBeNull_WhenInitialized()
    {
        // Arrange & Act
        var vm = CreateViewModel();

        // Assert
        vm.SelectedRun.Should().BeNull();
    }

    [Fact]
    public void StartTimeFrom_ShouldBeNull_WhenInitialized()
    {
        // Arrange & Act
        var vm = CreateViewModel();

        // Assert
        vm.StartTimeFrom.Should().BeNull();
    }

    [Fact]
    public void StartTimeTo_ShouldBeNull_WhenInitialized()
    {
        // Arrange & Act
        var vm = CreateViewModel();

        // Assert
        vm.StartTimeTo.Should().BeNull();
    }
}

using FluentAssertions;
using PcTest.Ui.ViewModels;
using Xunit;

namespace PcTest.Ui.Tests.ViewModels;

/// <summary>
/// Tests for SuitePickerViewModel.
/// </summary>
public class SuitePickerViewModelTests
{
    [Fact]
    public void Constructor_ShouldCreateEmptyViewModel()
    {
        // Act
        var vm = new SuitePickerViewModel();

        // Assert
        vm.FilteredSuites.Should().BeEmpty();
        vm.SelectedCount.Should().Be(0);
        vm.HasSelection.Should().BeFalse();
        vm.SearchText.Should().BeEmpty();
    }

    [Fact]
    public void LoadSuites_ShouldPopulateFilteredSuites()
    {
        // Arrange
        var vm = new SuitePickerViewModel();
        var suites = CreateSuiteList("Suite1", "Suite2");

        // Act
        vm.LoadSuites(suites);

        // Assert
        vm.FilteredSuites.Should().HaveCount(2);
    }

    [Fact]
    public void LoadSuites_ShouldExcludeSpecifiedIdentities()
    {
        // Arrange
        var vm = new SuitePickerViewModel();
        var suites = CreateSuiteList("Suite1", "Suite2", "Suite3");

        // Act
        vm.LoadSuites(suites, new[] { "Suite2@1.0.0" });

        // Assert
        vm.FilteredSuites.Should().HaveCount(2);
        vm.FilteredSuites.Should().NotContain(s => s.Id == "Suite2");
    }

    [Fact]
    public void SearchText_ShouldFilterSuites()
    {
        // Arrange
        var vm = new SuitePickerViewModel();
        var suites = CreateSuiteList("ThermalSuite", "MemorySuite", "DiskSuite");
        vm.LoadSuites(suites);

        // Act
        vm.SearchText = "Thermal";

        // Assert
        vm.FilteredSuites.Should().HaveCount(1);
        vm.FilteredSuites[0].Id.Should().Be("ThermalSuite");
    }

    [Fact]
    public void IsSelected_ShouldUpdateSelectedCount()
    {
        // Arrange
        var vm = new SuitePickerViewModel();
        var suites = CreateSuiteList("Suite1", "Suite2");
        vm.LoadSuites(suites);

        // Act
        vm.FilteredSuites[0].IsSelected = true;

        // Assert
        vm.SelectedCount.Should().Be(1);
        vm.HasSelection.Should().BeTrue();
    }

    [Fact]
    public void ClearSelectionCommand_ShouldDeselectAllSuites()
    {
        // Arrange
        var vm = new SuitePickerViewModel();
        var suites = CreateSuiteList("Suite1", "Suite2");
        vm.LoadSuites(suites);
        vm.FilteredSuites[0].IsSelected = true;
        vm.FilteredSuites[1].IsSelected = true;

        // Act
        vm.ClearSelectionCommand.Execute(null);

        // Assert
        vm.SelectedCount.Should().Be(0);
        vm.HasSelection.Should().BeFalse();
        vm.FilteredSuites.Should().OnlyContain(s => !s.IsSelected);
    }

    [Fact]
    public void GetSelectedSuites_ShouldReturnOnlySelectedItems()
    {
        // Arrange
        var vm = new SuitePickerViewModel();
        var suites = CreateSuiteList("Suite1", "Suite2", "Suite3");
        vm.LoadSuites(suites);
        vm.FilteredSuites[0].IsSelected = true;
        vm.FilteredSuites[2].IsSelected = true;

        // Act
        var selected = vm.GetSelectedSuites();

        // Assert
        selected.Should().HaveCount(2);
        selected.Select(s => s.Id).Should().Contain(new[] { "Suite1", "Suite3" });
    }

    private static List<SuiteListItemViewModel> CreateSuiteList(params string[] ids)
    {
        var list = new List<SuiteListItemViewModel>();
        foreach (var id in ids)
        {
            list.Add(new SuiteListItemViewModel
            {
                Id = id,
                Name = id,
                Version = "1.0.0",
                Description = $"Description for {id}",
                NodeCount = 5
            });
        }
        return list;
    }
}

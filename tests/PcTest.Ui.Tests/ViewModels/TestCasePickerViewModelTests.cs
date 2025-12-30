using FluentAssertions;
using PcTest.Contracts.Manifests;
using PcTest.Engine.Discovery;
using PcTest.Ui.ViewModels;
using Xunit;

namespace PcTest.Ui.Tests.ViewModels;

/// <summary>
/// Tests for TestCasePickerViewModel.
/// </summary>
public class TestCasePickerViewModelTests
{
    [Fact]
    public void Constructor_ShouldCreateEmptyViewModel()
    {
        // Act
        var vm = new TestCasePickerViewModel();

        // Assert
        vm.FilteredTestCases.Should().BeEmpty();
        vm.SelectedCount.Should().Be(0);
        vm.HasSelection.Should().BeFalse();
        vm.SearchText.Should().BeEmpty();
    }

    [Fact]
    public void LoadTestCases_ShouldPopulateFilteredTestCases()
    {
        // Arrange
        var vm = new TestCasePickerViewModel();
        var discovery = CreateDiscoveryWithTestCases("TestCase1", "TestCase2");

        // Act
        vm.LoadTestCases(discovery);

        // Assert
        vm.FilteredTestCases.Should().HaveCount(2);
    }

    [Fact]
    public void LoadTestCases_ShouldExcludeSpecifiedRefs()
    {
        // Arrange
        var vm = new TestCasePickerViewModel();
        var discovery = CreateDiscoveryWithTestCases("TestCase1", "TestCase2", "TestCase3");

        // Act
        vm.LoadTestCases(discovery, new[] { "TestCase2" });

        // Assert
        vm.FilteredTestCases.Should().HaveCount(2);
        vm.FilteredTestCases.Should().NotContain(tc => tc.Id == "TestCase2");
    }

    [Fact]
    public void SearchText_ShouldFilterTestCases()
    {
        // Arrange
        var vm = new TestCasePickerViewModel();
        var discovery = CreateDiscoveryWithTestCases("CpuStress", "MemoryTest", "DiskCheck");
        vm.LoadTestCases(discovery);

        // Act
        vm.SearchText = "Cpu";

        // Assert
        vm.FilteredTestCases.Should().HaveCount(1);
        vm.FilteredTestCases[0].Id.Should().Be("CpuStress");
    }

    [Fact]
    public void IsSelected_ShouldUpdateSelectedCount()
    {
        // Arrange
        var vm = new TestCasePickerViewModel();
        var discovery = CreateDiscoveryWithTestCases("TestCase1", "TestCase2");
        vm.LoadTestCases(discovery);

        // Act
        vm.FilteredTestCases[0].IsSelected = true;

        // Assert
        vm.SelectedCount.Should().Be(1);
        vm.HasSelection.Should().BeTrue();
    }

    [Fact]
    public void ClearSelectionCommand_ShouldDeselectAllTestCases()
    {
        // Arrange
        var vm = new TestCasePickerViewModel();
        var discovery = CreateDiscoveryWithTestCases("TestCase1", "TestCase2");
        vm.LoadTestCases(discovery);
        vm.FilteredTestCases[0].IsSelected = true;
        vm.FilteredTestCases[1].IsSelected = true;

        // Act
        vm.ClearSelectionCommand.Execute(null);

        // Assert
        vm.SelectedCount.Should().Be(0);
        vm.HasSelection.Should().BeFalse();
        vm.FilteredTestCases.Should().OnlyContain(tc => !tc.IsSelected);
    }

    [Fact]
    public void GetSelectedTestCases_ShouldReturnOnlySelectedItems()
    {
        // Arrange
        var vm = new TestCasePickerViewModel();
        var discovery = CreateDiscoveryWithTestCases("TestCase1", "TestCase2", "TestCase3");
        vm.LoadTestCases(discovery);
        vm.FilteredTestCases[0].IsSelected = true;
        vm.FilteredTestCases[2].IsSelected = true;

        // Act
        var selected = vm.GetSelectedTestCases();

        // Assert
        selected.Should().HaveCount(2);
        selected.Select(tc => tc.Id).Should().Contain(new[] { "TestCase1", "TestCase3" });
    }

    private static DiscoveryResult CreateDiscoveryWithTestCases(params string[] ids)
    {
        var result = new DiscoveryResult();
        foreach (var id in ids)
        {
            result.TestCases.Add($"{id}@1.0.0", new DiscoveredTestCase
            {
                Manifest = new TestCaseManifest
                {
                    SchemaVersion = "1.5.0",
                    Id = id,
                    Name = id,
                    Version = "1.0.0",
                    Category = "Test"
                },
                FolderPath = $"c:\\TestCases\\{id}",
                ManifestPath = $"c:\\TestCases\\{id}\\test.manifest.json"
            });
        }
        return result;
    }
}

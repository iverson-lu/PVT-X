using FluentAssertions;
using Moq;
using PcTest.Engine.Discovery;
using PcTest.Ui.Services;
using PcTest.Ui.ViewModels;
using Xunit;

namespace PcTest.Ui.Tests.ViewModels;

/// <summary>
/// Tests for SuiteEditorViewModel.
/// </summary>
public class SuiteEditorViewModelTests
{
    private readonly Mock<ISuiteRepository> _suiteRepoMock;
    private readonly Mock<IDiscoveryService> _discoveryMock;
    private readonly Mock<IFileDialogService> _fileDialogMock;
    private readonly Mock<INavigationService> _navigationMock;

    public SuiteEditorViewModelTests()
    {
        _suiteRepoMock = new Mock<ISuiteRepository>();
        _discoveryMock = new Mock<IDiscoveryService>();
        _fileDialogMock = new Mock<IFileDialogService>();
        _navigationMock = new Mock<INavigationService>();
    }

    private SuiteEditorViewModel CreateViewModel() =>
        new(_suiteRepoMock.Object, _discoveryMock.Object, _fileDialogMock.Object, _navigationMock.Object);

    [Fact]
    public void Constructor_ShouldCreateViewModel()
    {
        // Act
        var vm = CreateViewModel();

        // Assert
        vm.Should().NotBeNull();
        vm.Id.Should().BeEmpty();
    }

    [Fact]
    public void IsDirty_ShouldBeTrue_WhenIdChanged()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        vm.Id = "NewSuiteId";

        // Assert
        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void IsDirty_ShouldBeTrue_WhenNameChanged()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        vm.Name = "New Name";

        // Assert
        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void IsDirty_ShouldBeTrue_WhenVersionChanged()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        vm.Version = "2.0.0";

        // Assert
        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void IsDirty_ShouldBeTrue_WhenDescriptionChanged()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        vm.Description = "New description";

        // Assert
        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void IsDirty_ShouldBeTrue_WhenTagsTextChanged()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        vm.TagsText = "tag1, tag2";

        // Assert
        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void IsDirty_ShouldBeTrue_WhenRepeatChanged()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        vm.Repeat = 5;

        // Assert
        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void IsDirty_ShouldBeTrue_WhenMaxParallelChanged()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        vm.MaxParallel = 4;

        // Assert
        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void IsDirty_ShouldBeTrue_WhenContinueOnFailureChanged()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        vm.ContinueOnFailure = true;

        // Assert
        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void IsDirty_ShouldBeTrue_WhenRetryOnErrorChanged()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        vm.RetryOnError = 3;

        // Assert
        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void Nodes_ShouldBeEmpty_WhenInitialized()
    {
        // Arrange & Act
        var vm = CreateViewModel();

        // Assert
        vm.Nodes.Should().BeEmpty();
    }

    [Fact]
    public void AvailableTestCases_ShouldBeEmpty_WhenInitialized()
    {
        // Arrange & Act
        var vm = CreateViewModel();

        // Assert
        vm.AvailableTestCases.Should().BeEmpty();
    }
}

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
/// Tests for PlanEditorViewModel.
/// </summary>
public class PlanEditorViewModelTests
{
    private readonly Mock<IPlanRepository> _planRepoMock;
    private readonly Mock<ISuiteRepository> _suiteRepoMock;
    private readonly Mock<IDiscoveryService> _discoveryMock;
    private readonly Mock<IFileDialogService> _fileDialogMock;
    private readonly Mock<INavigationService> _navigationMock;

    public PlanEditorViewModelTests()
    {
        _planRepoMock = new Mock<IPlanRepository>();
        _suiteRepoMock = new Mock<ISuiteRepository>();
        _discoveryMock = new Mock<IDiscoveryService>();
        _fileDialogMock = new Mock<IFileDialogService>();
        _navigationMock = new Mock<INavigationService>();
    }

    private PlanEditorViewModel CreateViewModel() =>
        new(_planRepoMock.Object, _suiteRepoMock.Object, _discoveryMock.Object, _fileDialogMock.Object, _navigationMock.Object);

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
        vm.Id = "NewPlanId";

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
    public void SuiteReferences_ShouldBeEmpty_WhenInitialized()
    {
        // Arrange & Act
        var vm = CreateViewModel();

        // Assert
        vm.SuiteReferences.Should().BeEmpty();
    }

    [Fact]
    public void AvailableSuites_ShouldBeEmpty_WhenInitialized()
    {
        // Arrange & Act
        var vm = CreateViewModel();

        // Assert
        vm.AvailableSuites.Should().BeEmpty();
    }

    [Fact]
    public void SelectedSuiteReference_ShouldBeNull_WhenInitialized()
    {
        // Arrange & Act
        var vm = CreateViewModel();

        // Assert
        vm.SelectedSuiteReference.Should().BeNull();
    }

    [Fact]
    public void SuiteReferenceViewModel_CaseCount_ShouldReturn0_WhenDiscoveryIsNull()
    {
        // Arrange
        var suiteRef = new SuiteReferenceViewModel(null)
        {
            SuiteIdentity = "suite.test@1.0.0"
        };

        // Act & Assert
        suiteRef.CaseCount.Should().Be(0);
    }

    [Fact]
    public void SuiteReferenceViewModel_CaseCount_ShouldReturnCorrectCount_WhenSuiteExists()
    {
        // Arrange
        var discovery = new DiscoveryResult();
        discovery.TestSuites.Add("suite.test@1.0.0", new DiscoveredTestSuite
        {
            Manifest = new TestSuiteManifest
            {
                Id = "suite.test",
                Name = "Test Suite",
                Version = "1.0.0",
                SchemaVersion = "1.5.0",
                TestCases = new List<TestCaseNode>
                {
                    new TestCaseNode { NodeId = "node1", Ref = "case1" },
                    new TestCaseNode { NodeId = "node2", Ref = "case2" },
                    new TestCaseNode { NodeId = "node3", Ref = "case3" }
                }
            },
            FolderPath = "path/to/suite",
            ManifestPath = "path/to/suite/suite.manifest.json"
        });

        var discoveryMock = new Mock<IDiscoveryService>();
        discoveryMock.Setup(d => d.CurrentDiscovery).Returns(discovery);

        var suiteRef = new SuiteReferenceViewModel(discoveryMock.Object)
        {
            SuiteIdentity = "suite.test@1.0.0"
        };

        // Act & Assert
        suiteRef.CaseCount.Should().Be(3);
    }

    [Fact]
    public void SuiteReferenceViewModel_Name_ShouldReturnSuiteName_WhenSuiteExists()
    {
        // Arrange
        var discovery = new DiscoveryResult();
        discovery.TestSuites.Add("suite.test@1.0.0", new DiscoveredTestSuite
        {
            Manifest = new TestSuiteManifest
            {
                Id = "suite.test",
                Name = "Test Suite",
                Version = "1.0.0",
                SchemaVersion = "1.5.0",
                TestCases = new List<TestCaseNode>()
            },
            FolderPath = "path/to/suite",
            ManifestPath = "path/to/suite/suite.manifest.json"
        });

        var discoveryMock = new Mock<IDiscoveryService>();
        discoveryMock.Setup(d => d.CurrentDiscovery).Returns(discovery);

        var suiteRef = new SuiteReferenceViewModel(discoveryMock.Object)
        {
            SuiteIdentity = "suite.test@1.0.0"
        };

        // Act & Assert
        suiteRef.Name.Should().Be("Test Suite");
    }

    [Fact]
    public void SuiteReferenceViewModel_CaseCount_ShouldReturn0_WhenSuiteHasNoTestCases()
    {
        // Arrange
        var discovery = new DiscoveryResult();
        discovery.TestSuites.Add("suite.empty@1.0.0", new DiscoveredTestSuite
        {
            Manifest = new TestSuiteManifest
            {
                Id = "suite.empty",
                Name = "Empty Suite",
                Version = "1.0.0",
                SchemaVersion = "1.5.0",
                TestCases = new List<TestCaseNode>()
            },
            FolderPath = "path/to/suite",
            ManifestPath = "path/to/suite/suite.manifest.json"
        });

        var discoveryMock = new Mock<IDiscoveryService>();
        discoveryMock.Setup(d => d.CurrentDiscovery).Returns(discovery);

        var suiteRef = new SuiteReferenceViewModel(discoveryMock.Object)
        {
            SuiteIdentity = "suite.empty@1.0.0"
        };

        // Act & Assert
        suiteRef.CaseCount.Should().Be(0);
    }

    [Fact]
    public void SuiteReferenceViewModel_ToSuiteNode_ShouldAlwaysIncludeControls()
    {
        // Arrange
        var suiteRef = new SuiteReferenceViewModel(null)
        {
            SuiteIdentity = "suite.test@1.0.0",
            Ref = "Test Suite"
        };

        // Act
        var node = suiteRef.ToSuiteNode();

        // Assert
        node.NodeId.Should().Be("suite.test@1.0.0");
        node.Ref.Should().Be("Test Suite");
        node.Controls.Should().NotBeNull();
        node.Controls!.Repeat.Should().Be(1);
        node.Controls.MaxParallel.Should().Be(1);
        node.Controls.ContinueOnFailure.Should().BeFalse();
        node.Controls.RetryOnError.Should().Be(0);
    }

    [Fact]
    public void SuiteReferenceViewModel_ToSuiteNode_ShouldIncludeControls_WhenRepeatIsOverridden()
    {
        // Arrange
        var suiteRef = new SuiteReferenceViewModel(null)
        {
            SuiteIdentity = "suite.test@1.0.0",
            Ref = "Test Suite",
            Repeat = 3.0
        };

        // Act
        var node = suiteRef.ToSuiteNode();

        // Assert
        node.Controls.Should().NotBeNull();
        node.Controls!.Repeat.Should().Be(3);
        node.Controls.MaxParallel.Should().Be(1);
        node.Controls.ContinueOnFailure.Should().BeFalse();
        node.Controls.RetryOnError.Should().Be(0);
    }

    [Fact]
    public void SuiteReferenceViewModel_ToSuiteNode_ShouldIncludeControls_WhenMaxParallelIsOverridden()
    {
        // Arrange
        var suiteRef = new SuiteReferenceViewModel(null)
        {
            SuiteIdentity = "suite.test@1.0.0",
            Ref = "Test Suite",
            MaxParallel = 4.0
        };

        // Act
        var node = suiteRef.ToSuiteNode();

        // Assert
        node.Controls.Should().NotBeNull();
        node.Controls!.MaxParallel.Should().Be(4);
    }

    [Fact]
    public void SuiteReferenceViewModel_ToSuiteNode_ShouldIncludeControls_WhenContinueOnFailureIsTrue()
    {
        // Arrange
        var suiteRef = new SuiteReferenceViewModel(null)
        {
            SuiteIdentity = "suite.test@1.0.0",
            Ref = "Test Suite",
            ContinueOnFailure = true
        };

        // Act
        var node = suiteRef.ToSuiteNode();

        // Assert
        node.Controls.Should().NotBeNull();
        node.Controls!.ContinueOnFailure.Should().BeTrue();
    }

    [Fact]
    public void SuiteReferenceViewModel_ToSuiteNode_ShouldIncludeControls_WhenRetryOnErrorIsSet()
    {
        // Arrange
        var suiteRef = new SuiteReferenceViewModel(null)
        {
            SuiteIdentity = "suite.test@1.0.0",
            Ref = "Test Suite",
            RetryOnError = 2.0
        };

        // Act
        var node = suiteRef.ToSuiteNode();

        // Assert
        node.Controls.Should().NotBeNull();
        node.Controls!.RetryOnError.Should().Be(2);
    }

    [Fact]
    public void SuiteReferenceViewModel_LoadControls_ShouldSetDefaultValues_WhenControlsIsNull()
    {
        // Arrange
        var suiteRef = new SuiteReferenceViewModel(null)
        {
            SuiteIdentity = "suite.test@1.0.0",
            Repeat = 5.0,
            MaxParallel = 3.0
        };

        // Act
        suiteRef.LoadControls(null);

        // Assert
        suiteRef.Repeat.Should().Be(1);
        suiteRef.MaxParallel.Should().Be(1);
        suiteRef.ContinueOnFailure.Should().BeFalse();
        suiteRef.RetryOnError.Should().Be(0);
        suiteRef.HasControlsOverride.Should().BeFalse();
    }

    [Fact]
    public void SuiteReferenceViewModel_LoadControls_ShouldSetValues_WhenControlsIsProvided()
    {
        // Arrange
        var suiteRef = new SuiteReferenceViewModel(null)
        {
            SuiteIdentity = "suite.test@1.0.0"
        };
        var controls = new SuiteControls
        {
            Repeat = 5,
            MaxParallel = 3,
            ContinueOnFailure = true,
            RetryOnError = 2
        };

        // Act
        suiteRef.LoadControls(controls);

        // Assert
        suiteRef.Repeat.Should().Be(5);
        suiteRef.MaxParallel.Should().Be(3);
        suiteRef.ContinueOnFailure.Should().BeTrue();
        suiteRef.RetryOnError.Should().Be(2);
        suiteRef.HasControlsOverride.Should().BeTrue();
    }

    [Fact]
    public void SuiteReferenceViewModel_HasControlsOverride_ShouldUpdate_WhenRepeatChanges()
    {
        // Arrange
        var suiteRef = new SuiteReferenceViewModel(null)
        {
            SuiteIdentity = "suite.test@1.0.0"
        };
        suiteRef.HasControlsOverride.Should().BeFalse();

        // Act
        suiteRef.Repeat = 2.0;

        // Assert
        suiteRef.HasControlsOverride.Should().BeTrue();
    }
}

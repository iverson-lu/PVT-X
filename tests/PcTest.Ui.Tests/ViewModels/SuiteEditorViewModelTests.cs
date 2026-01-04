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

    [Fact]
    public async Task AddNodeCommand_ShouldAddNodes_WhenTestCasesSelected()
    {
        // Arrange
        var vm = CreateViewModel();
        var discovery = new DiscoveryResult();
        
        _discoveryMock.Setup(d => d.CurrentDiscovery).Returns(discovery);
        _fileDialogMock
            .Setup(f => f.ShowTestCasePicker(It.IsAny<DiscoveryResult>(), It.IsAny<IEnumerable<string>?>()))
            .Returns(new List<(string Id, string Name, string Version, string FolderName)>
            {
                ("TestCase1", "Test Case 1", "1.0.0", "TestCase1"),
                ("TestCase2", "Test Case 2", "1.0.0", "TestCase2")
            });

        // Act
        await vm.AddNodeCommand.ExecuteAsync(null);

        // Assert
        vm.Nodes.Should().HaveCount(2);
        vm.Nodes[0].NodeId.Should().Be("TestCase1@1.0.0");
        vm.Nodes[0].Ref.Should().Be("Test Case 1");
        vm.Nodes[1].NodeId.Should().Be("TestCase2@1.0.0");
        vm.Nodes[1].Ref.Should().Be("Test Case 2");
        vm.IsDirty.Should().BeTrue();
    }

    [Fact]
    public async Task AddNodeCommand_ShouldNotAddNodes_WhenDialogCancelled()
    {
        // Arrange
        var vm = CreateViewModel();
        var discovery = new DiscoveryResult();
        
        _discoveryMock.Setup(d => d.CurrentDiscovery).Returns(discovery);
        _fileDialogMock
            .Setup(f => f.ShowTestCasePicker(It.IsAny<DiscoveryResult>(), It.IsAny<IEnumerable<string>?>()))
            .Returns(Array.Empty<(string, string, string, string)>());

        // Act
        await vm.AddNodeCommand.ExecuteAsync(null);

        // Assert
        vm.Nodes.Should().BeEmpty();
        vm.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task AddNodeCommand_ShouldGenerateUniqueNodeIds_WhenDuplicateIds()
    {
        // Arrange
        var vm = CreateViewModel();
        var discovery = new DiscoveryResult();
        
        // Add existing node with ID "TestCase1@1.0.0"
        vm.Nodes.Add(new TestCaseNodeViewModel { NodeId = "TestCase1@1.0.0", Ref = "Test Case 1" });
        
        _discoveryMock.Setup(d => d.CurrentDiscovery).Returns(discovery);
        _fileDialogMock
            .Setup(f => f.ShowTestCasePicker(It.IsAny<DiscoveryResult>(), It.IsAny<IEnumerable<string>?>()))
            .Returns(new List<(string Id, string Name, string Version, string FolderName)>
            {
                ("TestCase1", "Test Case 1", "1.0.0", "TestCase1")
            });

        // Act
        await vm.AddNodeCommand.ExecuteAsync(null);

        // Assert
        vm.Nodes.Should().HaveCount(2);
        vm.Nodes[1].NodeId.Should().Be("TestCase1@1.0.0_1"); // Should have unique suffix
    }

    [Fact]
    public async Task AddNodeCommand_ShouldLoadParameters_WhenTestCaseHasParameters()
    {
        // Arrange
        var vm = CreateViewModel();
        var discovery = new DiscoveryResult();
        
        // Setup a test case with parameters
        var testCase = new DiscoveredTestCase
        {
            FolderPath = "TestCases\\SampleCase",
            Manifest = new PcTest.Contracts.Manifests.TestCaseManifest
            {
                Id = "SampleCase",
                Name = "Sample Case",
                Version = "1.0.0",
                Parameters = new List<PcTest.Contracts.Manifests.ParameterDefinition>
                {
                    new()
                    {
                        Name = "TestParam",
                        Type = "string",
                        Required = true,
                        Help = "A test parameter"
                    },
                    new()
                    {
                        Name = "Count",
                        Type = "integer",
                        Required = false,
                        Default = System.Text.Json.JsonSerializer.SerializeToElement(10)
                    }
                }
            }
        };
        
        discovery.TestCases["SampleCase@1.0.0"] = testCase;
        
        _discoveryMock.Setup(d => d.CurrentDiscovery).Returns(discovery);
        _fileDialogMock
            .Setup(f => f.ShowTestCasePicker(It.IsAny<DiscoveryResult>(), It.IsAny<IEnumerable<string>?>()))
            .Returns(new List<(string Id, string Name, string Version, string FolderName)>
            {
                ("SampleCase", "Sample Case", "1.0.0", "SampleCase")
            });

        // Act
        await vm.AddNodeCommand.ExecuteAsync(null);

        // Assert
        vm.Nodes.Should().HaveCount(1);
        var node = vm.Nodes[0];
        node.Parameters.Should().HaveCount(2);
        node.Parameters[0].Name.Should().Be("TestParam");
        node.Parameters[0].Type.Should().Be("string");
        node.Parameters[0].Required.Should().BeTrue();
        node.Parameters[1].Name.Should().Be("Count");
        node.Parameters[1].Type.Should().Be("integer");
        node.Parameters[1].Required.Should().BeFalse();
        node.HasParameters.Should().BeTrue();
    }

    [Fact]
    public async Task LoadAsync_ShouldLoadParametersWithExistingValues()
    {
        // Arrange
        var vm = CreateViewModel();
        var discovery = new DiscoveryResult();
        
        // Setup a test case with parameters
        var testCase = new DiscoveredTestCase
        {
            FolderPath = "TestCases\\SampleCase",
            Manifest = new PcTest.Contracts.Manifests.TestCaseManifest
            {
                Id = "SampleCase",
                Name = "Sample Case",
                Version = "1.0.0",
                Parameters = new List<PcTest.Contracts.Manifests.ParameterDefinition>
                {
                    new()
                    {
                        Name = "TestParam",
                        Type = "string",
                        Required = true
                    }
                }
            }
        };
        
        discovery.TestCases["SampleCase@1.0.0"] = testCase;
        _discoveryMock.Setup(d => d.CurrentDiscovery).Returns(discovery);
        _discoveryMock.Setup(d => d.DiscoverAsync()).ReturnsAsync(discovery);
        
        // Create suite info with existing parameter values
        var suiteInfo = new PcTest.Ui.Services.SuiteInfo
        {
            Manifest = new PcTest.Contracts.Manifests.TestSuiteManifest
            {
                Id = "TestSuite",
                Name = "Test Suite",
                Version = "1.0.0",
                TestCases = new List<PcTest.Contracts.Manifests.TestCaseNode>
                {
                    new()
                    {
                        NodeId = "SampleCase@1.0.0",
                        Ref = "SampleCase",
                        Inputs = new Dictionary<string, System.Text.Json.JsonElement>
                        {
                            ["TestParam"] = System.Text.Json.JsonSerializer.SerializeToElement("ExistingValue")
                        }
                    }
                }
            }
        };

        // Act
        await vm.LoadAsync(suiteInfo);

        // Assert
        vm.Nodes.Should().HaveCount(1);
        var node = vm.Nodes[0];
        node.Parameters.Should().HaveCount(1);
        node.Parameters[0].Name.Should().Be("TestParam");
        node.Parameters[0].CurrentValue.Should().Be("ExistingValue");
    }

    [Fact]
    public void BuildManifest_ShouldSerializeParametersToInputs()
    {
        // Arrange
        var vm = CreateViewModel();
        
        // Create a node with parameters
        var node = new TestCaseNodeViewModel
        {
            NodeId = "node1",
            Ref = "TestCase1"
        };
        
        node.Parameters.Add(new ParameterViewModel(
            new PcTest.Contracts.Manifests.ParameterDefinition
            {
                Name = "StringParam",
                Type = "string",
                Required = true
            })
        {
            CurrentValue = "TestValue"
        });
        
        node.Parameters.Add(new ParameterViewModel(
            new PcTest.Contracts.Manifests.ParameterDefinition
            {
                Name = "IntParam",
                Type = "integer",
                Required = false
            })
        {
            CurrentValue = "42"
        });
        
        node.Parameters.Add(new ParameterViewModel(
            new PcTest.Contracts.Manifests.ParameterDefinition
            {
                Name = "BoolParam",
                Type = "boolean",
                Required = false
            })
        {
            CurrentValue = "true"
        });
        
        vm.Nodes.Add(node);
        vm.Id = "TestSuite";
        vm.Name = "Test Suite";
        vm.Version = "1.0.0";

        // Act
        var manifest = typeof(SuiteEditorViewModel)
            .GetMethod("BuildManifest", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(vm, null) as PcTest.Contracts.Manifests.TestSuiteManifest;

        // Assert
        manifest.Should().NotBeNull();
        manifest!.TestCases.Should().HaveCount(1);
        var testCaseNode = manifest.TestCases[0];
        testCaseNode.Inputs.Should().NotBeNull();
        testCaseNode.Inputs.Should().HaveCount(3);
        testCaseNode.Inputs!["StringParam"].GetString().Should().Be("TestValue");
        testCaseNode.Inputs["IntParam"].GetInt32().Should().Be(42);
        testCaseNode.Inputs["BoolParam"].GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void TestCaseNodeViewModel_HasParameters_ShouldReturnFalse_WhenNoParameters()
    {
        // Arrange
        var node = new TestCaseNodeViewModel
        {
            NodeId = "node1",
            Ref = "TestCase1"
        };

        // Assert
        node.HasParameters.Should().BeFalse();
    }

    [Fact]
    public void TestCaseNodeViewModel_HasParameters_ShouldReturnTrue_WhenParametersExist()
    {
        // Arrange
        var node = new TestCaseNodeViewModel
        {
            NodeId = "node1",
            Ref = "TestCase1"
        };
        
        node.Parameters.Add(new ParameterViewModel(
            new PcTest.Contracts.Manifests.ParameterDefinition
            {
                Name = "TestParam",
                Type = "string",
                Required = true
            }));

        // Assert
        node.HasParameters.Should().BeTrue();
    }
}

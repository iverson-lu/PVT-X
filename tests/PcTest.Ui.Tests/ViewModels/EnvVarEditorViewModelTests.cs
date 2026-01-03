using FluentAssertions;
using PcTest.Ui.ViewModels;
using Xunit;

namespace PcTest.Ui.Tests.ViewModels;

/// <summary>
/// Tests for EnvVarEditorViewModel covering environment variable editing and validation.
/// </summary>
public class EnvVarEditorViewModelTests
{
    [Fact]
    public void Constructor_ShouldCreateEmptyViewModel()
    {
        // Act
        var vm = new EnvVarEditorViewModel();

        // Assert
        vm.Items.Should().BeEmpty();
        vm.SelectedItem.Should().BeNull();
        vm.HasValidationError.Should().BeFalse();
    }

    [Fact]
    public void LoadFromDictionary_ShouldPopulateItems()
    {
        // Arrange
        var vm = new EnvVarEditorViewModel();
        var env = new Dictionary<string, string>
        {
            ["VAR1"] = "value1",
            ["VAR2"] = "value2"
        };

        // Act
        vm.LoadFromDictionary(env);

        // Assert
        vm.Items.Should().HaveCount(2);
        vm.Items.Should().Contain(i => i.Key == "VAR1" && i.Value == "value1");
        vm.Items.Should().Contain(i => i.Key == "VAR2" && i.Value == "value2");
    }

    [Fact]
    public void LoadFromDictionary_WithNull_ShouldCreateEmptyList()
    {
        // Arrange
        var vm = new EnvVarEditorViewModel();

        // Act
        vm.LoadFromDictionary(null);

        // Assert
        vm.Items.Should().BeEmpty();
    }

    [Fact]
    public void ToDictionary_ShouldReturnCorrectDictionary()
    {
        // Arrange
        var vm = new EnvVarEditorViewModel();
        vm.LoadFromDictionary(new Dictionary<string, string>
        {
            ["KEY1"] = "val1",
            ["KEY2"] = "val2"
        });

        // Act
        var result = vm.ToDictionary();

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result!["KEY1"].Should().Be("val1");
        result["KEY2"].Should().Be("val2");
    }

    [Fact]
    public void ToDictionary_WithEmptyItems_ShouldReturnNull()
    {
        // Arrange
        var vm = new EnvVarEditorViewModel();

        // Act
        var result = vm.ToDictionary();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void AddRow_ShouldAddNewRow()
    {
        // Arrange
        var vm = new EnvVarEditorViewModel();

        // Act
        vm.AddRowCommand.Execute(null);

        // Assert
        vm.Items.Should().HaveCount(1);
        vm.SelectedItem.Should().NotBeNull();
        vm.SelectedItem.Should().Be(vm.Items[0]);
    }

    [Fact]
    public void RemoveRow_ShouldRemoveSelectedRow()
    {
        // Arrange
        var vm = new EnvVarEditorViewModel();
        vm.AddRowCommand.Execute(null);
        var row = vm.Items[0];
        row.Key = "TEST";
        row.Value = "value";

        // Act
        vm.RemoveRowCommand.Execute(null);

        // Assert
        vm.Items.Should().BeEmpty();
    }

    [Fact]
    public void ValidateAll_WithDuplicateKeys_ShouldSetError()
    {
        // Arrange
        var vm = new EnvVarEditorViewModel();
        vm.LoadFromDictionary(new Dictionary<string, string>
        {
            ["KEY1"] = "val1",
            ["KEY2"] = "val2"
        });

        // Act - change KEY2 to KEY1 (duplicate)
        vm.Items[1].Key = "KEY1";
        vm.ValidateAll();

        // Assert
        vm.HasValidationError.Should().BeTrue();
        vm.Items[0].HasError.Should().BeTrue();
        vm.Items[0].Error.Should().Be("Duplicate key");
        vm.Items[1].HasError.Should().BeTrue();
        vm.Items[1].Error.Should().Be("Duplicate key");
    }

    [Fact]
    public void ValidateAll_WithDuplicateKeys_CaseInsensitive_ShouldSetError()
    {
        // Arrange
        var vm = new EnvVarEditorViewModel();
        vm.LoadFromDictionary(new Dictionary<string, string>
        {
            ["KEY1"] = "val1",
            ["key1"] = "val2"  // Different case but same key
        });

        // Act
        vm.ValidateAll();

        // Assert
        vm.HasValidationError.Should().BeTrue();
        vm.Items[0].HasError.Should().BeTrue();
        vm.Items[1].HasError.Should().BeTrue();
    }

    [Fact]
    public void ValidateAll_WithEmptyKey_ShouldSetError()
    {
        // Arrange
        var vm = new EnvVarEditorViewModel();
        vm.AddRowCommand.Execute(null);
        var row = vm.Items[0];
        row.Key = "";
        row.Value = "value";

        // Act
        vm.ValidateAll();

        // Assert
        vm.HasValidationError.Should().BeTrue();
        row.HasError.Should().BeTrue();
        row.Error.Should().Be("Key is required");
    }

    [Fact]
    public void ValidateAll_WithValidKeys_ShouldClearErrors()
    {
        // Arrange
        var vm = new EnvVarEditorViewModel();
        vm.LoadFromDictionary(new Dictionary<string, string>
        {
            ["KEY1"] = "val1",
            ["KEY2"] = "val2"
        });

        // Act
        vm.ValidateAll();

        // Assert
        vm.HasValidationError.Should().BeFalse();
        vm.Items[0].HasError.Should().BeFalse();
        vm.Items[1].HasError.Should().BeFalse();
    }

    [Fact]
    public void ValidateAll_WithEmptyValue_ShouldNotSetError()
    {
        // Arrange
        var vm = new EnvVarEditorViewModel();
        vm.AddRowCommand.Execute(null);
        var row = vm.Items[0];
        row.Key = "KEY1";
        row.Value = "";  // Empty value is allowed

        // Act
        vm.ValidateAll();

        // Assert
        vm.HasValidationError.Should().BeFalse();
        row.HasError.Should().BeFalse();
    }

    [Fact]
    public void OnRowChanged_ShouldBeInvoked_WhenRowAdded()
    {
        // Arrange
        var vm = new EnvVarEditorViewModel();
        var callbackInvoked = false;
        vm.OnRowChanged = () => callbackInvoked = true;

        // Act
        vm.AddRowCommand.Execute(null);

        // Assert
        callbackInvoked.Should().BeTrue();
    }

    [Fact]
    public void OnRowChanged_ShouldBeInvoked_WhenRowRemoved()
    {
        // Arrange
        var vm = new EnvVarEditorViewModel();
        vm.AddRowCommand.Execute(null);
        var callbackInvoked = false;
        vm.OnRowChanged = () => callbackInvoked = true;

        // Act
        vm.RemoveRowCommand.Execute(null);

        // Assert
        callbackInvoked.Should().BeTrue();
    }

    [Fact]
    public void OnRowChanged_ShouldBeInvoked_WhenKeyChanged()
    {
        // Arrange
        var vm = new EnvVarEditorViewModel();
        vm.AddRowCommand.Execute(null);
        var callbackInvoked = false;
        vm.OnRowChanged = () => callbackInvoked = true;

        // Act
        vm.Items[0].Key = "NewKey";

        // Assert
        callbackInvoked.Should().BeTrue();
    }

    [Fact]
    public void ToDictionary_WithWhitespaceKeys_ShouldTrimAndExclude()
    {
        // Arrange
        var vm = new EnvVarEditorViewModel();
        vm.AddRowCommand.Execute(null);
        vm.Items[0].Key = "  KEY1  ";
        vm.Items[0].Value = "value1";
        vm.AddRowCommand.Execute(null);
        vm.Items[1].Key = "   ";  // Whitespace only
        vm.Items[1].Value = "value2";

        // Act
        var result = vm.ToDictionary();

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        result!["KEY1"].Should().Be("value1");
    }
}

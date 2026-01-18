using FluentAssertions;
using PcTest.Contracts.Manifests;
using PcTest.Ui.ViewModels;
using Xunit;

namespace PcTest.Ui.Tests.ViewModels;

/// <summary>
/// Tests for ParameterViewModel covering enum/bool detection, validation, and metadata exposure.
/// </summary>
public class ParameterViewModelTests
{
    [Fact]
    public void Constructor_ShouldInitialize_WithDefaultValue()
    {
        // Arrange
        var definition = new ParameterDefinition
        {
            Name = "TestParam",
            Type = "string",
            Required = false,
            Default = System.Text.Json.JsonDocument.Parse("\"default-value\"").RootElement
        };

        // Act
        var vm = new ParameterViewModel(definition);

        // Assert
        vm.Name.Should().Be("TestParam");
        vm.Type.Should().Be("string");
        vm.Required.Should().BeFalse();
        vm.CurrentValue.Should().Be("default-value");
    }

    [Theory]
    [InlineData("true", "true")]
    [InlineData("false", "false")]
    public void Constructor_ShouldInitialize_BooleanDefault(string jsonValue, string expected)
    {
        // Arrange
        var definition = new ParameterDefinition
        {
            Name = "BoolParam",
            Type = "boolean",
            Required = false,
            Default = System.Text.Json.JsonDocument.Parse(jsonValue).RootElement
        };

        // Act
        var vm = new ParameterViewModel(definition);

        // Assert
        vm.CurrentValue.Should().Be(expected);
    }

    [Fact]
    public void IsEnum_ShouldBeTrue_WhenTypeIsEnum()
    {
        // Arrange
        var definition = new ParameterDefinition
        {
            Name = "Mode",
            Type = "enum",
            Required = false,
            EnumValues = new List<string> { "A", "B", "C" }
        };

        // Act
        var vm = new ParameterViewModel(definition);

        // Assert
        vm.IsEnum.Should().BeTrue();
        vm.EnumValues.Should().NotBeNull();
        vm.EnumValues.Should().HaveCount(3);
        vm.EnumValues.Should().Contain(new[] { "A", "B", "C" });
    }

    [Fact]
    public void IsBoolean_ShouldBeTrue_WhenTypeIsBoolean()
    {
        // Arrange
        var definition = new ParameterDefinition
        {
            Name = "Flag",
            Type = "boolean",
            Required = false
        };

        // Act
        var vm = new ParameterViewModel(definition);

        // Assert
        vm.IsBoolean.Should().BeTrue();
    }

    [Theory]
    [InlineData("checkbox", true)]
    [InlineData("CheckBox", true)]
    [InlineData("CHECKBOX", true)]
    [InlineData("toggle", false)]
    [InlineData(null, false)]
    public void UsePlainCheckBox_ShouldDetect_CheckboxUiHint(string? uiHint, bool expected)
    {
        // Arrange
        var definition = new ParameterDefinition
        {
            Name = "Flag",
            Type = "boolean",
            Required = false,
            UiHint = uiHint
        };

        // Act
        var vm = new ParameterViewModel(definition);

        // Assert
        vm.UsePlainCheckBox.Should().Be(expected);
    }

    [Fact]
    public void ValidateValue_ShouldSetError_WhenRequiredAndEmpty()
    {
        // Arrange
        var definition = new ParameterDefinition
        {
            Name = "Required",
            Type = "string",
            Required = true
        };
        var vm = new ParameterViewModel(definition);

        // Act
        vm.CurrentValue = "";
        vm.ValidateValue();

        // Assert
        vm.HasError.Should().BeTrue();
        vm.ErrorMessage.Should().Contain("Required");
    }

    [Fact]
    public void ValidateValue_ShouldNotSetError_WhenOptionalAndEmpty()
    {
        // Arrange
        var definition = new ParameterDefinition
        {
            Name = "Optional",
            Type = "string",
            Required = false
        };
        var vm = new ParameterViewModel(definition);

        // Act
        vm.CurrentValue = "";
        vm.ValidateValue();

        // Assert
        vm.HasError.Should().BeFalse();
    }

    [Fact]
    public void ValidateValue_ShouldSetError_WhenEnumValueNotInList()
    {
        // Arrange
        var definition = new ParameterDefinition
        {
            Name = "Mode",
            Type = "enum",
            Required = false,
            EnumValues = new List<string> { "A", "B", "C" }
        };
        var vm = new ParameterViewModel(definition);

        // Act
        vm.CurrentValue = "D";
        vm.ValidateValue();

        // Assert
        vm.HasError.Should().BeTrue();
        vm.ErrorMessage.Should().Contain("A, B, C");
    }

    [Fact]
    public void ValidateValue_ShouldNotSetError_WhenEnumValueInList()
    {
        // Arrange
        var definition = new ParameterDefinition
        {
            Name = "Mode",
            Type = "enum",
            Required = false,
            EnumValues = new List<string> { "A", "B", "C" }
        };
        var vm = new ParameterViewModel(definition);

        // Act
        vm.CurrentValue = "B";
        vm.ValidateValue();

        // Assert
        vm.HasError.Should().BeFalse();
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("True")]
    [InlineData("FALSE")]
    [InlineData("1")]
    [InlineData("0")]
    public void ValidateValue_ShouldNotSetError_ForValidBooleanValues(string value)
    {
        // Arrange
        var definition = new ParameterDefinition
        {
            Name = "Flag",
            Type = "boolean",
            Required = false
        };
        var vm = new ParameterViewModel(definition);

        // Act
        vm.CurrentValue = value;
        vm.ValidateValue();

        // Assert
        vm.HasError.Should().BeFalse();
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("no")]
    [InlineData("2")]
    [InlineData("invalid")]
    public void ValidateValue_ShouldSetError_ForInvalidBooleanValues(string value)
    {
        // Arrange
        var definition = new ParameterDefinition
        {
            Name = "Flag",
            Type = "boolean",
            Required = false
        };
        var vm = new ParameterViewModel(definition);

        // Act
        vm.CurrentValue = value;
        vm.ValidateValue();

        // Assert
        vm.HasError.Should().BeTrue();
        vm.ErrorMessage.Should().Contain("true/false");
    }

    [Fact]
    public void CurrentValue_Changed_ShouldTrigger_Validation()
    {
        // Arrange
        var definition = new ParameterDefinition
        {
            Name = "Mode",
            Type = "enum",
            Required = false,
            EnumValues = new List<string> { "A", "B" }
        };
        var vm = new ParameterViewModel(definition);
        vm.CurrentValue = "A";
        vm.HasError.Should().BeFalse();

        // Act - set invalid value
        vm.CurrentValue = "Z";

        // Assert
        vm.HasError.Should().BeTrue();
    }

    [Fact]
    public void IsMultiSelect_ShouldBeTrue_WhenTypeIsJsonWithEnumValues()
    {
        // Arrange
        var definition = new ParameterDefinition
        {
            Name = "Options",
            Type = "json",
            Required = false,
            EnumValues = new List<string> { "OptionA", "OptionB", "OptionC" }
        };

        // Act
        var vm = new ParameterViewModel(definition);

        // Assert
        vm.IsMultiSelect.Should().BeTrue();
        vm.EnumValues.Should().NotBeNull();
        vm.EnumValues.Should().HaveCount(3);
    }

    [Fact]
    public void IsMultiSelect_ShouldBeFalse_WhenTypeIsJsonWithoutEnumValues()
    {
        // Arrange
        var definition = new ParameterDefinition
        {
            Name = "Config",
            Type = "json",
            Required = false,
            EnumValues = null
        };

        // Act
        var vm = new ParameterViewModel(definition);

        // Assert
        vm.IsMultiSelect.Should().BeFalse();
    }

    [Fact]
    public void IsMultiSelect_ShouldBeFalse_WhenTypeIsNotJson()
    {
        // Arrange
        var definition = new ParameterDefinition
        {
            Name = "Mode",
            Type = "enum",
            Required = false,
            EnumValues = new List<string> { "A", "B" }
        };

        // Act
        var vm = new ParameterViewModel(definition);

        // Assert
        vm.IsMultiSelect.Should().BeFalse();
        vm.IsEnum.Should().BeTrue();
    }
}

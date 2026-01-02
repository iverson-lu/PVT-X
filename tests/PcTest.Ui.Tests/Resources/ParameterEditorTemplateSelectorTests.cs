using FluentAssertions;
using PcTest.Contracts.Manifests;
using PcTest.Ui.Resources;
using PcTest.Ui.ViewModels;
using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace PcTest.Ui.Tests.Resources;

/// <summary>
/// Tests for ParameterEditorTemplateSelector covering template selection logic.
/// </summary>
public class ParameterEditorTemplateSelectorTests
{
    private readonly ParameterEditorTemplateSelector _selector;
    private readonly DataTemplate _enumTemplate;
    private readonly DataTemplate _boolToggleTemplate;
    private readonly DataTemplate _boolCheckBoxTemplate;
    private readonly DataTemplate _defaultTemplate;

    public ParameterEditorTemplateSelectorTests()
    {
        _enumTemplate = new DataTemplate();
        _boolToggleTemplate = new DataTemplate();
        _boolCheckBoxTemplate = new DataTemplate();
        _defaultTemplate = new DataTemplate();

        _selector = new ParameterEditorTemplateSelector
        {
            EnumEditorTemplate = _enumTemplate,
            BooleanToggleTemplate = _boolToggleTemplate,
            BooleanCheckBoxTemplate = _boolCheckBoxTemplate,
            DefaultEditorTemplate = _defaultTemplate
        };
    }

    [Fact]
    public void SelectTemplate_ShouldReturn_EnumTemplate_ForEnumType()
    {
        // Arrange
        var definition = new ParameterDefinition
        {
            Name = "Mode",
            Type = "enum",
            Required = false,
            EnumValues = new List<string> { "A", "B", "C" }
        };
        var param = new ParameterViewModel(definition);

        // Act
        var result = _selector.SelectTemplate(param, null!);

        // Assert
        result.Should().Be(_enumTemplate);
    }

    [Fact]
    public void SelectTemplate_ShouldReturn_DefaultTemplate_ForEnumWithoutValues()
    {
        // Arrange - enum type but no EnumValues
        var definition = new ParameterDefinition
        {
            Name = "Mode",
            Type = "enum",
            Required = false,
            EnumValues = null
        };
        var param = new ParameterViewModel(definition);

        // Act
        var result = _selector.SelectTemplate(param, null!);

        // Assert
        result.Should().Be(_defaultTemplate);
    }

    [Fact]
    public void SelectTemplate_ShouldReturn_BoolToggleTemplate_ForBoolean()
    {
        // Arrange
        var definition = new ParameterDefinition
        {
            Name = "Flag",
            Type = "boolean",
            Required = false
        };
        var param = new ParameterViewModel(definition);

        // Act
        var result = _selector.SelectTemplate(param, null!);

        // Assert
        result.Should().Be(_boolToggleTemplate);
    }

    [Fact]
    public void SelectTemplate_ShouldReturn_BoolCheckBoxTemplate_WhenUiHintIsCheckbox()
    {
        // Arrange
        var definition = new ParameterDefinition
        {
            Name = "Flag",
            Type = "boolean",
            Required = false,
            UiHint = "checkbox"
        };
        var param = new ParameterViewModel(definition);

        // Act
        var result = _selector.SelectTemplate(param, null!);

        // Assert
        result.Should().Be(_boolCheckBoxTemplate);
    }

    [Theory]
    [InlineData("string")]
    [InlineData("int")]
    [InlineData("double")]
    [InlineData("path")]
    [InlineData("json")]
    public void SelectTemplate_ShouldReturn_DefaultTemplate_ForOtherTypes(string type)
    {
        // Arrange
        var definition = new ParameterDefinition
        {
            Name = "TestParam",
            Type = type,
            Required = false
        };
        var param = new ParameterViewModel(definition);

        // Act
        var result = _selector.SelectTemplate(param, null!);

        // Assert
        result.Should().Be(_defaultTemplate);
    }

    [Fact]
    public void SelectTemplate_ShouldReturn_DefaultTemplate_ForNullItem()
    {
        // Act
        var result = _selector.SelectTemplate(null!, null!);

        // Assert
        result.Should().Be(_defaultTemplate);
    }

    [Fact]
    public void SelectTemplate_ShouldReturn_DefaultTemplate_ForNonParameterViewModel()
    {
        // Arrange
        var item = new object();

        // Act
        var result = _selector.SelectTemplate(item, null!);

        // Assert
        result.Should().Be(_defaultTemplate);
    }
}

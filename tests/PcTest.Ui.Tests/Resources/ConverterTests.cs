using FluentAssertions;
using PcTest.Ui.Resources;
using Xunit;
using System.Globalization;
using PcTest.Ui.ViewModels;

namespace PcTest.Ui.Tests.Resources;

/// <summary>
/// Tests for new converters added for tree view and detail view selection.
/// </summary>
public class ConverterTests
{
    [Theory]
    [InlineData(true, 1.0)]
    [InlineData(false, 0.0)]
    public void BoolToOpacityConverter_ShouldConvert_Correctly(bool input, double expected)
    {
        // Arrange
        var converter = new BoolToOpacityConverter();

        // Act
        var result = converter.Convert(input, typeof(double), null!, CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(true, 0.0)]
    [InlineData(false, 1.0)]
    public void InverseBoolToOpacityConverter_ShouldConvert_Correctly(bool input, double expected)
    {
        // Arrange
        var converter = new InverseBoolToOpacityConverter();

        // Act
        var result = converter.Convert(input, typeof(double), null!, CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(0.6, true)]
    [InlineData(0.4, false)]
    public void BoolToOpacityConverter_ConvertBack_ShouldUse_HalfThreshold(double opacity, bool expected)
    {
        // Arrange
        var converter = new BoolToOpacityConverter();

        // Act
        var result = converter.ConvertBack(opacity, typeof(bool), null!, CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(DetailView.Summary, "Summary", true)]
    [InlineData(DetailView.Stdout, "Summary", false)]
    [InlineData(DetailView.Stderr, "Stderr", true)]
    [InlineData(DetailView.StructuredEvents, "StructuredEvents", true)]
    [InlineData(DetailView.Artifacts, "Artifacts", true)]
    public void EnumToBooleanConverter_ShouldMatch_Parameter(DetailView value, string parameter, bool expected)
    {
        // Arrange
        var converter = new EnumToBooleanConverter();

        // Act
        var result = converter.Convert(value, typeof(bool), parameter, CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void EnumToBooleanConverter_ConvertBack_ShouldReturn_EnumValue()
    {
        // Arrange
        var converter = new EnumToBooleanConverter();

        // Act
        var result = converter.ConvertBack(true, typeof(DetailView), "Stdout", CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be(DetailView.Stdout);
    }

    [Fact]
    public void EnumToBooleanConverter_ConvertBack_WhenFalse_ShouldReturn_DoNothing()
    {
        // Arrange
        var converter = new EnumToBooleanConverter();

        // Act
        var result = converter.ConvertBack(false, typeof(DetailView), "Stdout", CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be(System.Windows.Data.Binding.DoNothing);
    }

    [Theory]
    [InlineData(DetailView.Summary, "Summary", System.Windows.Visibility.Visible)]
    [InlineData(DetailView.Stdout, "Summary", System.Windows.Visibility.Collapsed)]
    [InlineData(DetailView.Stderr, "Stderr", System.Windows.Visibility.Visible)]
    public void EnumToVisibilityConverter_ShouldMatch_Parameter(DetailView value, string parameter, System.Windows.Visibility expected)
    {
        // Arrange
        var converter = new EnumToVisibilityConverter();

        // Act
        var result = converter.Convert(value, typeof(System.Windows.Visibility), parameter, CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void EnumToVisibilityConverter_WithNullValue_ShouldReturn_Collapsed()
    {
        // Arrange
        var converter = new EnumToVisibilityConverter();

        // Act
        var result = converter.Convert(null!, typeof(System.Windows.Visibility), "Summary", CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be(System.Windows.Visibility.Collapsed);
    }

    [Fact]
    public void EnumToVisibilityConverter_WithNullParameter_ShouldReturn_Collapsed()
    {
        // Arrange
        var converter = new EnumToVisibilityConverter();

        // Act
        var result = converter.Convert(DetailView.Summary, typeof(System.Windows.Visibility), null!, CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be(System.Windows.Visibility.Collapsed);
    }

    [Theory]
    [InlineData(true, "ChevronDown20")]
    [InlineData(false, "ChevronRight20")]
    public void IsExpandedToIconConverter_ShouldReturn_CorrectIcon(bool isExpanded, string expectedIcon)
    {
        // Arrange
        var converter = new IsExpandedToIconConverter();

        // Act
        var result = converter.Convert(isExpanded, typeof(string), null!, CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be(expectedIcon);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("True", true)]
    [InlineData("FALSE", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    public void BooleanStringConverter_ShouldConvert_StringToBool(string input, bool expected)
    {
        // Arrange
        var converter = new BooleanStringConverter();

        // Act
        var result = converter.Convert(input, typeof(bool), null!, CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("invalid", false)]
    [InlineData("yes", false)]
    [InlineData("", false)]
    public void BooleanStringConverter_WithInvalidString_ShouldReturn_False(string input, bool expected)
    {
        // Arrange
        var converter = new BooleanStringConverter();

        // Act
        var result = converter.Convert(input, typeof(bool), null!, CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void BooleanStringConverter_ConvertBack_ShouldReturn_String(bool input, string expected)
    {
        // Arrange
        var converter = new BooleanStringConverter();

        // Act
        var result = converter.ConvertBack(input, typeof(string), null!, CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("[\"OptionA\", \"OptionB\"]", "OptionA", true)]
    [InlineData("[\"OptionA\", \"OptionB\"]", "OptionC", false)]
    [InlineData("[]", "OptionA", false)]
    [InlineData("", "OptionA", false)]
    [InlineData(null, "OptionA", false)]
    public void JsonArrayContainsConverter_ShouldCheck_IfValueInArray(string? jsonArray, string value, bool expected)
    {
        // Arrange
        var converter = new JsonArrayContainsConverter();
        var values = new object[] { jsonArray!, value };

        // Act
        var result = converter.Convert(values, typeof(bool), null!, CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void JsonArrayContainsConverter_WithInvalidJson_ShouldReturnFalse()
    {
        // Arrange
        var converter = new JsonArrayContainsConverter();
        var values = new object[] { "not-valid-json", "OptionA" };

        // Act
        var result = converter.Convert(values, typeof(bool), null!, CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be(false);
    }

    [Fact]
    public void JsonArrayContainsConverter_WithInsufficientValues_ShouldReturnFalse()
    {
        // Arrange
        var converter = new JsonArrayContainsConverter();
        var values = new object[] { "[\"OptionA\"]" }; // Only one value

        // Act
        var result = converter.Convert(values, typeof(bool), null!, CultureInfo.InvariantCulture);

        // Assert
        result.Should().Be(false);
    }
}

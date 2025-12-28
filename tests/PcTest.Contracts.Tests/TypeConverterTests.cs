using System.Text.Json;
using PcTest.Contracts.Validation;
using Xunit;

namespace PcTest.Contracts.Tests;

/// <summary>
/// Tests for type conversion per spec section 6.2 and 7.4.
/// </summary>
public class TypeConverterTests
{
    [Theory]
    [InlineData("42", ParameterTypes.Int, 42)]
    [InlineData("-10", ParameterTypes.Int, -10)]
    [InlineData("0", ParameterTypes.Int, 0)]
    public void ConvertString_Int_ParsesInvariantCulture(string value, string type, int expected)
    {
        var (success, result, error) = TypeConverter.ConvertString(value, type);

        Assert.True(success);
        Assert.Equal(expected, result);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("3.14", ParameterTypes.Double, 3.14)]
    [InlineData("0.5", ParameterTypes.Double, 0.5)]
    [InlineData("-2.5", ParameterTypes.Double, -2.5)]
    public void ConvertString_Double_ParsesInvariantCulture(string value, string type, double expected)
    {
        var (success, result, error) = TypeConverter.ConvertString(value, type);

        Assert.True(success);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("TRUE", true)]
    [InlineData("FALSE", false)]
    [InlineData("True", true)]
    [InlineData("False", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    public void ConvertString_Boolean_ParsesCaseInsensitive(string value, bool expected)
    {
        var (success, result, error) = TypeConverter.ConvertString(value, ParameterTypes.Boolean);

        Assert.True(success);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("no")]
    [InlineData("2")]
    [InlineData("")]
    public void ConvertString_Boolean_InvalidValues_Fail(string value)
    {
        var (success, result, error) = TypeConverter.ConvertString(value, ParameterTypes.Boolean);

        Assert.False(success);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("A", new[] { "A", "B", "C" }, true)]
    [InlineData("D", new[] { "A", "B", "C" }, false)]
    public void ConvertString_Enum_ValidatesAgainstEnumValues(string value, string[] enumValues, bool shouldSucceed)
    {
        var (success, result, error) = TypeConverter.ConvertString(value, ParameterTypes.Enum, enumValues);

        Assert.Equal(shouldSucceed, success);
        if (shouldSucceed)
        {
            Assert.Equal(value, result);
        }
        else
        {
            Assert.Contains("not in allowed values", error);
        }
    }

    [Fact]
    public void ConvertString_IntArray_ParsesJsonArray()
    {
        var (success, result, error) = TypeConverter.ConvertString("[1, 2, 3]", ParameterTypes.IntArray);

        Assert.True(success);
        var array = Assert.IsType<int[]>(result);
        Assert.Equal(new[] { 1, 2, 3 }, array);
    }

    [Fact]
    public void ConvertString_EmptyArray_Accepted()
    {
        var (success, result, error) = TypeConverter.ConvertString("[]", ParameterTypes.IntArray);

        Assert.True(success);
        var array = Assert.IsType<int[]>(result);
        Assert.Empty(array);
    }

    [Fact]
    public void ConvertString_StringArray_ParsesJsonArray()
    {
        var (success, result, error) = TypeConverter.ConvertString("[\"a\", \"b\"]", ParameterTypes.StringArray);

        Assert.True(success);
        var array = Assert.IsType<string[]>(result);
        Assert.Equal(new[] { "a", "b" }, array);
    }

    [Fact]
    public void ConvertString_EnumArray_ValidatesEachElement()
    {
        var enumValues = new[] { "A", "B" };

        var (success, result, error) = TypeConverter.ConvertString("[\"A\", \"B\"]", ParameterTypes.EnumArray, enumValues);
        Assert.True(success);

        var (failSuccess, _, failError) = TypeConverter.ConvertString("[\"A\", \"C\"]", ParameterTypes.EnumArray, enumValues);
        Assert.False(failSuccess);
        Assert.Contains("not in allowed values", failError);
    }

    [Fact]
    public void ConvertJsonElement_Int_FromNumber()
    {
        var json = JsonDocument.Parse("42").RootElement;

        var (success, result, error) = TypeConverter.ConvertJsonElement(json, ParameterTypes.Int);

        Assert.True(success);
        Assert.Equal(42, result);
    }

    [Fact]
    public void ConvertJsonElement_Boolean_FromTrue()
    {
        var json = JsonDocument.Parse("true").RootElement;

        var (success, result, error) = TypeConverter.ConvertJsonElement(json, ParameterTypes.Boolean);

        Assert.True(success);
        Assert.Equal(true, result);
    }

    [Fact]
    public void ConvertJsonElement_Array_FromJsonArray()
    {
        var json = JsonDocument.Parse("[\"A\", \"B\"]").RootElement;

        var (success, result, error) = TypeConverter.ConvertJsonElement(json, ParameterTypes.StringArray);

        Assert.True(success);
        Assert.Equal(new[] { "A", "B" }, result);
    }
}

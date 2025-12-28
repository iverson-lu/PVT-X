using PcTest.Contracts.Validation;
using Xunit;

namespace PcTest.Contracts.Tests;

/// <summary>
/// Tests for identity parsing per spec section 8.1.
/// </summary>
public class IdentityParserTests
{
    [Theory]
    [InlineData("CpuStress@1.0.0", "CpuStress", "1.0.0")]
    [InlineData("My.Test@2.0.0-beta", "My.Test", "2.0.0-beta")]
    [InlineData("Test_Case-1@1.0.0", "Test_Case-1", "1.0.0")]
    [InlineData("a@b", "a", "b")]
    public void Parse_ValidIdentity_ReturnsSuccess(string identity, string expectedId, string expectedVersion)
    {
        var result = IdentityParser.Parse(identity);

        Assert.True(result.Success);
        Assert.Equal(expectedId, result.Id);
        Assert.Equal(expectedVersion, result.Version);
    }

    [Theory]
    [InlineData("  CpuStress@1.0.0  ", "CpuStress", "1.0.0")] // Trimmed
    public void Parse_IdentityWithLeadingTrailingWhitespace_TrimmedAndParsed(string identity, string expectedId, string expectedVersion)
    {
        var result = IdentityParser.Parse(identity);

        Assert.True(result.Success);
        Assert.Equal(expectedId, result.Id);
        Assert.Equal(expectedVersion, result.Version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_NullOrEmpty_ReturnsFail(string? identity)
    {
        var result = IdentityParser.Parse(identity);

        Assert.False(result.Success);
        Assert.Contains("null or empty", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("NoAtSign")]
    [InlineData("Only@")]
    [InlineData("@OnlyVersion")]
    public void Parse_MissingAtOrEmptyParts_ReturnsFail(string identity)
    {
        var result = IdentityParser.Parse(identity);

        Assert.False(result.Success);
    }

    [Theory]
    [InlineData("Two@At@Signs")]
    [InlineData("a@b@c")]
    public void Parse_MultipleAtSigns_ReturnsFail(string identity)
    {
        var result = IdentityParser.Parse(identity);

        Assert.False(result.Success);
        Assert.Contains("exactly one '@'", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Test Case@1.0.0")] // Internal space in id
    [InlineData("TestCase@1.0 .0")] // Internal space in version
    public void Parse_InternalWhitespace_ReturnsFail(string identity)
    {
        var result = IdentityParser.Parse(identity);

        Assert.False(result.Success);
        Assert.Contains("internal whitespace", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Test!Case@1.0.0")] // Invalid char !
    [InlineData("Test/Case@1.0.0")] // Invalid char /
    [InlineData("Test:Case@1.0.0")] // Invalid char :
    public void Parse_InvalidIdCharacters_ReturnsFail(string identity)
    {
        var result = IdentityParser.Parse(identity);

        Assert.False(result.Success);
        Assert.Contains("invalid characters", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_ReturnsCorrectFormat()
    {
        var identity = IdentityParser.Create("MyTest", "1.0.0");

        Assert.Equal("MyTest@1.0.0", identity);
    }

    [Theory]
    [InlineData("Valid@1.0.0", true)]
    [InlineData("Invalid Space@1.0.0", false)]
    [InlineData("", false)]
    public void IsValid_ReturnsExpected(string identity, bool expected)
    {
        var result = IdentityParser.IsValid(identity);

        Assert.Equal(expected, result);
    }
}

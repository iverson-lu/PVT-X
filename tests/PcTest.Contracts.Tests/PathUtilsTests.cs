using PcTest.Contracts.Validation;
using Xunit;

namespace PcTest.Contracts.Tests;

/// <summary>
/// Tests for path utilities per spec section 5.2 and 6.5.
/// Windows case-insensitive comparison and containment checks.
/// </summary>
public class PathUtilsTests
{
    [Fact]
    public void NormalizePath_ResolvesRelativePaths()
    {
        var result = PathUtils.NormalizePath(@"C:\Foo\..\Bar");

        Assert.Equal(@"C:\Bar", result);
    }

    [Fact]
    public void NormalizePath_RemovesTrailingSeparator()
    {
        var result = PathUtils.NormalizePath(@"C:\Foo\Bar\");

        Assert.Equal(@"C:\Foo\Bar", result);
    }

    [Fact]
    public void NormalizePath_PreservesRootTrailingSeparator()
    {
        var result = PathUtils.NormalizePath(@"C:\");

        Assert.Equal(@"C:\", result);
    }

    [Theory]
    [InlineData(@"C:\Parent\Child", @"C:\Parent", true)]
    [InlineData(@"C:\Parent\Child\Deep", @"C:\Parent", true)]
    [InlineData(@"C:\Parent", @"C:\Parent", true)] // Equal paths
    [InlineData(@"C:\Other", @"C:\Parent", false)]
    [InlineData(@"C:\ParentExtra", @"C:\Parent", false)] // Not a true child
    public void IsContainedIn_ChecksContainment(string child, string parent, bool expected)
    {
        var result = PathUtils.IsContainedIn(child, parent);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(@"C:\PARENT\CHILD", @"C:\parent", true)] // Case insensitive
    [InlineData(@"c:\parent\child", @"C:\Parent", true)]
    public void IsContainedIn_CaseInsensitive(string child, string parent, bool expected)
    {
        var result = PathUtils.IsContainedIn(child, parent);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Combine_CombinesAndNormalizes()
    {
        var result = PathUtils.Combine(@"C:\Base", "relative/path");

        Assert.Equal(@"C:\Base\relative\path", result);
    }

    [Theory]
    [InlineData(@"C:\Foo\Bar", @"c:\foo\bar", true)]
    [InlineData(@"C:\Foo\Bar", @"C:\FOO\BAR", true)]
    [InlineData(@"C:\Foo\Bar", @"C:\Foo\Baz", false)]
    public void PathEquals_CaseInsensitive(string path1, string path2, bool expected)
    {
        var result = PathUtils.PathEquals(path1, path2);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void MakeRelative_ReturnsRelativePath()
    {
        var result = PathUtils.MakeRelative(@"C:\Root\Sub\File.txt", @"C:\Root");

        Assert.Equal(@"Sub\File.txt", result);
    }

    [Theory]
    [InlineData("ValidName", "ValidName")]
    [InlineData("Name With Spaces", "Name With Spaces")]
    [InlineData("CON", "_CON")] // Reserved name
    [InlineData("LPT1", "_LPT1")] // Reserved name
    public void SanitizeFolderName_HandlesReservedNames(string input, string expected)
    {
        var result = PathUtils.SanitizeFolderName(input);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void SanitizeFolderName_RemovesInvalidChars()
    {
        var result = PathUtils.SanitizeFolderName("File<>:Name");

        Assert.DoesNotContain("<", result);
        Assert.DoesNotContain(">", result);
        Assert.DoesNotContain(":", result);
    }
}

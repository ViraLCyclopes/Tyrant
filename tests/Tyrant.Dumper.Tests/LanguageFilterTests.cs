using Tyrant.Dumper.Serialization;

namespace Tyrant.Dumper.Tests;

public class LanguageFilterTests
{
    [Theory]
    [InlineData("en", "English", true)]
    [InlineData("zh-CN", "Chinese (Simplified)", true)]
    [InlineData("", "Description", false)] // I2's description column, not a language
    [InlineData(null, "Description", false)]
    [InlineData("", "", false)]
    [InlineData("", "Klingon", true)] // a language without a code is still a language
    public void Only_real_languages_are_dumped(string? code, string? name, bool expected)
    {
        Assert.Equal(expected, LanguageFilter.IsLanguage(code, name));
    }
}

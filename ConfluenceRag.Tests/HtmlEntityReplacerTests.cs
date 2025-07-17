namespace ConfluenceRag.Tests;

public class HtmlEntityReplacerTests
{
    [Fact]
    public void ReplaceHtmlEntitiesWithUtf8_ShouldPreserveXmlEntities()
    {
        // Arrange
        var input = "&amp; &lt; &gt; &quot; &apos;";
        var expected = "&amp; &lt; &gt; &quot; &apos;";

        // Act
        var result = HtmlEntityReplacer.ReplaceHtmlEntitiesWithUtf8(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReplaceHtmlEntitiesWithUtf8_ShouldConvertSpecialCharacters()
    {
        // Arrange
        var input = "&rsquo; &lsquo; &ldquo; &rdquo; &mdash; &ndash; &hellip; &nbsp;";
        var expected = "' ' \" \" — – …  ";

        // Act
        var result = HtmlEntityReplacer.ReplaceHtmlEntitiesWithUtf8(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReplaceHtmlEntitiesWithUtf8_ShouldConvertAccentedCharacters()
    {
        // Arrange
        var input = "&eacute; &egrave; &aacute; &ccedil; &ntilde;";
        var expected = "é è á ç ñ";

        // Act
        var result = HtmlEntityReplacer.ReplaceHtmlEntitiesWithUtf8(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReplaceHtmlEntitiesWithUtf8_ShouldRemoveUnknownEntities()
    {
        // Arrange
        var input = "&unknownentity; some text &anotherbadone;";
        var expected = " some text ";

        // Act
        var result = HtmlEntityReplacer.ReplaceHtmlEntitiesWithUtf8(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReplaceHtmlEntitiesWithUtf8_ShouldHandleNumericEntities()
    {
        // Arrange
        var input = "&#65; &#x41; &#8217;";
        
        // Act
        var result = HtmlEntityReplacer.ReplaceHtmlEntitiesWithUtf8(input);

        // Assert
        Assert.Contains("A", result);
        Assert.Contains("’", result); // Unicode right single quotation mark (U+2019)
    }

    [Fact]
    public void ReplaceHtmlEntitiesWithUtf8_ShouldHandleEmptyString()
    {
        // Arrange
        var input = "";
        var expected = "";

        // Act
        var result = HtmlEntityReplacer.ReplaceHtmlEntitiesWithUtf8(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReplaceHtmlEntitiesWithUtf8_ShouldHandleStringWithoutEntities()
    {
        // Arrange
        var input = "This is just plain text without any entities.";
        var expected = "This is just plain text without any entities.";

        // Act
        var result = HtmlEntityReplacer.ReplaceHtmlEntitiesWithUtf8(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReplaceHtmlEntitiesWithUtf8_ShouldConvertMathAndSymbols()
    {
        // Arrange
        var input = "&deg; &micro; &times; &divide; &plusmn;";
        var expected = "° µ × ÷ ±";

        // Act
        var result = HtmlEntityReplacer.ReplaceHtmlEntitiesWithUtf8(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReplaceHtmlEntitiesWithUtf8_ShouldConvertGreekLetters()
    {
        // Arrange
        var input = "&alpha; &beta; &gamma; &pi; &sigma;";
        var expected = "α β γ π σ";

        // Act
        var result = HtmlEntityReplacer.ReplaceHtmlEntitiesWithUtf8(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReplaceHtmlEntitiesWithUtf8_ShouldConvertFractions()
    {
        // Arrange
        var input = "&frac12; &frac14; &frac34;";
        var expected = "½ ¼ ¾";

        // Act
        var result = HtmlEntityReplacer.ReplaceHtmlEntitiesWithUtf8(input);

        // Assert
        Assert.Equal(expected, result);
    }
}
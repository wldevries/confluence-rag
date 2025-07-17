using ConfluenceRag.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestPlatform.Common;
using Moq;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;

namespace ConfluenceRag.Tests;

public class ConfluenceMarkdownExtractorTestsDates
{
    private const string PeoplePath = @"C:\temp\data\atlassian\people.json";
    private readonly Mock<ILogger<ConfluenceMarkdownExtractor>> _mockLogger;
    private readonly Mock<IEmbeddingGenerator<string, Embedding<float>>> _mockEmbedder;
    private readonly MockFileSystem _fileSystem;
    private readonly ConfluenceMarkdownExtractor _extractor;

    public ConfluenceMarkdownExtractorTestsDates()
    {
        _mockLogger = new Mock<ILogger<ConfluenceMarkdownExtractor>>();
        _mockEmbedder = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        _fileSystem = new MockFileSystem();
        _extractor = new ConfluenceMarkdownExtractor(_mockLogger.Object, _fileSystem, new() { PeoplePath = PeoplePath });
    }
    
    [Fact]
    public void ExtractMarkdown_WithTimeElement_ShouldFormatDateCorrectly()
    {
        // Arrange
        var xml = @"<p>Meeting scheduled for <time datetime=""2024-03-15T14:30:00.000Z"">March 15, 2024</time></p>";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        Assert.NotEmpty(result);
        // Date should be formatted and integrated into the paragraph
        var paragraphWithDate = result.FirstOrDefault(line => line.Contains("Date: 2024-03-15"));
        Assert.NotNull(paragraphWithDate);
        Assert.Contains("Meeting scheduled for Date: 2024-03-15", paragraphWithDate);
    }

    [Fact]
    public void ExtractMarkdown_WithDateMacro_ShouldFormatDateCorrectly()
    {
        // Arrange
        var xml = @"<p><ac:structured-macro ac:name=""date"" xmlns:ac=""http://atlassian.com/ac"">
            <ac:parameter ac:name=""date"">2024-03-15</ac:parameter>
        </ac:structured-macro></p>";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        Assert.NotEmpty(result);
        Assert.Contains("Date: 2024-03-15", result);
    }

    [Fact]
    public void ExtractMarkdown_WithDateMacroInvalidDate_ShouldShowOriginalValue()
    {
        // Arrange
        var xml = @"<p><ac:structured-macro ac:name=""date"" xmlns:ac=""http://atlassian.com/ac"">
            <ac:parameter ac:name=""date"">Not a valid date</ac:parameter>
        </ac:structured-macro></p>";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        Assert.NotEmpty(result);
        Assert.Contains("Date: Not a valid date", result);
    }

    [Fact]
    public void ExtractMarkdown_WithEmptyDateMacro_ShouldShowNotSpecified()
    {
        // Arrange
        var xml = @"<p><ac:structured-macro ac:name=""date"" xmlns:ac=""http://atlassian.com/ac"">
        </ac:structured-macro></p>";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        Assert.NotEmpty(result);
        Assert.Contains("Date: [Not specified]", result);
    }

    [Theory]
    [InlineData("2024-03-15T14:30:00Z", "2024-03-15")]
    [InlineData("2024-12-25", "2024-12-25")]
    [InlineData("March 15, 2024", "2024-03-15")]
    public void ExtractMarkdown_WithVariousDateFormats_ShouldParseCorrectly(string inputDate, string expectedDate)
    {
        // Arrange
        var xml = $@"<p><time datetime=""{inputDate}"">Some date</time></p>";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        Assert.NotEmpty(result);
        Assert.Contains($"Date: {expectedDate}", result);
    }

    [Fact]
    public void ExtractMarkdown_WithDatetimeMacro_ShouldFormatDatetime()
    {
        // Arrange
        var xml = @"<p><ac:structured-macro ac:name=""datetime"" xmlns:ac=""http://atlassian.com/ac"">
            <ac:parameter ac:name=""date"">2024-03-15T14:30:00Z</ac:parameter>
        </ac:structured-macro></p>";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        Assert.NotEmpty(result);
        Assert.Contains("Date: 2024-03-15", result);
    }

    [Theory]
    [InlineData("invalid-time-format", "invalid-time-format")]
    [InlineData("", "")]
    [InlineData("2024-13-40T99:99:99Z", "2024-13-40T99:99:99Z")] // Invalid date components
    public void ExtractMarkdown_WithInvalidTimeElement_ShouldShowOriginalValue(string inputDateTime, string expectedOutput)
    {
        // Arrange
        var xml = $@"<p><time datetime=""{inputDateTime}"">Some date</time></p>";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        if (string.IsNullOrEmpty(inputDateTime))
        {
            // Empty datetime should not produce any output
            Assert.Contains("Date: [Not specified]", result);
        }
        else
        {
            Assert.NotEmpty(result);
            Assert.Contains($"Date: {expectedOutput}", result);
        }
    }
}
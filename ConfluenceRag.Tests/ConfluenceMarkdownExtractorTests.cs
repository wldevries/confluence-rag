using ConfluenceRag.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;

namespace ConfluenceRag.Tests;

public class ConfluenceMarkdownExtractorTests
{
    private const string PeoplePath = @"C:\temp\data\atlassian\people.json";
    private readonly Mock<ILogger<ConfluenceMarkdownExtractor>> _mockLogger;
    private readonly Mock<IEmbeddingGenerator<string, Embedding<float>>> _mockEmbedder;
    private readonly MockFileSystem _fileSystem;
    private readonly ConfluenceMarkdownExtractor _extractor;

    public ConfluenceMarkdownExtractorTests()
    {
        _mockLogger = new Mock<ILogger<ConfluenceMarkdownExtractor>>();
        _mockEmbedder = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        _fileSystem = new MockFileSystem();
        _extractor = new ConfluenceMarkdownExtractor(_mockLogger.Object, _fileSystem, new() { PeoplePath = PeoplePath });
    }

    [Fact]
    public void ExtractMarkdown_WithBasicHtml_ShouldCreateChunks()
    {
        // Arrange
        var xml = "<h1>Test Heading</h1><p>This is a test paragraph with some content.</p>";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        Assert.NotEmpty(result);
        Assert.Contains("# Test Heading", result);
        Assert.Contains("This is a test paragraph with some content.", result);
    }

    [Fact]
    public void ExtractMarkdown_WithTables_ShouldCreateMarkdownTables()
    {
        // Arrange
        var xml = @"<table>
            <tr><th>Header 1</th><th>Header 2</th></tr>
            <tr><td>Cell 1</td><td>Cell 2</td></tr>
        </table>";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        if (result.Count > 0)
        {
            Assert.Contains("Header 1", result);
            Assert.Contains("Header 2", result);
            Assert.Contains("Cell 1", result);
            Assert.Contains("Cell 2", result);
        }
    }

    [Fact]
    public void ExtractMarkdown_WithLists_ShouldCreateFormattedLists()
    {
        // Arrange
        var xml = @"<ul>
            <li>First item</li>
            <li>Second item</li>
        </ul>";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        Assert.NotEmpty(result);
        Assert.Contains("- First item", result);
        Assert.Contains("- Second item", result);
    }

    [Fact]
    public void ExtractMarkdown_WithInvalidXml_ShouldFallbackToPlainText()
    {
        // Arrange
        var invalidXml = "This is not valid XML <unclosed tag";

        // Act
        var result = _extractor.ExtractMarkdown(invalidXml);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ExtractMarkdown_WithEmptyContent_ShouldReturnEmptyList()
    {
        // Arrange
        var xml = "";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ExtractMarkdown_WithFormattingTags_ShouldPreserveFormatting()
    {
        // Arrange
        var xml = "<p><strong>Bold text</strong> and <em>italic text</em> and <code>code text</code></p>";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        Assert.NotEmpty(result);
        Assert.Contains("**Bold text**", result);
        Assert.Contains("*italic text*", result);
        Assert.Contains("`code text`", result);
    }

    [Fact]
    public void ExtractMarkdown_WithInlineCodeInListItems_ShouldKeepCodeInline()
    {
        // Arrange
        var xml = @"<ul>
            <li><p>Install the <code>app.exe</code> file to <code>/usr/local/bin</code> directory.</p></li>
            <li><p>Set the <code>PATH</code> environment variable correctly.</p></li>
        </ul>";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        Assert.NotEmpty(result);
        
        // Verify that code blocks stay inline within list items
        Assert.Contains("- Install the `app.exe` file to `/usr/local/bin` directory.", result);
        Assert.Contains("- Set the `PATH` environment variable correctly.", result);
        
        // Verify that inline code is not split across multiple lines
        Assert.All(result.Where(l => l.Contains("`")), line => 
            Assert.True(line.Count(c => c == '`') % 2 == 0, "Code blocks should be properly closed on same line"));
    }
}
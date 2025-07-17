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
        // All formatting should be preserved in the combined paragraph
        var combinedText = result.FirstOrDefault(line => line.Contains("Bold text"));
        Assert.NotNull(combinedText);
        Assert.Contains("**Bold text**", combinedText);
        Assert.Contains("*italic text*", combinedText);
        Assert.Contains("`code text`", combinedText);
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

    [Fact]
    public void ExtractMarkdown_WithStructuredCodeMacro_ShouldIncludeLargeCodeBlocks()
    {
        // Arrange
        var xml = @"<p>Configuration example:</p>
        <ac:structured-macro ac:name=""code"" ac:schema-version=""1"">
            <ac:parameter ac:name=""language"">json</ac:parameter>
            <ac:plain-text-body><![CDATA[{
  ""name"": ""test-app"",
  ""version"": ""1.0.0"",
  ""dependencies"": {
    ""express"": ""^4.18.0"",
    ""lodash"": ""^4.17.21""
  }
}]]></ac:plain-text-body>
        </ac:structured-macro>
        <p>End of example.</p>";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        Assert.NotEmpty(result);
        Assert.Contains("Configuration example:", result);
        Assert.Contains("End of example.", result);
        
        // Verify that the code block is properly formatted
        Assert.Contains("```json", result);
        Assert.Contains("```", result.Where(l => l == "```"));
        Assert.Contains("  \"name\": \"test-app\",", result);
        Assert.Contains("  \"version\": \"1.0.0\",", result);
        Assert.Contains("  \"dependencies\": {", result);
        
        // Verify code block structure
        var codeStartIndex = result.ToList().FindIndex(l => l == "```json");
        var codeEndIndex = result.ToList().FindIndex(codeStartIndex + 1, l => l == "```");
        Assert.True(codeStartIndex >= 0, "Code block should start with ```json");
        Assert.True(codeEndIndex > codeStartIndex, "Code block should end with ```");
        Assert.True(codeEndIndex - codeStartIndex > 2, "Code block should contain actual code content");
    }

    [Fact]
    public void ExtractMarkdown_WithInlineElementsInParagraph_ShouldKeepInlineElementsInline()
    {
        // Arrange - This reproduces the issue with inline elements being split across lines
        var xml = @"<p>Simple <em>italic</em> text.</p>";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        Assert.NotEmpty(result);
        
        // The entire paragraph should be on one line with inline formatting preserved
        Assert.Contains("Simple *italic* text.", result);
        
        // Verify that the italic text is not on a separate line
        Assert.DoesNotContain("*italic*", result.Where(line => line.Trim() == "*italic*"));
    }

    [Fact]
    public void ExtractMarkdown_WithSimpleText_ShouldWork()
    {
        // Arrange
        var xml = @"<p>Simple text without formatting.</p>";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        Assert.NotEmpty(result);
        var resultList = result.ToList();
        Assert.Contains("Simple text without formatting.", result);
        
        // Paragraph case should produce 1 line of content (blank line is filtered out)
        Assert.True(resultList.Count == 1, $"Expected 1 line, got {resultList.Count}: {string.Join(" | ", resultList.Select(r => $"'{r}'"))}");
    }

    [Fact]
    public void ExtractMarkdown_WithInlineElementAlone_ShouldWork()
    {
        // Arrange
        var xml = @"<em>italic</em>";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        Assert.NotEmpty(result);
        var resultList = result.ToList();
        Assert.Contains("*italic*", result);
        Assert.True(resultList.Count == 1, $"Expected 1 line, got {resultList.Count}: {string.Join(" | ", resultList.Select(r => $"'{r}'"))}");
    }

    [Fact]
    public void ExtractMarkdown_WithStrongElementAlone_ShouldWork()
    {
        // Arrange
        var xml = @"<strong>bold</strong>";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        Assert.NotEmpty(result);
        var resultList = result.ToList();
        Assert.Contains("**bold**", result);
        Assert.True(resultList.Count == 1, $"Expected 1 line, got {resultList.Count}: {string.Join(" | ", resultList.Select(r => $"'{r}'"))}");
    }

    [Fact]
    public void ExtractMarkdown_WithMultipleInlineElementsInParagraph_ShouldKeepAllInlineElementsInline()
    {
        // Arrange
        var xml = @"<p>This document contains <strong>important information</strong> about our <em>new procedures</em> and includes <code>system commands</code> for reference.</p>";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        Assert.NotEmpty(result);
        
        // The entire paragraph should be on one line with all inline formatting preserved
        Assert.Contains("This document contains **important information** about our *new procedures* and includes `system commands` for reference.", result);
        
        // Verify that none of the inline elements are on separate lines
        Assert.DoesNotContain("**important information**", result.Where(line => line.Trim() == "**important information**"));
        Assert.DoesNotContain("*new procedures*", result.Where(line => line.Trim() == "*new procedures*"));
        Assert.DoesNotContain("`system commands`", result.Where(line => line.Trim() == "`system commands`"));
    }

    [Fact]
    public void ExtractMarkdown_WithRootLevelRoadmapMacro_ShouldOmitRoadmapContent()
    {
        // Arrange
        var xml = @"<p>Before roadmap</p>
        <ac:structured-macro ac:name=""roadmap"" ac:schema-version=""1"" xmlns:ac=""http://atlassian.com/ac"">
            <ac:parameter ac:name=""roadmapId"">12345</ac:parameter>
        </ac:structured-macro>
        <p>After roadmap</p>";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        Assert.NotEmpty(result);
        Assert.Contains("Before roadmap", result);
        Assert.Contains("After roadmap", result);
        Assert.Contains("[Roadmap macro omitted]", result);
    }

    [Fact]
    public void ExtractMarkdown_WithRootLevelRoadmapPlannerMacro_ShouldOmitRoadmapContent()
    {
        // Arrange
        var xml = @"<p>Before roadmap planner</p>
        <ac:structured-macro ac:name=""roadmap-planner"" ac:schema-version=""1"" xmlns:ac=""http://atlassian.com/ac"">
            <ac:parameter ac:name=""planId"">67890</ac:parameter>
        </ac:structured-macro>
        <p>After roadmap planner</p>";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        Assert.NotEmpty(result);
        Assert.Contains("Before roadmap planner", result);
        Assert.Contains("After roadmap planner", result);
        Assert.Contains("[Roadmap macro omitted]", result);
    }

    [Fact]
    public void ExtractMarkdown_WithNestedRoadmapMacroInDiv_ShouldOmitRoadmapContent()
    {
        // Arrange
        var xml = @"<div>
            <p>Container content</p>
            <ac:structured-macro ac:name=""roadmap"" ac:schema-version=""1"" xmlns:ac=""http://atlassian.com/ac"">
                <ac:parameter ac:name=""roadmapId"">nested123</ac:parameter>
            </ac:structured-macro>
            <p>More container content</p>
        </div>";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        Assert.NotEmpty(result);
        var fullText = string.Join(" ", result);
        Assert.Contains("Container content", fullText);
        Assert.Contains("More container content", fullText);
        Assert.Contains("[Roadmap macro omitted]", fullText);
    }

    [Fact]
    public void ExtractMarkdown_WithRoadmapMacroInTableCell_ShouldOmitRoadmapContent()
    {
        // Arrange
        var xml = @"<table>
            <tr>
                <td>Regular cell</td>
                <td>
                    <ac:structured-macro ac:name=""roadmap-planner"" ac:schema-version=""1"" xmlns:ac=""http://atlassian.com/ac"">
                        <ac:parameter ac:name=""planId"">table123</ac:parameter>
                    </ac:structured-macro>
                </td>
            </tr>
        </table>";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        Assert.NotEmpty(result);
        var fullText = string.Join(" ", result);
        Assert.Contains("Regular cell", fullText);
        Assert.Contains("[Roadmap macro omitted]", fullText);
        // Should have table structure
        Assert.Contains("|", result.First());
    }
}
using ConfluenceRag.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using System.IO.Abstractions.TestingHelpers;

namespace ConfluenceRag.Tests;

public class ConfluenceMarkdownExtractorTablesTests
{
    private const string PeoplePath = @"C:\temp\data\atlassian\people.json";
    private readonly Mock<ILogger<ConfluenceMarkdownExtractor>> _mockLogger;
    private readonly Mock<IEmbeddingGenerator<string, Embedding<float>>> _mockEmbedder;
    private readonly MockFileSystem _fileSystem;
    private readonly ConfluenceMarkdownExtractor _extractor;

    public ConfluenceMarkdownExtractorTablesTests()
    {
        _mockLogger = new Mock<ILogger<ConfluenceMarkdownExtractor>>();
        _mockEmbedder = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        _fileSystem = new MockFileSystem();
        _extractor = new ConfluenceMarkdownExtractor(_mockLogger.Object, _fileSystem, new() { PeoplePath = PeoplePath });
    }

    [Fact]
    public void ExtractMarkdown_WithBasicTable_ShouldCreateMarkdownTable()
    {
        // Arrange
        var xml = @"<table>
            <tr><th>Header 1</th><th>Header 2</th></tr>
            <tr><td>Cell 1</td><td>Cell 2</td></tr>
        </table>";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        Assert.NotEmpty(result);
        Assert.Contains("| Header 1 | Header 2 |", result);
        Assert.Contains("| --- | --- |", result);
        Assert.Contains("| Cell 1 | Cell 2 |", result);
    }

    [Fact]
    public void ExtractMarkdown_WithTableWithoutHeaders_ShouldSynthesizeHeaders()
    {
        // Arrange
        var xml = @"<table>
            <tr><td>Cell 1</td><td>Cell 2</td></tr>
            <tr><td>Cell 3</td><td>Cell 4</td></tr>
        </table>";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        Assert.NotEmpty(result);
        Assert.Contains("| Column 1 | Column 2 |", result);
        Assert.Contains("| --- | --- |", result);
        Assert.Contains("| Cell 1 | Cell 2 |", result);
        Assert.Contains("| Cell 3 | Cell 4 |", result);
    }

    [Fact]
    public void ExtractMarkdown_WithTableWithTbody_ShouldProcessCorrectly()
    {
        // Arrange
        var xml = @"<table>
            <tbody>
                <tr><th>Name</th><th>Age</th></tr>
                <tr><td>John</td><td>30</td></tr>
                <tr><td>Jane</td><td>25</td></tr>
            </tbody>
        </table>";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        Assert.NotEmpty(result);
        Assert.Contains("| Name | Age |", result);
        Assert.Contains("| --- | --- |", result);
        Assert.Contains("| John | 30 |", result);
        Assert.Contains("| Jane | 25 |", result);
    }

    [Fact]
    public void ExtractMarkdown_WithTableWithColgroup_ShouldIgnoreColgroup()
    {
        // Arrange
        var xml = @"<table>
            <colgroup>
                <col style=""width: 50%;"" />
                <col style=""width: 50%;"" />
            </colgroup>
            <tbody>
                <tr><th>Product</th><th>Price</th></tr>
                <tr><td>Widget</td><td>$10</td></tr>
            </tbody>
        </table>";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        Assert.NotEmpty(result);
        Assert.Contains("| Product | Price |", result);
        Assert.Contains("| --- | --- |", result);
        Assert.Contains("| Widget | $10 |", result);
    }

    [Fact]
    public void ExtractMarkdown_WithComplexTableCells_ShouldHandleNestedContent()
    {
        // Arrange
        var xml = @"<table>
            <tbody>
                <tr>
                    <th><p><strong>Property</strong></p></th>
                    <th><p><strong>Value</strong></p></th>
                    <th><p><strong>Notes</strong></p></th>
                </tr>
                <tr>
                    <td><p><code>AppVersion</code></p></td>
                    <td><p><code>&quot;2.1&quot;</code></p></td>
                    <td><p>Application version number. Updated with each release cycle.</p></td>
                </tr>
                <tr>
                    <td><p><code>Environment</code></p></td>
                    <td><p><code>&quot;Production&quot;</code></p></td>
                    <td>
                        <p>Deployment environment. Can be Production, Staging, or Development like <em>AWS EC2.</em></p>
                        <ac:structured-macro ac:name=""note"" ac:schema-version=""1"">
                            <ac:rich-text-body>
                                <p>This value must match the configuration in the deployment pipeline.</p>
                            </ac:rich-text-body>
                        </ac:structured-macro>
                    </td>
                </tr>
            </tbody>
        </table>";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        Assert.NotEmpty(result);
        
        // Check markdown table structure
        Assert.Contains("| **Property** | **Value** | **Notes** |", result);
        Assert.Contains("| --- | --- | --- |", result);
        
        // Check basic cell content (looking for partial matches since cells contain more content)
        Assert.True(result.Any(l => l.Contains("| `AppVersion` | `\"2.1\"` |")), "AppVersion row should be present");
        Assert.True(result.Any(l => l.Contains("| `Environment` | `\"Production\"` |")), "Environment row should be present");
        
        // Check that complex cell content is preserved
        Assert.True(result.Any(l => l.Contains("*AWS EC2.*")), "Emphasized text should be preserved");
        Assert.True(result.Any(l => l.Contains("> **NOTE:**")), "Note macro should be converted to blockquote");
    }

    [Fact]
    public void ExtractMarkdown_WithTableContainingCodeBlocks_ShouldPreserveCodeBlocks()
    {
        // Arrange
        var xml = @"<table>
            <tbody>
                <tr>
                    <th><p><strong>Setting</strong></p></th>
                    <th><p><strong>Configuration</strong></p></th>
                </tr>
                <tr>
                    <td><p><code>DatabaseConfig</code></p></td>
                    <td>
                        <ac:structured-macro ac:name=""code"" ac:schema-version=""1"">
                            <ac:parameter ac:name=""language"">json</ac:parameter>
                            <ac:plain-text-body><![CDATA[{
  ""host"": ""localhost"",
  ""port"": ""5432""
}]]></ac:plain-text-body>
                        </ac:structured-macro>
                    </td>
                </tr>
            </tbody>
        </table>";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        Assert.NotEmpty(result);
        
        // Check code block within table cell
        Assert.True(result.Any(l => l.Contains("```json")), "Code block should be preserved in table cells");
        Assert.True(result.Any(l => l.Contains("\"host\": \"localhost\"")), "Code block content should be preserved");
        Assert.True(result.Any(l => l.Contains("\"port\": \"5432\"")), "Code block content should be preserved");
    }

    [Fact]
    public void ExtractMarkdown_WithMultilineTableCells_ShouldHandleMultipleLines()
    {
        // Arrange
        var xml = @"<table>
            <tbody>
                <tr>
                    <th><p><strong>Component</strong></p></th>
                    <th><p><strong>Details</strong></p></th>
                </tr>
                <tr>
                    <td><p><code>UserService</code></p></td>
                    <td>
                        <p>This service handles user authentication and authorization.</p>
                        <p>It also manages user profile data and session management.</p>
                    </td>
                </tr>
            </tbody>
        </table>";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        Assert.NotEmpty(result);
        
        // Check that multi-line content is preserved
        Assert.True(result.Any(l => l.Contains("authentication and authorization")), "First paragraph should be preserved");
        Assert.True(result.Any(l => l.Contains("profile data and session")), "Second paragraph should be preserved");
        
        // Check for proper multi-line handling with <br> tags
        var multiLineCell = result.FirstOrDefault(l => l.Contains("| `UserService` |"));
        Assert.NotNull(multiLineCell);
        
        // The multi-line content should be properly formatted
        Assert.True(result.Any(l => l.Contains("authentication and authorization") && l.Contains("profile data and session")), 
            "Multi-line content should be in the same table cell");
    }

    [Fact]
    public void ExtractMarkdown_WithTableContainingInfoMacros_ShouldPreserveInfoMacros()
    {
        // Arrange
        var xml = @"<table>
            <tbody>
                <tr>
                    <th><p><strong>Field</strong></p></th>
                    <th><p><strong>Notes</strong></p></th>
                </tr>
                <tr>
                    <td><p><code>ImportantField</code></p></td>
                    <td>
                        <p>This field is important for the system.</p>
                        <ac:structured-macro ac:name=""info"" ac:schema-version=""1"">
                            <ac:rich-text-body>
                                <p>Both Latitude and Longitude are strings, not numbers!</p>
                            </ac:rich-text-body>
                        </ac:structured-macro>
                    </td>
                </tr>
            </tbody>
        </table>";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        Assert.NotEmpty(result);
        
        // Check info macro within table cell
        Assert.True(result.Any(l => l.Contains("> **INFO:**")), "Info macro should be converted to blockquote");
        Assert.True(result.Any(l => l.Contains("strings, not numbers!")), "Info macro content should be preserved");
    }

    [Fact]
    public void ExtractMarkdown_WithTableStructure_ShouldHaveConsistentColumnCount()
    {
        // Arrange
        var xml = @"<table>
            <tbody>
                <tr>
                    <th><p><strong>Col1</strong></p></th>
                    <th><p><strong>Col2</strong></p></th>
                    <th><p><strong>Col3</strong></p></th>
                </tr>
                <tr>
                    <td><p>A1</p></td>
                    <td><p>A2</p></td>
                    <td><p>A3</p></td>
                </tr>
                <tr>
                    <td><p>B1</p></td>
                    <td><p>B2</p></td>
                    <td><p>B3</p></td>
                </tr>
            </tbody>
        </table>";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        Assert.NotEmpty(result);
        
        // Check that table structure is complete
        var tableLines = result.Where(l => l.StartsWith("| ")).ToList();
        Assert.True(tableLines.Count >= 4, "Should have at least header + separator + 2 data rows");
        
        // Verify all table rows have consistent column count
        Assert.All(tableLines.Where(l => !l.Contains("---")), line => 
        {
            var columnCount = line.Count(c => c == '|') - 1; // Subtract 1 for the trailing |
            Assert.Equal(3, columnCount);
        });
        
        // Check specific content
        Assert.Contains("| **Col1** | **Col2** | **Col3** |", result);
        Assert.Contains("| A1 | A2 | A3 |", result);
        Assert.Contains("| B1 | B2 | B3 |", result);
    }

    [Fact]
    public void ExtractMarkdown_WithEmptyTable_ShouldHandleGracefully()
    {
        // Arrange
        var xml = @"<table>
            <tbody>
            </tbody>
        </table>";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        // Empty tables should not cause exceptions and should return empty result
        Assert.Empty(result);
    }

    [Fact]
    public void ExtractMarkdown_WithTableWithMixedHeaderTypes_ShouldDetectHeaders()
    {
        // Arrange
        var xml = @"<table>
            <tbody>
                <tr>
                    <th><p><strong>Header TH</strong></p></th>
                    <td><p><strong>Header TD</strong></p></td>
                </tr>
                <tr>
                    <td><p>Data 1</p></td>
                    <td><p>Data 2</p></td>
                </tr>
            </tbody>
        </table>";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        Assert.NotEmpty(result);
        
        // Should treat first row as header since it contains th elements
        Assert.Contains("| **Header TH** | **Header TD** |", result);
        Assert.Contains("| --- | --- |", result);
        Assert.Contains("| Data 1 | Data 2 |", result);
    }
}
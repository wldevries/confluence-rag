using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestPlatform.Common;
using Moq;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;

namespace ConfluenceRag.Tests;

public class ConfluenceChunkerTestsDates
{
    private const string PeoplePath = @"C:\temp\data\atlassian\people.json";
    private readonly Mock<IEmbeddingGenerator<string, Embedding<float>>> _mockEmbedder;
    private readonly MockFileSystem _fileSystem;
    private readonly Mock<IConfluenceMarkdownExtractor> _extractor;
    private readonly ConfluenceChunkerOptions _options;
    private readonly ConfluenceChunker _chunker;

    public ConfluenceChunkerTestsDates()
    {
        _mockEmbedder = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        _fileSystem = new MockFileSystem();
        _extractor = new Mock<IConfluenceMarkdownExtractor>();
        _extractor.Setup(x => x.ExtractMarkdown(It.IsAny<string>())).Returns(new List<string>() { "# Test Heading", "Test content." });
        _options = new ConfluenceChunkerOptions
        {
            PeoplePath = PeoplePath,
        };
        _chunker = new ConfluenceChunker(_fileSystem, _extractor.Object, _options);
    }

    [Fact]
    public async Task ExtractMarkdown_WithInvalidDates_ShouldHandleGracefully()
    {
        // Arrange
        var confluenceData = new
        {
            id = "123456",
            title = "Test Page",
            _links = new { webui = "/pages/viewpage.action?pageId=123456" },
            body = new { storage = new { value = "<h1>Test Heading</h1><p>Test content.</p>" } },
            version = new { when = "invalid-date-string" },
            history = new { createdDate = "not-a-date" }
        };

        var jsonContent = JsonSerializer.Serialize(confluenceData);
        var tempFile = @"C:\temp\invaliddates.json";
        _fileSystem.AddFile(tempFile, new MockFileData(jsonContent));

        var mockEmbedding = new Embedding<float>(new float[] { 0.1f, 0.2f, 0.3f });
        _mockEmbedder.Setup(e => e.GenerateAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EmbeddingGenerationOptions>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>(new[] { mockEmbedding }));

        // Act
        var result = await _chunker.ProcessSingleConfluenceJsonAndChunkAsync(tempFile, _mockEmbedder.Object);

        // Assert
        Assert.NotEmpty(result);
        var chunk = result.First();
        Assert.Null(chunk.Metadata.CreatedDate);
        Assert.Null(chunk.Metadata.LastModifiedDate);
    }

    [Fact]
    public async Task ExtractMarkdown_WithDateFields_ShouldParseDatesCorrectly()
    {
        // Arrange
        var confluenceData = new
        {
            id = "123456",
            title = "Test Page",
            _links = new { webui = "/pages/viewpage.action?pageId=123456" },
            body = new { storage = new { value = "<h1>Test Heading</h1><p>Test content.</p>" } },
            version = new { when = "2024-03-15T14:30:00.000Z" },
            history = new { createdDate = "2024-01-10T09:15:30.000Z" }
        };

        var jsonContent = JsonSerializer.Serialize(confluenceData);
        var tempFile = @"C:\temp\datefields.json";
        _fileSystem.AddFile(tempFile, new MockFileData(jsonContent));

        var mockEmbedding = new Embedding<float>(new float[] { 0.1f, 0.2f, 0.3f });
        _mockEmbedder.Setup(e => e.GenerateAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EmbeddingGenerationOptions>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>(new[] { mockEmbedding }));

        // Act
        var result = await _chunker.ProcessSingleConfluenceJsonAndChunkAsync(tempFile, _mockEmbedder.Object);

        // Assert
        Assert.NotEmpty(result);
        var chunk = result.First();
        Assert.Equal("2024-01-10T09:15:30.0000000Z", chunk.Metadata.CreatedDate);
        Assert.Equal("2024-03-15T14:30:00.0000000Z", chunk.Metadata.LastModifiedDate);
    }

    [Fact]
    public async Task ProcessSingleConfluenceJsonAndChunkAsync_WithDateFields_ShouldParseDatesCorrectly()
    {
        // Arrange
        var confluenceData = new
        {
            id = "123456",
            title = "Test Page",
            _links = new { webui = "/pages/viewpage.action?pageId=123456" },
            body = new { storage = new { value = "<h1>Test Heading</h1><p>Test content.</p>" } },
            version = new { when = "2024-03-15T14:30:00.000Z" },
            history = new { createdDate = "2024-01-10T09:15:30.000Z" }
        };

        var jsonContent = JsonSerializer.Serialize(confluenceData);
        var tempFile = @"C:\temp\datefields.json";
        _fileSystem.AddFile(tempFile, new MockFileData(jsonContent));

        var mockEmbedding = new Embedding<float>(new float[] { 0.1f, 0.2f, 0.3f });
        _mockEmbedder.Setup(e => e.GenerateAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EmbeddingGenerationOptions>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>(new[] { mockEmbedding }));

        // Act
        var result = await _chunker.ProcessSingleConfluenceJsonAndChunkAsync(tempFile, _mockEmbedder.Object);

        // Assert
        Assert.NotEmpty(result);
        var chunk = result.First();
        Assert.Equal("2024-01-10T09:15:30.0000000Z", chunk.Metadata.CreatedDate);
        Assert.Equal("2024-03-15T14:30:00.0000000Z", chunk.Metadata.LastModifiedDate);
    }

    [Fact]
    public async Task ProcessSingleConfluenceJsonAndChunkAsync_WithInvalidDates_ShouldHandleGracefully()
    {
        // Arrange
        var confluenceData = new
        {
            id = "123456",
            title = "Test Page",
            _links = new { webui = "/pages/viewpage.action?pageId=123456" },
            body = new { storage = new { value = "<h1>Test Heading</h1><p>Test content.</p>" } },
            version = new { when = "invalid-date-string" },
            history = new { createdDate = "not-a-date" }
        };

        var jsonContent = JsonSerializer.Serialize(confluenceData);
        var tempFile = @"C:\temp\invaliddates.json";
        _fileSystem.AddFile(tempFile, new MockFileData(jsonContent));

        var mockEmbedding = new Embedding<float>(new float[] { 0.1f, 0.2f, 0.3f });
        _mockEmbedder.Setup(e => e.GenerateAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EmbeddingGenerationOptions>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>(new[] { mockEmbedding }));

        // Act
        var result = await _chunker.ProcessSingleConfluenceJsonAndChunkAsync(tempFile, _mockEmbedder.Object);

        // Assert
        Assert.NotEmpty(result);
        var chunk = result.First();
        Assert.Null(chunk.Metadata.CreatedDate);
        Assert.Null(chunk.Metadata.LastModifiedDate);
    }

    [Fact]
    public async Task ProcessSingleConfluenceJsonAndChunkAsync_WithValidJson_ShouldReturnChunks()
    {
        // Arrange
        var confluenceData = new
        {
            id = "123456",
            title = "Test Page",
            _links = new { webui = "/pages/viewpage.action?pageId=123456" },
            body = new { storage = new { value = "<h1>Test Heading</h1><p>Test content here.</p>" } },
            labels = new[] { "test", "confluence" },
            version = new { when = "2024-01-01T10:00:00Z" },
            history = new { createdDate = "2023-12-01T10:00:00Z" }
        };

        var jsonContent = JsonSerializer.Serialize(confluenceData);
        var tempFile = @"C:\temp\testfile.json";
        _fileSystem.AddFile(tempFile, new MockFileData(jsonContent));

        var mockEmbedding = new Embedding<float>(new float[] { 0.1f, 0.2f, 0.3f });
        _mockEmbedder.Setup(e => e.GenerateAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EmbeddingGenerationOptions>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>(new[] { mockEmbedding }));

        // Act
        var result = await _chunker.ProcessSingleConfluenceJsonAndChunkAsync(tempFile, _mockEmbedder.Object);

        // Assert
        Assert.NotEmpty(result);
        var chunk = result.First();
        Assert.Equal("123456", chunk.Metadata.PageId);
        Assert.Equal("Test Page", chunk.Metadata.Title);
        Assert.Contains("/pages/viewpage.action?pageId=123456", chunk.Metadata.WebUI);
        Assert.Contains("test", chunk.Metadata.Labels);
        Assert.Contains("confluence", chunk.Metadata.Labels);
        Assert.Contains("# Test Heading", chunk.Metadata.ChunkText);
        Assert.NotNull(chunk.Metadata.CreatedDate);
        Assert.NotNull(chunk.Metadata.LastModifiedDate);
        Assert.NotEmpty(chunk.Embedding);
    }

    [Fact]
    public async Task ProcessSingleConfluenceJsonAndChunkAsync_WithMissingContent_ShouldReturnEmptyList()
    {
        // Arrange
        var confluenceData = new
        {
            id = "123456",
            title = "Test Page",
            _links = new { webui = "/pages/viewpage.action?pageId=123456" }
            // Missing body content
        };

        var jsonContent = JsonSerializer.Serialize(confluenceData);
        var tempFile = @"C:\temp\missingcontent.json";
        _fileSystem.AddFile(tempFile, new MockFileData(jsonContent));

        // Act
        var result = await _chunker.ProcessSingleConfluenceJsonAndChunkAsync(tempFile, _mockEmbedder.Object);

        // Assert
        Assert.Empty(result);
    }
    
    
    [Fact]
    public async Task ProcessSingleConfluenceJsonAndChunkAsync_WithMissingDateFields_ShouldHandleGracefully()
    {
        // Arrange
        var confluenceData = new
        {
            id = "123456",
            title = "Test Page",
            _links = new { webui = "/pages/viewpage.action?pageId=123456" },
            body = new { storage = new { value = "<h1>Test Heading</h1><p>Test content.</p>" } }
            // Missing version and history fields entirely
        };

        var jsonContent = JsonSerializer.Serialize(confluenceData);
        var tempFile = @"C:\temp\missingdate.json";
        _fileSystem.AddFile(tempFile, new MockFileData(jsonContent));

        var mockEmbedding = new Embedding<float>(new float[] { 0.1f, 0.2f, 0.3f });
        _mockEmbedder.Setup(e => e.GenerateAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EmbeddingGenerationOptions>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>(new[] { mockEmbedding }));

        // Act
        var result = await _chunker.ProcessSingleConfluenceJsonAndChunkAsync(tempFile, _mockEmbedder.Object);

        // Assert
        Assert.NotEmpty(result);
        var chunk = result.First();
        Assert.Null(chunk.Metadata.CreatedDate);
        Assert.Null(chunk.Metadata.LastModifiedDate);
    }

}
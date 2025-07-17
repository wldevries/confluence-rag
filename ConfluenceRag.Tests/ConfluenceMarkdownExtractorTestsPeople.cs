using ConfluenceRag.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;

namespace ConfluenceRag.Tests;

public class ConfluenceMarkdownExtractorTestsPeople
{
    private const string PeoplePath = @"C:\temp\data\atlassian\people.json";
    private readonly Mock<ILogger<ConfluenceMarkdownExtractor>> _mockLogger;
    private readonly Mock<IEmbeddingGenerator<string, Embedding<float>>> _mockEmbedder;
    private readonly MockFileSystem _fileSystem;
    private readonly ConfluenceMarkdownExtractor _extractor;

    public ConfluenceMarkdownExtractorTestsPeople()
    {
        _mockLogger = new Mock<ILogger<ConfluenceMarkdownExtractor>>();
        _mockEmbedder = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        _fileSystem = new MockFileSystem();
        _extractor = new ConfluenceMarkdownExtractor(_mockLogger.Object, _fileSystem, new() { PeoplePath = PeoplePath });
    }

    [Fact]
    public void ExtractMarkdown_WithUserElement_ShouldDisplayAccountId()
    {
        // Arrange
        var xml = @"<p><ri:user ri:account-id=""123e4567-e89b-12d3-a456-426614174000"" xmlns:ri=""http://atlassian.com/ri"" /></p>";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        Assert.NotEmpty(result);
        Assert.Contains("User ID: 123e4567-e89b-12d3-a456-426614174000", result);
    }

    [Fact]
    public void ExtractMarkdown_WithUserElementUserKey_ShouldDisplayUserKey()
    {
        // Arrange
        var xml = @"<p><ri:user ri:userkey=""john.doe"" xmlns:ri=""http://atlassian.com/ri"" /></p>";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        Assert.NotEmpty(result);
        Assert.Contains("User Key: john.doe", result);
    }

    [Fact]
    public void ExtractMarkdown_WithUserElementAndPeopleMapping_ShouldDisplayDisplayName()
    {
        // Arrange - Create a chunker with mock people data by creating a temporary people.json file
        var tempDir = _fileSystem.Path.Combine("C:\\temp", Guid.NewGuid().ToString());
        var dataDir = _fileSystem.Path.Combine(tempDir, "data");
        var atlassianDir = _fileSystem.Path.Combine(dataDir, "atlassian");
        _fileSystem.AddDirectory(atlassianDir);

        var peopleData = new[]
        {
            new
            {
                accountId = "557058:12345678-1234-5678-9012-123456789abc",
                displayName = "Jane Smith",
                email = "janesmith@testcompany.com"
            },
            new
            {
                accountId = "5fad1bf6c824730070816da5",
                displayName = "John Doe",
                email = "johndoe@testcompany.com"
            }
        };

        var peopleJson = JsonSerializer.Serialize(peopleData);
        var peoplePath = _fileSystem.Path.Combine(atlassianDir, "people.json");
        _fileSystem.AddFile(peoplePath, new MockFileData(peopleJson));

        // Create chunker with people mapping, using mock file system
        var testChunker = new ConfluenceMarkdownExtractor(_mockLogger.Object, _fileSystem, new() { PeoplePath = peoplePath });

        var xml = @"<p><ri:user ri:account-id=""557058:12345678-1234-5678-9012-123456789abc"" xmlns:ri=""http://atlassian.com/ri"" /></p>";

        // Act
        var result = testChunker.ExtractMarkdown(xml);

        // Assert
        Assert.NotEmpty(result);
        Assert.Contains("User: Jane Smith", result);
    }

    [Fact]
    public void ExtractMarkdown_WithMultipleUserElements_ShouldDisplayAllUsers()
    {
        // Arrange
        var xml = @"<p>This page was created by <ri:user ri:account-id=""user1"" xmlns:ri=""http://atlassian.com/ri"" /> and reviewed by <ri:user ri:account-id=""user2"" xmlns:ri=""http://atlassian.com/ri"" /></p>";

        // Act
        var result = _extractor.ExtractMarkdown(xml);

        // Assert
        Assert.NotEmpty(result);
        Assert.Contains("User ID: user1", result);
        Assert.Contains("User ID: user2", result);
    }
}
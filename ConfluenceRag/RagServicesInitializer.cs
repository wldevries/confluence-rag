using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.IO.Abstractions;

namespace ConfluenceRag;

public static class RagServicesInitializer
{
    public static IServiceCollection BuildServiceProvider(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(o => {
            var fetchDir = configuration["CONFLUENCE_FETCH_DIR"] ?? "data";
            var dataDir = Path.Combine(fetchDir, "pages");
            var outputDir = configuration["CONFLUENCE_OUTPUT_DIR"] ?? "output/confluence";
            var peoplePath = Path.Combine(fetchDir, "people.json");
            
            return new ConfluenceChunkerOptions
            {
                PagesDir = dataDir,
                OutputDir = outputDir,
                PeoplePath = peoplePath
            };
        });
        
        // Register ConfluenceOptions using the Options pattern
        services.Configure<ConfluenceOptions>(opts =>
        {
            opts.Username = configuration["ATLASSIAN_USERNAME"] ?? throw new InvalidOperationException("ATLASSIAN_USERNAME environment variable is not set.");
            opts.ApiToken = configuration["ATLASSIAN_API_KEY"] ?? throw new InvalidOperationException("ATLASSIAN_API_KEY environment variable is not set.");
            opts.BaseUrl = configuration["ATLASSIAN_BASE_URL"] ?? throw new InvalidOperationException("ATLASSIAN_BASE_URL environment variable is not set.");
        });

        services.AddLogging(builder => builder.AddConsole());

        // Register file system abstraction
        services.AddSingleton<IFileSystem, FileSystem>();

        // Register embedding service
        var embeddingModelPath = configuration["EMBEDDING_MODEL_PATH"] ?? Path.Combine(Directory.GetCurrentDirectory(), "onnx/all-MiniLM-L6-v2");
        services.AddBertOnnxEmbeddingGenerator(
            Path.Combine(embeddingModelPath, "model.onnx"),
            Path.Combine(embeddingModelPath, "vocab.txt"));

        // Register chunker and fetcher
        services.AddSingleton<IConfluenceChunker, ConfluenceChunker>();
        services.AddSingleton<IConfluenceFetcher, ConfluenceFetcher>();
        services.AddSingleton<IConfluenceMarkdownExtractor, ConfluenceMarkdownExtractor>();
        return services;
    }
}

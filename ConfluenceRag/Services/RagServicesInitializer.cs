using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.IO.Abstractions;
using ConfluenceRag.Services;
using ConfluenceRag.Models;
using Microsoft.SemanticKernel.Connectors.Onnx;
using Spectre.Console;

namespace ConfluenceRag;

public static class RagServicesInitializer
{
    public static IServiceCollection BuildServiceProvider(IServiceCollection services, IConfiguration configuration)
    {
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<IFileSystem, FileSystem>();
        
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
            AnsiConsole.MarkupLineInterpolated($"[green]Using Confluence API at base url {opts.BaseUrl}[/]");
        });
        
        // Register embedding service
        var embeddingModelPath = configuration["EMBEDDING_MODEL_PATH"] ?? Path.Combine(Directory.GetCurrentDirectory(), "onnx/all-MiniLM-L6-v2");
        var modelPath = Path.Combine(embeddingModelPath, "model.onnx");
        var vocabPath = Path.Combine(embeddingModelPath, "vocab.txt");
        AnsiConsole.MarkupLine($"[green]Using embedding model at: {modelPath}[/]");
        BertOnnxOptions bertOptions = new()
        {
            CaseSensitive = false,
        };
        services.AddBertOnnxEmbeddingGenerator(modelPath, vocabPath, bertOptions);

        // Register embedding tokenizer        
        services.AddSingleton(services =>
        {
            AnsiConsole.MarkupLine($"[green]Using tokenizer vocabulary at: {vocabPath}[/]");
            FastBertTokenizer.BertTokenizer tokenizer = new();
            using FileStream vocabStream = new(vocabPath, FileMode.Open, FileAccess.Read);
            using StreamReader vocabReader = new(vocabStream);
            tokenizer.LoadVocabulary(vocabReader, !bertOptions.CaseSensitive, bertOptions.UnknownToken, bertOptions.ClsToken, bertOptions.SepToken, bertOptions.PadToken, bertOptions.UnicodeNormalization);
            return tokenizer;
        });

        // Register chunker and fetcher
        services.AddSingleton<IConfluenceChunker, ConfluenceChunker>();
        services.AddSingleton<IConfluenceFetcher, ConfluenceFetcher>();
        services.AddSingleton<IConfluenceMarkdownExtractor, ConfluenceMarkdownExtractor>();
        return services;
    }
}

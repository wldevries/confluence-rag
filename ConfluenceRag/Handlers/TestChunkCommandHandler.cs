using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using System.IO.Abstractions;
using ConfluenceRag.Services;

namespace ConfluenceRag.Handlers;

public static class TestChunkCommandHandler
{
    public static void Register(RootCommand rootCommand, IServiceProvider provider)
    {
        var chunkFileArg = new Argument<string>("file", "The Confluence JSON file to chunk");
        var testChunkCommand = new Command("test-chunk", "Chunk a single Confluence JSON file and output to stdout");
        testChunkCommand.AddArgument(chunkFileArg);
        testChunkCommand.SetHandler(async (string file) =>
        {
            var fileSystem = provider.GetRequiredService<IFileSystem>();
            var chunker = provider.GetRequiredService<IConfluenceChunker>();
            var embedder = provider.GetRequiredService<Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>>();
            
            try
            {
                if (!fileSystem.File.Exists(file))
                {
                    Console.WriteLine($"File not found: {file}");
                    return;
                }

                var jsonText = await fileSystem.File.ReadAllTextAsync(file);
                using var doc = System.Text.Json.JsonDocument.Parse(jsonText);
                var root = doc.RootElement;
                string xml = "";
                if (root.TryGetProperty("body", out var body) && body.TryGetProperty("storage", out var storage) && storage.TryGetProperty("value", out var value))
                    xml = value.GetString() ?? "";

                Console.WriteLine("=== Raw XML (Confluence Storage Format) ===");
                Console.WriteLine(xml);
                Console.WriteLine();

                var chunks = await chunker.ProcessSingleConfluenceJsonAndChunkAsync(file, embedder);

                int i = 1;
                foreach (var chunk in chunks)
                {
                    var headingsStr = string.Join(" > ", chunk.Metadata.Headings.Where(h => !string.IsNullOrWhiteSpace(h)));
                    Console.WriteLine($"=== Chunk {i++} (Headings: {headingsStr}) ===");
                    Console.WriteLine(chunk.Metadata.ChunkText);
                    Console.WriteLine("---");
                    Console.WriteLine();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during chunking: {ex.Message}");
            }
        }, chunkFileArg);
        
        rootCommand.AddCommand(testChunkCommand);
    }
}
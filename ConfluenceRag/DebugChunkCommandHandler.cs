using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.CommandLine;

namespace ConfluenceRag;

public static class DebugChunkCommandHandler
{
    public static void Register(RootCommand rootCommand, IServiceProvider provider, string dataDir)
    {
        var debugChunkArg = new Argument<string>("filename", "The filename of the Confluence JSON file to debug chunking (in data/confluence)");
        var debugChunkCommand = new Command("debug-chunk", "Debug chunking for a single Confluence document");
        debugChunkCommand.AddArgument(debugChunkArg);
        debugChunkCommand.SetHandler(async (string filename) =>
        {
            var logger = provider.GetRequiredService<ILogger<Program>>();
            var chunker = provider.GetRequiredService<IConfluenceChunker>();
            var embedder = provider.GetRequiredService<Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>>();
            string filePath = Path.Combine(dataDir, filename);
            if (!File.Exists(filePath))
            {
                logger.LogError("File not found: {FilePath}", filePath);
                return;
            }
            string json = await File.ReadAllTextAsync(filePath);
            // Try to extract the original XML from the JSON (assuming a property 'body.storage.value')
            string? xml = null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("body", out var body) &&
                    body.TryGetProperty("storage", out var storage) &&
                    storage.TryGetProperty("value", out var value))
                {
                    xml = value.GetString();
                }
            }
            catch (Exception exJson)
            {
                logger.LogError(exJson, "Failed to parse JSON for XML extraction: {Message}", exJson.Message);
            }
            if (xml != null)
            {
                Console.WriteLine("Original Confluence XML:\n" + xml);
            }
            else
            {
                Console.WriteLine($"Could not extract XML from JSON file: {filePath}");
            }
            // Run chunking on this file
            if (embedder == null)
            {
                logger.LogError("Could not resolve embedding generator service.");
                return;
            }
            var chunkRecords = await chunker.ProcessSingleConfluenceJsonAndChunkAsync(filePath, embedder);
            int i = 1;
            foreach (var chunk in chunkRecords)
            {
                var headingsStr = string.Join(" > ", chunk.Headings.Where(h => !string.IsNullOrWhiteSpace(h)));
                Console.WriteLine($"Chunk {i++} (Headings: {headingsStr})\n{chunk.ChunkText}\n---");
            }
        }, debugChunkArg);
        rootCommand.AddCommand(debugChunkCommand);
    }
}

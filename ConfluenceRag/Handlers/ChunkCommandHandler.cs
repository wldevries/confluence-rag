using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ConfluenceRag.Handlers;

public static class ChunkCommandHandler
{
    public static void Register(RootCommand rootCommand, IServiceProvider provider, string pagesDir, string outputDir)
    {
        var chunkCommand = new Command("chunk", "Chunk all local Confluence JSON files");
        chunkCommand.SetHandler(async () =>
        {
            var logger = provider.GetRequiredService<ILogger<Program>>();
            var chunker = provider.GetRequiredService<IConfluenceChunker>();
            var embedder = provider.GetRequiredService<Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>>();
            
            try
            {
                logger.LogInformation("Chunking all local Confluence JSON files from {PagesDir} to {OutputDir}", pagesDir, outputDir);
                await chunker.ProcessAllConfluenceJsonAndChunkAsync(pagesDir, outputDir, embedder);
                logger.LogInformation("Chunking completed successfully.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during chunking: {Message}", ex.Message);
            }
        });
        
        rootCommand.AddCommand(chunkCommand);
    }
}
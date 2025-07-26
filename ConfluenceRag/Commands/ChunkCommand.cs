using System.CommandLine;
using ConfluenceRag.Services;
using ConfluenceRag.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.IO.Abstractions;

namespace ConfluenceRag.Commands;

public class ChunkCommand(Func<IHostBuilder> createHostBuilder) : IRagCommand
{
    public Command CreateCommand()
    {
        var chunkCommand = new Command("chunk", "Chunk all local Confluence JSON files");
        chunkCommand.SetHandler(async () =>
        {
            using var host = createHostBuilder().Build();
            var provider = host.Services;
            
            var logger = provider.GetRequiredService<ILogger<Program>>();
            var chunker = provider.GetRequiredService<IConfluenceChunker>();
            var embedder = provider.GetRequiredService<Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>>();
            var fileSystem = provider.GetRequiredService<IFileSystem>();
            var chunkerOptions = provider.GetRequiredService<ConfluenceChunkerOptions>();
            
            string pagesDir = fileSystem.Path.IsPathRooted(chunkerOptions.PagesDir)
                ? chunkerOptions.PagesDir
                : fileSystem.Path.Combine(fileSystem.Directory.GetCurrentDirectory(), chunkerOptions.PagesDir);
            
            string outputDir = fileSystem.Path.IsPathRooted(chunkerOptions.OutputDir)
                ? chunkerOptions.OutputDir
                : fileSystem.Path.Combine(fileSystem.Directory.GetCurrentDirectory(), chunkerOptions.OutputDir);
            
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

        return chunkCommand;
    }
}
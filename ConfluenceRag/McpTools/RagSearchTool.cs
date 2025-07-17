using ConfluenceRag.Models;
using ConfluenceRag.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.IO.Abstractions;

namespace ConfluenceRag.McpTools
{
    [McpServerToolType]
    public static class RagSearchTool
    {
        [McpServerTool(Name = "search"), Description("Searches RAG documents for the closest match and returns up to 10 chunks.")]
        public static async Task<List<string>> Search(
            [Description("The search query")] string query,
            IServiceProvider serviceProvider)
        {
            var fileSystem = serviceProvider.GetRequiredService<IFileSystem>();
            var chunkerOptions = serviceProvider.GetRequiredService<ConfluenceChunkerOptions>();
            // Get the chunk reader service
            var chunkReader = new ConfluenceChunkReader(fileSystem, chunkerOptions.OutputDir);
            // Search for the closest matches by text
            var results = await chunkReader.SearchByTextAsync(query);
            return results.Take(10).Select(chunk => chunk.Metadata.ChunkText).ToList();
        }
    }
}

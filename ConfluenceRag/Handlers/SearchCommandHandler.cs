using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using System.IO.Abstractions;

namespace ConfluenceRag;

public static class SearchCommandHandler
{
    public static void Register(RootCommand rootCommand, IServiceProvider provider, string outputDir)
    {
        var searchTextArg = new Argument<string>("searchText", "Text to search for in chunks");
        var searchCommand = new Command("search", "Search chunks using the new metadata.jsonl + embeddings.bin format");
        searchCommand.AddArgument(searchTextArg);
        searchCommand.SetHandler(async (string searchText) =>
        {
            var fileSystem = provider.GetRequiredService<IFileSystem>();
            
            try
            {
                var reader = new ConfluenceChunkReader(fileSystem, outputDir);
                var chunks = await reader.SearchByTextAsync(searchText);
                
                Console.WriteLine($"Found {chunks.Count} chunks matching '{searchText}':");
                Console.WriteLine();
                
                foreach (var chunk in chunks.Take(5))
                {
                    Console.WriteLine($"=== {chunk.Metadata.Title} (Page: {chunk.Metadata.PageId}, Chunk: {chunk.Metadata.ChunkIndex}) ===");
                    Console.WriteLine($"Headings: [{string.Join(", ", chunk.Metadata.Headings.Where(h => !string.IsNullOrEmpty(h)))}]");
                    Console.WriteLine(chunk.Metadata.ChunkText.Length > 200 ? 
                        chunk.Metadata.ChunkText.Substring(0, 200) + "..." : 
                        chunk.Metadata.ChunkText);
                    Console.WriteLine();
                }
                
                if (chunks.Count > 5)
                {
                    Console.WriteLine($"... and {chunks.Count - 5} more results");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during search: {ex.Message}");
            }
        }, searchTextArg);
        
        rootCommand.AddCommand(searchCommand);
    }
}
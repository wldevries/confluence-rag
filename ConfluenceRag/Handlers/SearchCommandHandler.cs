using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using System.IO.Abstractions;
using ConfluenceRag.Services;
using Microsoft.Extensions.AI;
using Spectre.Console;

namespace ConfluenceRag.Handlers;

public static class SearchCommandHandler
{
    public static void Register(RootCommand rootCommand, IServiceProvider provider, string outputDir)
    {
        var searchTextArg = new Argument<string>("searchText", "Text to search for in chunks");
        var useTextSearchOption = new Option<bool>("--text", "Use text-based search instead of embedding similarity search");
        var topKOption = new Option<int>("--top", () => 5, "Number of top results to return");
        
        var searchCommand = new Command("search", "Search chunks using embedding similarity (default) or text-based search (--text)");
        searchCommand.AddArgument(searchTextArg);
        searchCommand.AddOption(useTextSearchOption);
        searchCommand.AddOption(topKOption);
        
        searchCommand.SetHandler(async (string searchText, bool useTextSearch, int topK) =>
        {
            var fileSystem = provider.GetRequiredService<IFileSystem>();
            
            try
            {
                var reader = new ConfluenceChunkReader(fileSystem, outputDir);
                
                if (useTextSearch)
                {
                    // Use the existing text-based search
                    var chunks = await reader.SearchByTextAsync(searchText);
                    
                    Console.WriteLine($"Found {chunks.Count} chunks matching '{searchText}' (text search):");
                    Console.WriteLine();
                    
                    foreach (var chunk in chunks.Take(topK))
                    {
                        Console.WriteLine($"=== {chunk.Metadata.Title} (Page: {chunk.Metadata.PageId}, Chunk: {chunk.Metadata.ChunkIndex}) ===");
                        Console.WriteLine($"Headings: [{string.Join(", ", chunk.Metadata.Headings.Where(h => !string.IsNullOrEmpty(h)))}]");
                        Console.WriteLine(chunk.Metadata.ChunkText.Length > 200 ? 
                            chunk.Metadata.ChunkText.Substring(0, 200) + "..." : 
                            chunk.Metadata.ChunkText);
                        Console.WriteLine();
                    }
                    
                    if (chunks.Count > topK)
                    {
                        Console.WriteLine($"... and {chunks.Count - topK} more results");
                    }
                }
                else
                {
                    // Use embedding similarity search
                    var embeddingGenerator = provider.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

                    AnsiConsole.MarkupLine("[grey]Generating embedding for search query...[/]");
                    var queryEmbeddingResult = await embeddingGenerator.GenerateAsync([searchText]);
                    var queryEmbedding = queryEmbeddingResult.First().Vector.ToArray();

                    AnsiConsole.MarkupLine("[grey]Searching for similar chunks...[/]");
                    var results = await reader.SearchBySimilarityAsync(queryEmbedding, topK);

                    AnsiConsole.MarkupLine($"[bold]Found {results.Count} most similar chunks for '[cyan]{searchText.EscapeMarkup()}[/]':[/]");
                    AnsiConsole.MarkupLine("");

                    foreach (var (chunk, similarity) in results)
                    {
                        var meta = chunk.Metadata;
                        string FormatIsoDate(string? dateStr)
                        {
                            if (string.IsNullOrWhiteSpace(dateStr)) return "-";
                            if (DateTime.TryParse(dateStr, out var dt))
                                return dt.ToString("yyyy-MM-dd");
                            return "-";
                        }
                        var created = FormatIsoDate(meta.CreatedDate);
                        var modified = FormatIsoDate(meta.LastModifiedDate);
                        var headings = string.Join(", ", meta.Headings.Where(h => !string.IsNullOrEmpty(h)));
                        var snippet = meta.ChunkText;

                        string simColor = similarity >= 0.75 ? "green" :
                            similarity >= 0.6 ? "yellow" :
                            similarity >= 0.45 ? "orange1" :
                            similarity >= 0.3 ? "red" : "grey";

                        AnsiConsole.MarkupLine($"[bold cyan]{meta.Title.EscapeMarkup()}[/] [dim](Page: {meta.PageId}, Chunk: {meta.ChunkIndex})[/]");
                        AnsiConsole.MarkupLine($"Similarity: [bold {simColor}]{similarity:F3}[/]   [dim]Headings:[/] [grey]{headings.EscapeMarkup()}[/]");
                        AnsiConsole.MarkupLine($"[italic dim]Created:[/] [grey]{created.EscapeMarkup()}[/]   [italic dim]Last Modified:[/] [grey]{modified.EscapeMarkup()}[/]");
                        AnsiConsole.MarkupLine($"[white]{snippet.EscapeMarkup()}[/]");
                        AnsiConsole.MarkupLine("");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during search: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
            }
        }, searchTextArg, useTextSearchOption, topKOption);
        
        rootCommand.AddCommand(searchCommand);
    }
}
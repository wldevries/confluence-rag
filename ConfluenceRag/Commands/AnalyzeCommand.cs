using System.CommandLine;
using System.IO.Abstractions;
using System.Text.Json.Nodes;
using FastBertTokenizer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ConfluenceRag.Models;
using Spectre.Console;

namespace ConfluenceRag.Commands;

public class AnalyzeCommand(Func<IHostBuilder> createHostBuilder) : IRagCommand
{
    public Command CreateCommand()
    {
        var chunksFileOption = new Option<string>(
            name: "--chunks-file",
            description: "Path to the metadata.jsonl file"
        );
        var analyzeCommand = new Command("analyze", "Analyze chunk statistics from generated JSONL output")
        {
            chunksFileOption
        };
        analyzeCommand.SetHandler(async (chunksFile) =>
        {
            using var host = createHostBuilder().Build();
            var provider = host.Services;
            
            var fileSystem = provider.GetRequiredService<IFileSystem>();
            var chunkerOptions = provider.GetRequiredService<ConfluenceChunkerOptions>();
            var tokenizer = provider.GetRequiredService<BertTokenizer>();
            
            string outputDir = fileSystem.Path.IsPathRooted(chunkerOptions.OutputDir)
                ? chunkerOptions.OutputDir
                : fileSystem.Path.Combine(fileSystem.Directory.GetCurrentDirectory(), chunkerOptions.OutputDir);
            
            string finalChunksFile = chunksFile ?? Path.Combine(outputDir, "metadata.jsonl");
            
            AnalyzeChunks(finalChunksFile, fileSystem, tokenizer);
        }, chunksFileOption);
        return analyzeCommand;
    }

    private static void AnalyzeChunks(string chunksFile, IFileSystem fileSystem, BertTokenizer tokenizer)
    {
        AnsiConsole.MarkupLine("[cyan]Analyzing chunk statistics...[/]");
        AnsiConsole.MarkupLine($"[grey]Reading chunks from: {Markup.Escape(chunksFile)}[/]");

        if (!fileSystem.File.Exists(chunksFile))
        {
            AnsiConsole.MarkupLine($"[red]Error: Chunks file not found at {Markup.Escape(chunksFile)}[/]");
            AnsiConsole.MarkupLine("[yellow]Please run the chunking process first:[/]");
            AnsiConsole.MarkupLine("[yellow]  dotnet run --project .\\ConfluenceRag\\ConfluenceRag.csproj[/]");
            return;
        }

        var chunks = new List<JsonObject>();
        int lineNumber = 0;
        foreach (var line in fileSystem.File.ReadLines(chunksFile))
        {
            lineNumber++;
            try
            {
                var obj = JsonNode.Parse(line)?.AsObject();
                if (obj != null)
                    chunks.Add(obj);
            }
            catch
            {
                AnsiConsole.MarkupLine($"[yellow]Warning: Failed to parse line {lineNumber}[/]");
            }
        }
        if (chunks.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No chunks found in file![/]");
            return;
        }

        // Use FastBertTokenizer to get actual token counts
        long[] tokenIdsBuffer = new long[512];
        long[] attentionMaskBuffer = new long[512];
        var chunkSizes = new List<int>(chunks.Count);
        var chunkTokenCounts = new List<int>(chunks.Count);
        foreach (var c in chunks)
        {
            var text = c["ChunkText"]?.ToString() ?? string.Empty;
            chunkSizes.Add(text.Length);
            // Tokenize (trim trailing whitespace for consistency with chunking)
            var trimmedText = text.TrimEnd();
            int tokenCount = tokenizer.Encode(trimmedText, tokenIdsBuffer, attentionMaskBuffer);
            chunkTokenCounts.Add(tokenCount);
        }

        AnsiConsole.MarkupLine("\n[green]Chunk Analysis Results[/]");
        AnsiConsole.MarkupLine("[green]=====================[/]");

        // Basic statistics
        int totalChunks = chunks.Count;
        int minSize = chunkSizes.Min();
        int maxSize = chunkSizes.Max();
        double avgSize = Math.Round(chunkSizes.Average(), 1);
        int medianSize = chunkSizes.OrderBy(x => x).ElementAt(chunkSizes.Count / 2);

        AnsiConsole.MarkupLine("\n[white]Basic Statistics:[/]");
        AnsiConsole.MarkupLine($"  Total chunks: [bold]{totalChunks}[/]");
        AnsiConsole.MarkupLine($"  Smallest chunk: [bold]{minSize}[/] characters");
        AnsiConsole.MarkupLine($"  Largest chunk: [bold]{maxSize}[/] characters");
        AnsiConsole.MarkupLine($"  Average chunk size: [bold]{avgSize}[/] characters");
        AnsiConsole.MarkupLine($"  Median chunk size: [bold]{medianSize}[/] characters");

        // Actual token statistics
        int minTokens = chunkTokenCounts.Min();
        int maxTokens = chunkTokenCounts.Max();
        double avgTokens = Math.Round(chunkTokenCounts.Average(), 1);
        int medianTokens = chunkTokenCounts.OrderBy(x => x).ElementAt(chunkTokenCounts.Count / 2);
        int totalTokens = chunkTokenCounts.Sum();
        AnsiConsole.MarkupLine("\n[white]Token Statistics (BertTokenizer):[/]");
        AnsiConsole.MarkupLine($"  Smallest chunk: [bold]{minTokens}[/] tokens");
        AnsiConsole.MarkupLine($"  Largest chunk: [bold]{maxTokens}[/] tokens");
        AnsiConsole.MarkupLine($"  Average chunk size: [bold]{avgTokens}[/] tokens");
        AnsiConsole.MarkupLine($"  Median chunk size: [bold]{medianTokens}[/] tokens");
        AnsiConsole.MarkupLine($"  Total tokens: [bold]{totalTokens}[/] tokens");

        // Size distribution
        var sizeRanges = new Dictionary<string, int>
        {
            {"Very Small (0-200)", chunkSizes.Count(s => s <= 200)},
            {"Small (201-500)", chunkSizes.Count(s => s > 200 && s <= 500)},
            {"Medium (501-1000)", chunkSizes.Count(s => s > 500 && s <= 1000)},
            {"Large (1001-2000)", chunkSizes.Count(s => s > 1000 && s <= 2000)},
            {"Very Large (2000+)", chunkSizes.Count(s => s > 2000)}
        };
        AnsiConsole.MarkupLine("\n[white]Size Distribution:[/]");
        foreach (var range in sizeRanges)
        {
            double pct = Math.Round((double)range.Value / totalChunks * 100, 1);
            AnsiConsole.MarkupLine($"  {range.Key}: [bold]{range.Value}[/] chunks ({pct}%)");
        }

        // Page distribution
        var pageStats = chunks.GroupBy(c => c["PageId"]?.ToString() ?? "").OrderByDescending(g => g.Count()).ToList();
        AnsiConsole.MarkupLine("\n[white]Page Statistics:[/]");
        AnsiConsole.MarkupLine($"  Total pages: [bold]{pageStats.Count}[/]");
        AnsiConsole.MarkupLine($"  Average chunks per page: [bold]{Math.Round((double)totalChunks / pageStats.Count, 1)}[/]");
        AnsiConsole.MarkupLine("\nPages with most chunks:");
        foreach (var page in pageStats.Take(5))
        {
            var pageTitle = page.First()["Title"]?.ToString() ?? "";
            var truncatedTitle = pageTitle.Length > 50 ? pageTitle.Substring(0, 47) + "..." : pageTitle;
            AnsiConsole.MarkupLine($"  [bold]{page.Count()}[/] chunks: {Markup.Escape(truncatedTitle)}");
        }

        // Heading analysis
        var chunksWithHeadings = chunks.Where(c => c["Headings"] is JsonArray arr && arr.Count > 0).ToList();
        var headingLevels = new Dictionary<string, int>();
        foreach (var c in chunks)
        {
            if (c["Headings"] is JsonArray arr)
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(arr[i]?.ToString()))
                    {
                        var level = $"H{i + 1}";
                        if (!headingLevels.ContainsKey(level)) headingLevels[level] = 0;
                        headingLevels[level]++;
                    }
                }
            }
        }
        AnsiConsole.MarkupLine("\n[white]Heading Structure:[/]");
        AnsiConsole.MarkupLine($"  Chunks with headings: [bold]{chunksWithHeadings.Count}[/] / {totalChunks} ({Math.Round((double)chunksWithHeadings.Count / totalChunks * 100, 1)}%)");
        if (headingLevels.Count > 0)
        {
            AnsiConsole.MarkupLine("  Heading level distribution:");
            foreach (var level in headingLevels.OrderBy(kv => kv.Key))
                AnsiConsole.MarkupLine($"    {level.Key}: [bold]{level.Value}[/] occurrences");
        }

        // Label analysis
        var allLabels = chunks.SelectMany(c => c["Labels"] as JsonArray ?? new JsonArray()).Where(l => l != null).GroupBy(l => l?.ToString() ?? "").OrderByDescending(g => g.Count()).ToList();
        AnsiConsole.MarkupLine("\n[white]Label Analysis:[/]");
        if (allLabels.Count > 0)
        {
            AnsiConsole.MarkupLine($"  Total unique labels: [bold]{allLabels.Count}[/]");
            AnsiConsole.MarkupLine("  Most common labels:");
            foreach (var label in allLabels.Take(10))
                AnsiConsole.MarkupLine($"    {Markup.Escape(label.Key)}: [bold]{label.Count()}[/] chunks");
        }
        else
        {
            AnsiConsole.MarkupLine("  No labels found in chunks");
        }

        // Quality indicators
        int emptyChunks = chunks.Count(c => string.IsNullOrWhiteSpace(c["ChunkText"]?.ToString()));
        int shortChunks = chunkSizes.Count(s => s < 100);
        int veryLongChunks = chunkSizes.Count(s => s > 2000);
        AnsiConsole.MarkupLine("\n[white]Quality Indicators:[/]");
        AnsiConsole.MarkupLine($"  Empty chunks: [bold]{emptyChunks}[/]");
        AnsiConsole.MarkupLine($"  Very short chunks (<100 chars): [bold]{shortChunks}[/]");
        AnsiConsole.MarkupLine($"  Very long chunks (>2000 chars): [bold]{veryLongChunks}[/]");
        if (shortChunks > 0)
        {
            AnsiConsole.MarkupLine("\n[yellow]Sample short chunks:[/]");
            foreach (var c in chunks.Where(c => (c["ChunkText"]?.ToString()?.Length ?? 0) < 100).Take(3))
            {
                var preview = c["ChunkText"]?.ToString()?.Replace("\n", " ").Replace("\r", "") ?? "";
                if (preview.Length > 80) preview = preview.Substring(0, 77) + "...";
                AnsiConsole.MarkupLine($"    [grey]{c["ChunkText"]?.ToString()?.Length ?? 0} chars: {Markup.Escape(preview ?? string.Empty)}[/]");
            }
        }
        if (veryLongChunks > 0)
        {
            AnsiConsole.MarkupLine("\n[yellow]Sample very long chunks:[/]");
            foreach (var c in chunks.Where(c => (c["ChunkText"]?.ToString()?.Length ?? 0) > 2000).Take(3))
            {
                var preview = c["ChunkText"]?.ToString()?.Substring(0, Math.Min(100, c["ChunkText"]?.ToString()?.Length ?? 0)).Replace("\n", " ").Replace("\r", "");
                AnsiConsole.MarkupLine($"    [grey]{c["ChunkText"]?.ToString()?.Length ?? 0} chars: {Markup.Escape(preview ?? string.Empty)}...[/]");
            }
        }

        // Date analysis
        var chunksWithDates = chunks.Where(c => !string.IsNullOrWhiteSpace(c["CreatedDate"]?.ToString()) || !string.IsNullOrWhiteSpace(c["LastModifiedDate"]?.ToString())).ToList();
        AnsiConsole.MarkupLine("\n[white]Date Information:[/]");
        if (chunksWithDates.Count > 0)
        {
            AnsiConsole.MarkupLine($"  Chunks with date information: [bold]{chunksWithDates.Count}[/] / {totalChunks} ({Math.Round((double)chunksWithDates.Count / totalChunks * 100, 1)}%)");
            var chunksWithCreated = chunks.Where(c => !string.IsNullOrWhiteSpace(c["CreatedDate"]?.ToString())).ToList();
            if (chunksWithCreated.Count > 0)
            {
                var createdDates = chunksWithCreated.Select(c => DateTime.TryParse(c["CreatedDate"]?.ToString(), out var dt) ? dt : (DateTime?)null).Where(dt => dt.HasValue).ToList();
                if (createdDates.Count > 0)
                {
                    var oldestCreated = createdDates.Min();
                    var newestCreated = createdDates.Max();
                    AnsiConsole.MarkupLine($"  Created date range: [bold]{oldestCreated:yyyy-MM-dd}[/] to [bold]{newestCreated:yyyy-MM-dd}[/]");
                }
            }
            var chunksWithModified = chunks.Where(c => !string.IsNullOrWhiteSpace(c["LastModifiedDate"]?.ToString())).ToList();
            if (chunksWithModified.Count > 0)
            {
                var modifiedDates = chunksWithModified.Select(c => DateTime.TryParse(c["LastModifiedDate"]?.ToString(), out var dt) ? dt : (DateTime?)null).Where(dt => dt.HasValue).ToList();
                if (modifiedDates.Count > 0)
                {
                    var oldestModified = modifiedDates.Min();
                    var newestModified = modifiedDates.Max();
                    AnsiConsole.MarkupLine($"  Modified date range: [bold]{oldestModified:yyyy-MM-dd}[/] to [bold]{newestModified:yyyy-MM-dd}[/]");
                    var thirtyDaysAgo = DateTime.Now.AddDays(-30);
                    var recentlyModified = modifiedDates.Count(dt => dt > thirtyDaysAgo);
                    if (recentlyModified > 0)
                        AnsiConsole.MarkupLine($"  Recently modified (last 30 days): [bold]{recentlyModified}[/] chunks");
                }
            }
        }
        else
        {
            AnsiConsole.MarkupLine("  No date information found in chunks");
        }

        // File information
        var embeddingFile = Path.Combine(Path.GetDirectoryName(chunksFile) ?? ".", "embeddings.bin");
        AnsiConsole.MarkupLine("\n[white]File Information:[/]");
        var metadataFileSize = fileSystem.FileInfo.New(chunksFile).Length;
        AnsiConsole.MarkupLine($"  Metadata file: [bold]{Markup.Escape(Path.GetFileName(chunksFile))}[/]");
        AnsiConsole.MarkupLine($"  Metadata file size: [bold]{Math.Round(metadataFileSize / 1024.0 / 1024.0, 2)}[/] MB");
        if (fileSystem.File.Exists(embeddingFile))
        {
            var embeddingFileSize = fileSystem.FileInfo.New(embeddingFile).Length;
            AnsiConsole.MarkupLine($"  Embeddings stored in: [bold]embeddings.bin[/]");
            AnsiConsole.MarkupLine($"  Embedding file size: [bold]{Math.Round(embeddingFileSize / 1024.0 / 1024.0, 2)}[/] MB");
            AnsiConsole.MarkupLine($"  Expected chunks with embeddings: [bold]{totalChunks}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[yellow]  Embedding file not found at: {Markup.Escape(embeddingFile)}[/]");
            AnsiConsole.MarkupLine("[yellow]  Embeddings may not have been generated yet[/]");
        }

        AnsiConsole.MarkupLine("\n[green]Analysis complete![/]");
    }
}

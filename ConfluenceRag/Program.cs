using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;
using System.IO.Abstractions;

namespace ConfluenceRag;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((hostingContext, config) =>
            {
                config.SetBasePath(Directory.GetCurrentDirectory());
                config.AddUserSecrets<Program>();
                config.AddEnvironmentVariables();
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
            })
            .ConfigureServices((ctx, services) =>
            {
                RagServicesInitializer.BuildServiceProvider(services, ctx.Configuration);
            })
            .Build();

        // Build configuration to load secrets
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddUserSecrets<Program>()
            .AddEnvironmentVariables()
            .Build();

        var provider = host.Services;

        var logger = provider.GetRequiredService<ILogger<Program>>();
        var chunker = provider.GetRequiredService<IConfluenceChunker>();
        var fetcher = provider.GetRequiredService<IConfluenceFetcher>();
        var fileSystem = provider.GetRequiredService<IFileSystem>();
        var embedder = provider.GetService(typeof(object)); // Will be cast below

        var chunkerOptions = provider.GetRequiredService<ConfluenceChunkerOptions>();
        string pagesDir = fileSystem.Path.IsPathRooted(chunkerOptions.PagesDir) ? chunkerOptions.PagesDir : fileSystem.Path.Combine(fileSystem.Directory.GetCurrentDirectory(), chunkerOptions.PagesDir);
        string peoplePath = fileSystem.Path.IsPathRooted(chunkerOptions.PeoplePath) ? chunkerOptions.PeoplePath : fileSystem.Path.Combine(fileSystem.Directory.GetCurrentDirectory(), chunkerOptions.PeoplePath);
        string outputDir = fileSystem.Path.IsPathRooted(chunkerOptions.OutputDir) ? chunkerOptions.OutputDir : fileSystem.Path.Combine(fileSystem.Directory.GetCurrentDirectory(), chunkerOptions.OutputDir);

        // System.CommandLine 1.x compatible pattern (like UserManualCli)
        var rootCommand = new RootCommand("ConfluenceRag - fetch and chunk Confluence documentation");

        var chunkCommand = new Command("chunk", "Chunk all local Confluence JSON files");
        chunkCommand.SetHandler(async () =>
        {
            try
            {
                var embedder = provider.GetRequiredService<Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>>();
                logger.LogInformation("Chunking all local Confluence JSON files from {PagesDir} to {OutputDir}", pagesDir, outputDir);
                await chunker.ProcessAllConfluenceJsonAndChunkAsync(pagesDir, outputDir, embedder);
                logger.LogInformation("Chunking completed successfully.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during chunking: {Message}", ex.Message);
            }
        });

        var pageIdArg = new Argument<string>("pageId", "The Confluence page ID to fetch");
        var fetchCommand = new Command("fetch", "Fetch a Confluence page and its children by pageId");
        fetchCommand.AddArgument(pageIdArg);
        fetchCommand.SetHandler(async (string pageId) =>
        {
            try
            {
                logger.LogInformation("Fetching Confluence page {PageId} and its children...", pageId);
                await fetcher.FetchAndSavePageRecursive(pageId, pagesDir);
                logger.LogInformation("Fetch completed successfully.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error fetching Confluence page: {Message}", ex.Message);
            }
        }, pageIdArg);

        var chunkFileArg = new Argument<string>("file", "The Confluence JSON file to chunk");
        var testChunkCommand = new Command("test-chunk", "Chunk a single Confluence JSON file and output to stdout");
        testChunkCommand.AddArgument(chunkFileArg);
        testChunkCommand.SetHandler(async (string file) =>
        {
            try
            {
                if (!fileSystem.File.Exists(file))
                {
                    Console.WriteLine($"File not found: {file}");
                    return;
                }

                // Output the raw XML content from the JSON file
                var jsonText = await fileSystem.File.ReadAllTextAsync(file);
                using var doc = System.Text.Json.JsonDocument.Parse(jsonText);
                var root = doc.RootElement;
                string xml = "";
                if (root.TryGetProperty("body", out var body) && body.TryGetProperty("storage", out var storage) && storage.TryGetProperty("value", out var value))
                    xml = value.GetString() ?? "";

                Console.WriteLine("=== Raw XML (Confluence Storage Format) ===");
                Console.WriteLine(xml);
                Console.WriteLine();

                var embedder = provider.GetRequiredService<Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>>();
                var chunks = await chunker.ProcessSingleConfluenceJsonAndChunkAsync(file, embedder);

                foreach (var chunk in chunks)
                {
                    Console.WriteLine($"=== Chunk {chunk.ChunkIndex} ===");
                    Console.WriteLine($"Headings: {string.Join(", ", chunk.Headings)}");
                    Console.WriteLine(chunk.ChunkText);
                    Console.WriteLine();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during chunking: {ex.Message}");
            }
        }, chunkFileArg);

        rootCommand.AddCommand(chunkCommand);
        rootCommand.AddCommand(fetchCommand);
        rootCommand.AddCommand(testChunkCommand);

        // Add fetch-people command
        var fetchPeopleCommand = new Command("fetch-people", "Fetch all Confluence users and save to data/people.json");
        fetchPeopleCommand.SetHandler(async () =>
        {
            try
            {
                logger.LogInformation("Fetching all Confluence users...");
                await ((ConfluenceFetcher)fetcher).FetchAllUsersAsync(peoplePath);
                logger.LogInformation("Fetched all users successfully.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error fetching users: {Message}", ex.Message);
            }
        });
        rootCommand.AddCommand(fetchPeopleCommand);
        DebugChunkCommandHandler.Register(rootCommand, provider, pagesDir);

        // If no command is specified, show help
        if (args.Length == 0)
        {
            rootCommand.Description += "\n\nSpecify a command: chunk or fetch <pageId>";
            args = new[] { "--help" };
        }

        return await rootCommand.InvokeAsync(args);
    }
}

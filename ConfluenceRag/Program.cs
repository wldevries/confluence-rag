using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;
using System.IO.Abstractions;
using ConfluenceRag.Handlers;

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

        var fileSystem = provider.GetRequiredService<IFileSystem>();
        var chunkerOptions = provider.GetRequiredService<ConfluenceChunkerOptions>();
        string pagesDir = fileSystem.Path.IsPathRooted(chunkerOptions.PagesDir) ? chunkerOptions.PagesDir : fileSystem.Path.Combine(fileSystem.Directory.GetCurrentDirectory(), chunkerOptions.PagesDir);
        string peoplePath = fileSystem.Path.IsPathRooted(chunkerOptions.PeoplePath) ? chunkerOptions.PeoplePath : fileSystem.Path.Combine(fileSystem.Directory.GetCurrentDirectory(), chunkerOptions.PeoplePath);
        string outputDir = fileSystem.Path.IsPathRooted(chunkerOptions.OutputDir) ? chunkerOptions.OutputDir : fileSystem.Path.Combine(fileSystem.Directory.GetCurrentDirectory(), chunkerOptions.OutputDir);

        // System.CommandLine 1.x compatible pattern (like UserManualCli)
        var rootCommand = new RootCommand("ConfluenceRag - fetch and chunk Confluence documentation");

        // Register all command handlers
        ChunkCommandHandler.Register(rootCommand, provider, pagesDir, outputDir);
        FetchCommandHandler.Register(rootCommand, provider, pagesDir);
        TestChunkCommandHandler.Register(rootCommand, provider);
        FetchPeopleCommandHandler.Register(rootCommand, provider, peoplePath);
        SearchCommandHandler.Register(rootCommand, provider, outputDir);

        // If no command is specified, show help
        if (args.Length == 0)
        {
            rootCommand.Description += "\n\nSpecify a command: chunk or fetch <pageId>";
            args = new[] { "--help" };
        }

        return await rootCommand.InvokeAsync(args);
    }
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.CommandLine;
using ConfluenceRag.Services;
using ConfluenceRag.Commands;

namespace ConfluenceRag;

class Program
{
    private static IHostBuilder CreateBaseHostBuilder() =>
        Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((hostingContext, config) =>
            {
                config.SetBasePath(Directory.GetCurrentDirectory());
                config.AddUserSecrets<Program>();
                config.AddEnvironmentVariables();
            })
            .ConfigureServices((ctx, services) =>
            {
                RagServicesInitializer.BuildServiceProvider(services, ctx.Configuration);
            });

    static async Task<int> Main(string[] args)
    {
        var host = CreateBaseHostBuilder().Build();


        // System.CommandLine 1.x compatible pattern (like UserManualCli)
        var rootCommand = new RootCommand("ConfluenceRag - fetch and chunk Confluence documentation");

        IRagCommand[] commands =
        [
            new FetchCommand(CreateBaseHostBuilder),
            new FetchPeopleCommand(CreateBaseHostBuilder),
            new ChunkCommand(CreateBaseHostBuilder),
            new AnalyzeCommand(CreateBaseHostBuilder),
            new TestChunkCommand(CreateBaseHostBuilder),
            new SearchCommand(CreateBaseHostBuilder),
            new McpCommand(CreateBaseHostBuilder)
        ];

        // Add commands to root
        foreach (var commandHandler in commands)
        {
            rootCommand.AddCommand(commandHandler.CreateCommand());
        }

        // If no command is specified, show help
        if (args.Length == 0)
        {
            args = ["--help"];
        }

        return await rootCommand.InvokeAsync(args);
    }
}

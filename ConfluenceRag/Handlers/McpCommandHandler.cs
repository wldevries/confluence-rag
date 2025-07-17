using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.CommandLine;

namespace ConfluenceRag.Handlers
{
    public static class McpCommandHandler
    {
        public static void Register(RootCommand rootCommand)
        {
            var mcpCommand = new Command("mcp", "Run in Model Context Protocol (MCP) server mode with a 'search' tool.");
            mcpCommand.SetHandler(async () =>
            {
                using var mcpHost = Host.CreateDefaultBuilder()
                    .ConfigureServices((ctx, services) =>
                    {
                        // Register all existing services
                        RagServicesInitializer.BuildServiceProvider(services, ctx.Configuration);
                        // Register MCP server and tools
                        services.AddMcpServer()
                            .WithStdioServerTransport()
                            .WithToolsFromAssembly();
                    })
                    .ConfigureLogging(logging =>
                    {
                        logging.ClearProviders();
                    })
                    .Build();

                await mcpHost.RunAsync();
            });
            rootCommand.AddCommand(mcpCommand);
        }
    }
}

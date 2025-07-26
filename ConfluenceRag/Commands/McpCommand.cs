using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.CommandLine;

namespace ConfluenceRag.Commands;

public class McpCommand(Func<IHostBuilder> createHostBuilder) : IRagCommand
{
    public Command CreateCommand()
    {
        var mcpCommand = new Command("mcp", "Run in Model Context Protocol (MCP) server mode with a 'search' tool.");
        mcpCommand.SetHandler(async () =>
        {
            using var mcpHost = createHostBuilder()
                .ConfigureServices((ctx, services) =>
                {
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

        return mcpCommand;
    }
}

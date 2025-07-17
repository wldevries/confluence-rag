using System.CommandLine;
using ConfluenceRag.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ConfluenceRag.Handlers;

public static class FetchCommandHandler
{
    public static void Register(RootCommand rootCommand, IServiceProvider provider, string pagesDir)
    {
        var pageIdArg = new Argument<string>("pageId", "The Confluence page ID to fetch");
        var fetchCommand = new Command("fetch", "Fetch a Confluence page and its children by pageId");
        fetchCommand.AddArgument(pageIdArg);
        fetchCommand.SetHandler(async (string pageId) =>
        {
            var logger = provider.GetRequiredService<ILogger<Program>>();
            var fetcher = provider.GetRequiredService<IConfluenceFetcher>();
            
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
        
        rootCommand.AddCommand(fetchCommand);
    }
}
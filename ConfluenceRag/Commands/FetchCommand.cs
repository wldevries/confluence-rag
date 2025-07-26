using ConfluenceRag.Models;
using ConfluenceRag.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using System.IO.Abstractions;

namespace ConfluenceRag.Commands;

public class FetchCommand(Func<IHostBuilder> createHostBuilder) : IRagCommand
{
    public Command CreateCommand()
    {
        var pageIdArg = new Argument<string>("pageId", "The Confluence page ID to fetch");
        var fetchCommand = new Command("fetch", "Fetch a Confluence page and its children by pageId");
        fetchCommand.AddArgument(pageIdArg);
        fetchCommand.SetHandler(async (pageId) =>
        {
            using var host = createHostBuilder().Build();
            var provider = host.Services;
            
            var logger = provider.GetRequiredService<ILogger<Program>>();
            var fetcher = provider.GetRequiredService<IConfluenceFetcher>();
            var fileSystem = provider.GetRequiredService<IFileSystem>();
            var chunkerOptions = provider.GetRequiredService<ConfluenceChunkerOptions>();
            
            string pagesDir = fileSystem.Path.IsPathRooted(chunkerOptions.PagesDir) 
                ? chunkerOptions.PagesDir 
                : fileSystem.Path.Combine(fileSystem.Directory.GetCurrentDirectory(), chunkerOptions.PagesDir);
            
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

        return fetchCommand;
    }
}
using System.CommandLine;
using ConfluenceRag.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ConfluenceRag.Handlers;

public static class FetchPeopleCommandHandler
{
    public static void Register(RootCommand rootCommand, IServiceProvider provider, string peoplePath)
    {
        var fetchPeopleCommand = new Command("fetch-people", "Fetch all Confluence users and save to data/people.json");
        fetchPeopleCommand.SetHandler(async () =>
        {
            var logger = provider.GetRequiredService<ILogger<Program>>();
            var fetcher = provider.GetRequiredService<IConfluenceFetcher>();
            
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
    }
}
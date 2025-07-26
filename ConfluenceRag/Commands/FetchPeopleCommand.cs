using ConfluenceRag.Models;
using ConfluenceRag.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using System.IO.Abstractions;

namespace ConfluenceRag.Commands;

public class FetchPeopleCommand(Func<IHostBuilder> createHostBuilder) : IRagCommand
{
    public Command CreateCommand()
    {
        var fetchPeopleCommand = new Command("fetch-people", "Fetch all Confluence users and save to data/people.json");
        fetchPeopleCommand.SetHandler(async () =>
        {
            using var host = createHostBuilder().Build();
            var provider = host.Services;
            
            var logger = provider.GetRequiredService<ILogger<Program>>();
            var fetcher = provider.GetRequiredService<IConfluenceFetcher>();
            var fileSystem = provider.GetRequiredService<IFileSystem>();
            var chunkerOptions = provider.GetRequiredService<ConfluenceChunkerOptions>();
            
            string peoplePath = fileSystem.Path.IsPathRooted(chunkerOptions.PeoplePath)
                ? chunkerOptions.PeoplePath
                : fileSystem.Path.Combine(fileSystem.Directory.GetCurrentDirectory(), chunkerOptions.PeoplePath);
            
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

        return fetchPeopleCommand;
    }
}
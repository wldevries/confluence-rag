using ConfluenceRag.Models;
using Spectre.Console;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ConfluenceRag.Services;

public class ConfluenceFetcher : IConfluenceFetcher
{
    private readonly ConfluenceOptions _options;

    public ConfluenceFetcher(Microsoft.Extensions.Options.IOptions<ConfluenceOptions> options)
    {
        _options = options.Value;
    }

    private string BaseUrl => _options.BaseUrl.TrimEnd('/');

    private string GetAuthToken()
    {
        return Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{_options.Username}:{_options.ApiToken}"));
    }

    /// <summary>
    /// Fetch all Confluence users and save to data/atlassian/people.json
    /// </summary>
    public async Task FetchAllUsersAsync(string outputFilePath)
    {
        var users = new List<JsonElement>();
        int start = 0;
        int limit = 100;
        bool more = true;
        var outputDirPath = Path.GetDirectoryName(outputFilePath) ?? throw new InvalidOperationException("Output directory path is not set.");
        Directory.CreateDirectory(outputDirPath);
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", GetAuthToken());
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        while (more)
        {
            var url = $"{BaseUrl}/rest/api/search/user?limit={limit}&start={start}&cql=type=user";
            var response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                int count = 0;
                foreach (var result in results.EnumerateArray())
                {
                    if (result.TryGetProperty("user", out var user))
                    {
                        users.Add(user.Clone());
                        count++;
                    }
                }
                if (count < limit)
                {
                    more = false;
                }
                else
                {
                    start += limit;
                }
            }
            else
            {
                more = false;
            }
        }
        using var fs = File.Create(outputFilePath);
        await JsonSerializer.SerializeAsync(fs, users, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine($"Fetched {users.Count} users. Saved to {outputFilePath}");
    }
    public async Task<string> FetchConfluencePageAsync(string pageId)
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", GetAuthToken());
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var url = $"{BaseUrl}/rest/api/content/{pageId}?expand=body.storage,version,history.createdDate";
        var response = await httpClient.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Error response: {errorContent}");
            Console.WriteLine($"Status: {response.StatusCode}");
            Console.WriteLine($"URL: {url}");
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public async Task<string[]> FetchChildPageIdsAsync(string pageId)
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", GetAuthToken());
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var url = $"{BaseUrl}/rest/api/content/{pageId}/child/page?limit=100";
        var response = await httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var results = doc.RootElement.GetProperty("results");
        return results.EnumerateArray()
            .Select(e => e.GetProperty("id").GetString())
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .ToArray();
    }

    public async Task<string[]> FetchPageLabelsAsync(string pageId)
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", GetAuthToken());
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var url = $"{BaseUrl}/rest/api/content/{pageId}/label";
        var response = await httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var results = doc.RootElement.GetProperty("results");
        return results.EnumerateArray()
            .Select(e => e.GetProperty("name").GetString())
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .ToArray();
    }

    public async Task<(string id, string type)[]> FetchChildNodeIdsAndTypesV2Async(string nodeId, string nodeType)
    {
        var allChildren = new System.Collections.Generic.List<(string, string)>();
        string? cursor = null;
        string endpoint = nodeType == "folder"
            ? $"{BaseUrl}/api/v2/folders/{nodeId}/direct-children?limit=100"
            : $"{BaseUrl}/api/v2/pages/{nodeId}/direct-children?limit=100";
        do
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", GetAuthToken());
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var url = endpoint;
            if (!string.IsNullOrEmpty(cursor))
                url += $"&cursor={Uri.EscapeDataString(cursor)}";
            var response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var results = doc.RootElement.GetProperty("results");
            foreach (var child in results.EnumerateArray())
            {
                var id = child.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                var type = child.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;
                var status = child.TryGetProperty("status", out var statusProp) ? statusProp.GetString() : null;
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(type) && status == "current")
                    allChildren.Add((id!, type!));
            }
            cursor = doc.RootElement.TryGetProperty("_links", out var links) && links.TryGetProperty("next", out var next)
                ? next.GetString()
                : null;
        } while (!string.IsNullOrEmpty(cursor));
        return allChildren.ToArray();
    }

    public async Task FetchAndSavePageRecursive(string nodeId, string dataDir, string? nodeType = null)
    {
        Directory.CreateDirectory(dataDir);
        nodeType ??= "page";
        if (nodeType is not "page" and not "folder")
        {
            Console.WriteLine($"Skipping unsupported node type '{nodeType}' for node {nodeId}");
            return;
        }
        if (nodeType == "page")
        {
            try
            {
                var pageContent = await FetchConfluencePageAsync(nodeId);
                var labels = await FetchPageLabelsAsync(nodeId);
                using var doc = JsonDocument.Parse(pageContent);
                using var stream = new MemoryStream();
                using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    writer.WriteStartObject();
                    foreach (var property in doc.RootElement.EnumerateObject())
                    {
                        property.WriteTo(writer);
                    }
                    writer.WritePropertyName("labels");
                    writer.WriteStartArray();
                    foreach (var label in labels)
                    {
                        writer.WriteStringValue(label);
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                stream.Position = 0;
                string title = doc.RootElement.GetProperty("title").GetString() ?? "untitled";
                foreach (var c in Path.GetInvalidFileNameChars())
                    title = title.Replace(c, '_');
                string filePath = Path.Combine(dataDir, $"{nodeId}_{title}.json");
                using (var fileStream = File.Create(filePath))
                {
                    await stream.CopyToAsync(fileStream);
                }
                Console.WriteLine($"Saved node to: {filePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching or saving page node {nodeId}: {ex.Message}");
                AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
                throw;
            }
        }
        else
        {
            Console.WriteLine($"Skipping save for folder node {nodeId}, recursing into children...");
        }
        var childNodes = await FetchChildNodeIdsAndTypesV2Async(nodeId, nodeType);
        foreach (var (childId, childType) in childNodes)
        {
            try
            {
                await FetchAndSavePageRecursive(childId, dataDir, childType);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching child node {childId}: {ex.Message}");
                AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
            }
        }
    }
}

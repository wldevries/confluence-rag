namespace ConfluenceRag;

public interface IConfluenceFetcher
{
    Task<string> FetchConfluencePageAsync(string pageId);
    Task<string[]> FetchChildPageIdsAsync(string pageId);
    Task<string[]> FetchPageLabelsAsync(string pageId);
    Task<(string id, string type)[]> FetchChildNodeIdsAndTypesV2Async(string nodeId, string nodeType);
    Task FetchAndSavePageRecursive(string nodeId, string dataDir, string? nodeType = null);
    Task FetchAllUsersAsync(string dataDir);
}

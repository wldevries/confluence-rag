namespace ConfluenceRag;

public class ConfluenceChunkerOptions
{
    public string PagesDir { get; set; } = string.Empty;
    public string OutputDir { get; set; } = string.Empty;
    public string PeoplePath { get; set; } = string.Empty;
    public int MaxChunkSize { get; set; } = 1000;
    public int OverlapSize { get; set; } = 100;
}

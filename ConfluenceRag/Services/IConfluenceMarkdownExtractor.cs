namespace ConfluenceRag.Services;

public interface IConfluenceMarkdownExtractor
{
    List<string> ExtractMarkdown(string xml);
}

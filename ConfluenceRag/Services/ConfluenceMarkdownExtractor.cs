using System.IO.Abstractions;
using System.Text.Json;
using System.Xml.Linq;
using ConfluenceRag.Models;
using Microsoft.Extensions.Logging;

namespace ConfluenceRag.Services;

public class ConfluenceMarkdownExtractor : IConfluenceMarkdownExtractor
{
    private const string ResourceIdentifierNamespace = "http://atlassian.com/ri";
    private const string AtlassianCloudNamespace = "http://atlassian.com/ac";
    private readonly ILogger<ConfluenceMarkdownExtractor> _logger;
    private readonly IFileSystem _fileSystem;

    // Track current list indentation level for recursive extraction
    private int _currentListIndentLevel = 0;
    private Dictionary<string, string> _userKeyToDisplayName;

    public ConfluenceMarkdownExtractor(
        ILogger<ConfluenceMarkdownExtractor> logger,
        IFileSystem fileSystem,
        ConfluenceChunkerOptions options)
    {
        _logger = logger;
        _fileSystem = fileSystem;

        // Try to load people.json if it exists
        if (_fileSystem.File.Exists(options.PeoplePath))
        {
            try
            {
                var json = _fileSystem.File.ReadAllText(options.PeoplePath);
                var arr = JsonDocument.Parse(json).RootElement;
                _userKeyToDisplayName = new Dictionary<string, string>();
                foreach (var user in arr.EnumerateArray())
                {
                    var accountId = user.TryGetProperty("accountId", out var idProp) ? idProp.GetString() : null;
                    var displayName = user.TryGetProperty("displayName", out var dnProp) ? dnProp.GetString() : null;
                    if (!string.IsNullOrEmpty(accountId) && !string.IsNullOrEmpty(displayName))
                        _userKeyToDisplayName[accountId] = displayName;
                }
            }
            catch
            {
                _userKeyToDisplayName = new Dictionary<string, string>();
            }
        }
        else
        {
            _userKeyToDisplayName = new Dictionary<string, string>();
        }
    }

    public List<string> ExtractMarkdown(string xml)
    {
        try
        {
            // Replace HTML entities before parsing
            xml = HtmlEntityReplacer.ReplaceHtmlEntitiesWithUtf8(xml);
            var doc = XDocument.Parse($"<root xmlns:ac=\"{AtlassianCloudNamespace}\" xmlns:ri=\"{ResourceIdentifierNamespace}\">" + xml + "</root>");
            var root = doc.Root;
            if (root == null)
                return [];

            string[] headingLevels = new string[6];
            var lines = new List<string>();
            var nodeHeadings = new List<string[]>();
            foreach (var node in root.Elements())
            {
                // Skip top-level roadmap macros
                if (node.Name.LocalName == "structured-macro")
                {
                    var macroName = node.Attribute(XName.Get("name", AtlassianCloudNamespace))?.Value ?? string.Empty;
                    if (macroName == "roadmap" || macroName == "roadmap-planner")
                    {
                        continue;
                    }
                }

                // Only handle headings specially, everything else is delegated to ExtractConfluenceContentFromXElement
                if (node.Name.LocalName.Length == 2 && node.Name.LocalName[0] == 'h' && char.IsDigit(node.Name.LocalName[1]))
                {
                    // Heading (h1-h6)
                    int nodeHeadingLevel = int.Parse(node.Name.LocalName.Substring(1, 1));
                    var headingLines = this.ExtractConfluenceContentFromXElement(node).ToList();
                    if (headingLines.Count == 0) continue;
                    // Only the first line is used for heading context
                    var headingText = headingLines[0];
                    var headingLine = new string('#', nodeHeadingLevel) + " " + headingText;
                    lines.Add(headingLine);
                    nodeHeadings.Add([.. headingLevels]);
                    // Update heading context
                    headingLevels[nodeHeadingLevel - 1] = headingText;
                    for (int i = nodeHeadingLevel; i < headingLevels.Length; i++)
                        headingLevels[i] = string.Empty;
                }
                else
                {
                    // For all other elements, just extract lines and add with current heading context
                    foreach (var line in this.ExtractConfluenceContentFromXElement(node))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        lines.Add(line.TrimEnd('\r', '\n'));
                        nodeHeadings.Add([.. headingLevels]);
                    }
                }
            }
            return lines;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract markdown from XML");
            return [];
        }
    }


    // Recursively extract readable text from an XElement, handling macros, tables, and links
    private IEnumerable<string> ExtractConfluenceContentFromXElement(XElement node)
    {
        if (!node.HasElements)
        {
            if (!string.IsNullOrWhiteSpace(node.Value))
                yield return node.Value;
            yield break;
        }

        int olCounter = 1;

        // Use indexed iteration to allow lookahead
        var childNodes = node.Nodes().ToList();
        for (int idx = 0; idx < childNodes.Count; idx++)
        {
            var el = childNodes[idx];
            if (el is XElement child)
            {
                if (child.Name.NamespaceName == AtlassianCloudNamespace)
                {
                    foreach (var line in ExtractFromAtlassionConfluenceElement(child))
                    {
                        yield return line;
                    }
                    continue;
                }

                if (child.Name.NamespaceName == ResourceIdentifierNamespace)
                {
                    foreach (var line in ExtractFromResourceIdentifierElement(child))
                    {
                        yield return line;
                    }
                    continue;
                }

                switch (child.Name.LocalName)
                {
                    case "time":
                        // Output <time datetime="..."></time> as readable date
                        var datetimeAttr = child.Attribute("datetime")?.Value;
                        if (!string.IsNullOrWhiteSpace(datetimeAttr))
                        {
                            if (DateTime.TryParse(datetimeAttr, out var parsedDate))
                                yield return $"Date: {parsedDate:yyyy-MM-dd}";
                            else
                                yield return $"Date: {datetimeAttr}";
                        }
                        else
                        {
                            yield return "Date: [Not specified]";
                        }
                        break;

                    case "hr":
                        yield return "---"; // Horizontal rule
                        break;

                    // Should be handled at the top level in ExtractTextChunksWithHeadings
                    case "h1":
                    case "h2":
                    case "h3":
                    case "h4":
                    case "h5":
                    case "h6":
                        foreach (var line in ExtractConfluenceContentFromXElement(child))
                            yield return line;
                        break;

                    case "p":
                    case "div":
                        foreach (var line in ExtractConfluenceContentFromXElement(child))
                            yield return line;
                        yield return ""; // Add a blank line after paragraphs/divs
                        break;

                    case "span":
                        var style = child.Attribute("style")?.Value;
                        if (style != null && style.Contains("text-decoration") && style.Contains("line-through"))
                        {
                            foreach (var line in ExtractConfluenceContentFromXElement(child))
                                yield return "~~" + line + "~~";
                        }
                        else
                        {
                            foreach (var line in ExtractConfluenceContentFromXElement(child))
                                yield return line;
                        }
                        break;
                    case "blockquote":
                        foreach (var line in ExtractConfluenceContentFromXElement(child))
                            yield return "> " + line.Replace("\n", "\n> ");
                        break;
                    case "a":
                        var href = child.Attribute("href")?.Value;
                        var linkLines = this.ExtractConfluenceContentFromXElement(child).ToArray();
                        if (href != null)
                            yield return $"[{string.Join(" ", linkLines)}]({href})";
                        else
                            foreach (var line in linkLines)
                                yield return line;
                        break;
                    case "em":
                    case "i":
                    case "u":
                        foreach (var line in this.ExtractConfluenceContentFromXElement(child))
                            yield return "*" + line.Trim() + "*";
                        break;
                    case "strong":
                    case "b":
                        foreach (var line in ExtractConfluenceContentFromXElement(child))
                            yield return "**" + line.Trim() + "**";
                        break;
                    case "s":
                    case "del":
                        foreach (var line in ExtractConfluenceContentFromXElement(child))
                            yield return "~~" + line.Trim() + "~~";
                        break;
                    case "sup":
                        foreach (var line in ExtractConfluenceContentFromXElement(child))
                            yield return "^" + line.Trim() + "^";
                        break;
                    case "sub":
                        foreach (var line in ExtractConfluenceContentFromXElement(child))
                            yield return "~" + line.Trim() + "~";
                        break;
                    case "code":
                    case "pre":
                        foreach (var line in ExtractConfluenceContentFromXElement(child))
                            yield return "`" + line.Trim() + "`";
                        break;
                    case "br":
                        yield return ""; // blank line
                        break;

                    case "table":
                        // Only treat rows with <th> as header rows. Do not use <colgroup> for header detection.
                        var headerRows = new List<XElement>();
                        var bodyRows = new List<XElement>();
                        var thead = child.Elements().FirstOrDefault(e => e.Name.LocalName == "thead");
                        var tbody = child.Elements().FirstOrDefault(e => e.Name.LocalName == "tbody");
                        var tfoot = child.Elements().FirstOrDefault(e => e.Name.LocalName == "tfoot");

                        // If thead exists, use its rows as header
                        if (thead != null)
                            headerRows.AddRange(thead.Elements().Where(e => e.Name.LocalName == "tr"));
                        // If tbody exists, use its rows as body
                        if (tbody != null)
                            bodyRows.AddRange(tbody.Elements().Where(e => e.Name.LocalName == "tr"));
                        // Add tfoot rows to body (optional)
                        if (tfoot != null)
                            bodyRows.AddRange(tfoot.Elements().Where(e => e.Name.LocalName == "tr"));
                        // If no tbody/thead, just get all tr under table
                        if (headerRows.Count == 0 && bodyRows.Count == 0)
                        {
                            var allRows = child.Elements().Where(e => e.Name.LocalName == "tr").ToList();
                            if (allRows.Count > 0)
                            {
                                // If first row has th, treat as header
                                if (allRows[0].Elements().Any(c => c.Name.LocalName == "th"))
                                {
                                    headerRows.Add(allRows[0]);
                                    bodyRows.AddRange(allRows.Skip(1));
                                }
                                else
                                {
                                    // All rows are body rows
                                    bodyRows.AddRange(allRows);
                                }
                            }
                        }

                        if (headerRows.Count == 0)
                        {
                            // No header row: synthesize a header row with numbers (1, 2, 3, ...)
                            var firstRow = bodyRows[0];
                            var cellCount = firstRow.Elements().Count(e => e.Name.LocalName == "th" || e.Name.LocalName == "td");
                            // Synthesize a header row XElement
                            var headerRow = new XElement("tr",
                                Enumerable.Range(1, cellCount).Select(n => new XElement("th", $"Column {n}"))
                            );
                            headerRows.Add(headerRow);
                        }

                        // Output header rows
                        bool headerDone = false;
                        foreach (var row in headerRows)
                        {
                            var cells = row.Elements().Where(e => e.Name.LocalName == "th" || e.Name.LocalName == "td")
                                .Select(e => string.Join(" ", ExtractConfluenceContentFromXElement(e)).Trim().Replace("\n", " "));
                            yield return "| " + string.Join(" | ", cells) + " |";
                            headerDone = true;
                        }
                        // If header present, add markdown separator
                        if (headerDone)
                        {
                            var headerCellCount = headerRows.First().Elements().Count(e => e.Name.LocalName == "th" || e.Name.LocalName == "td");
                            yield return "|" + string.Join("|", Enumerable.Repeat(" --- ", headerCellCount)) + "|";
                        }
                        // Output body rows
                        foreach (var row in bodyRows)
                        {
                            var cells = row.Elements().Where(e => e.Name.LocalName == "th" || e.Name.LocalName == "td")
                                .Select(e => string.Join(" ", ExtractConfluenceContentFromXElement(e)).Trim().Replace("\n", " "));
                            yield return "| " + string.Join(" | ", cells) + " |";
                        }
                        break;

                    // All table-related elements are handled above
                    case "tr":
                    case "tbody":
                    case "thead":
                    case "tfoot":
                    case "colgroup":
                        break;
                    case "col":
                    case "th":
                    case "td":
                        foreach (var line in ExtractConfluenceContentFromXElement(child))
                            yield return line;
                        break;

                    case "ul":
                    case "ol":
                        _currentListIndentLevel++;
                        foreach (var line in ExtractConfluenceContentFromXElement(child))
                            yield return line;
                        _currentListIndentLevel--;
                        // Look ahead: if next sibling is not a ul/ol, emit a blank line
                        bool nextIsList = false;
                        if (idx + 1 < childNodes.Count)
                        {
                            var next = childNodes[idx + 1] as XElement;
                            if (next != null && (next.Name.LocalName == "ul" || next.Name.LocalName == "ol"))
                                nextIsList = true;
                        }
                        if (!nextIsList)
                            yield return string.Empty;
                        break;

                    case "li":
                        var liLines = ExtractConfluenceContentFromXElement(child).ToList();
                        if (liLines.Count > 0)
                        {
                            string indent = new(' ', _currentListIndentLevel * 2);
                            if (node.Name.LocalName == "ol")
                            {
                                // Ordered list item
                                yield return $"{indent}{olCounter++}. {liLines[0]}";
                            }
                            else
                            {
                                // Unordered list item
                                yield return $"{indent}- {liLines[0]}";
                            }
                            for (int i = 1; i < liLines.Count; i++)
                            {
                                yield return liLines[i];
                            }
                        }
                        break;
                }
            }
            else if (el is XText textNode)
            {
                if (!string.IsNullOrWhiteSpace(textNode.Value))
                    yield return textNode.Value;
            }
        }
    }

    private IEnumerable<string> ExtractFromResourceIdentifierElement(XElement child)
    {
        switch (child.Name.LocalName)
        {
            case "shortcut":
                // <ri:shortcut ri:key="jira" ri:parameter="ABC-123">
                var shortcutKey = child.Attribute(XName.Get("key", ResourceIdentifierNamespace))?.Value;
                if (shortcutKey == "jira")
                {
                    var shortcutParam = child.Attribute(XName.Get("parameter", ResourceIdentifierNamespace))?.Value;
                    if (!string.IsNullOrWhiteSpace(shortcutParam))
                        yield return $"[Shortcut: {shortcutParam.Trim()}]";
                    else
                        yield return "[Shortcut: Jira]";
                }
                break;
            case "user":
                var accountId = child.Attribute(XName.Get("account-id", ResourceIdentifierNamespace))?.Value;
                var userKey = child.Attribute(XName.Get("userkey", ResourceIdentifierNamespace))?.Value;
                if (!string.IsNullOrEmpty(accountId) && _userKeyToDisplayName.TryGetValue(accountId, out var displayName))
                    yield return $"User: {displayName}";
                else if (!string.IsNullOrEmpty(accountId))
                    yield return $"User ID: {accountId}";
                else if (!string.IsNullOrEmpty(userKey) && _userKeyToDisplayName.TryGetValue(userKey, out var displayName2))
                    yield return $"User: {displayName2}";
                else if (!string.IsNullOrEmpty(userKey))
                    yield return $"User Key: {userKey}";
                break;
            case "page":
                var pageTitle = child.Attribute(XName.Get("content-title", ResourceIdentifierNamespace))?.Value;
                var pageIdAttr = child.Attribute(XName.Get("content-id", ResourceIdentifierNamespace))?.Value;

                if (!string.IsNullOrEmpty(pageTitle))
                {
                    if (!string.IsNullOrEmpty(pageIdAttr))
                        yield return $"Link to: \"{pageTitle}\" (Page ID: {pageIdAttr})";
                    else
                        yield return $"Link to: \"{pageTitle}\"";
                }
                else if (!string.IsNullOrEmpty(pageIdAttr))
                    yield return $"Link to page ID: {pageIdAttr}";
                else
                    yield return "Page link";
                break;
            default:
                foreach (var line in ExtractConfluenceContentFromXElement(child))
                    yield return line;
                break;
        }
    }

    private IEnumerable<string> ExtractFromAtlassionConfluenceElement(XElement child)
    {
        switch (child.Name.LocalName)
        {
            case "link":
                // <ac:link ac:anchor="anchor">
                var anchor = child.Attribute(XName.Get("anchor", AtlassianCloudNamespace))?.Value;
                if (!string.IsNullOrWhiteSpace(anchor))
                    yield break; // Skip links with anchors, they are not useful in text extraction

                foreach (var line in ExtractConfluenceContentFromXElement(child))
                {
                    yield return line;
                }
                break;

            case "placeholder":
                var placeholderText = child.Value?.Trim();
                if (!string.IsNullOrWhiteSpace(placeholderText))
                    yield return $"> Placeholder: {placeholderText}";
                break;

            case "jira":
                var jiraIssueKey = child.Elements().FirstOrDefault(e => e.Name.LocalName == "param" && (string?)e.Attribute(XName.Get("name", AtlassianCloudNamespace)) == "key")?.Value;
                var jiraFilter = child.Elements().FirstOrDefault(e => e.Name.LocalName == "param" && (string?)e.Attribute(XName.Get("name", AtlassianCloudNamespace)) == "filter")?.Value;
                string jiraSummary = string.Empty;
                if (!string.IsNullOrWhiteSpace(jiraIssueKey))
                    jiraSummary = $"Jira Issue: {jiraIssueKey}";
                else if (!string.IsNullOrWhiteSpace(jiraFilter))
                    jiraSummary = $"Jira Filter: {jiraFilter}";
                else
                    jiraSummary = "Jira reference";
                yield return jiraSummary;
                break;

            case "emoticon":
                var emoticonName = child.Attribute(XName.Get("name", AtlassianCloudNamespace))?.Value;
                if (!string.IsNullOrWhiteSpace(emoticonName))
                    yield return $":{emoticonName}:";
                else
                    yield return ":emoticon:";
                break;

            case "rich-text-body":
                // Handle rich-text-body as a special case
                foreach (var line in ExtractConfluenceContentFromXElement(child))
                    yield return line;
                break;

            case "structured-macro":
                foreach (var line in ExtractStructuredMacroContent(child))
                    yield return line;
                break;
        }
    }

    private IEnumerable<string> ExtractStructuredMacroContent(XElement child)
    {
        var macroName = child.Attribute(XName.Get("name", AtlassianCloudNamespace))?.Value ?? "macro";
        switch (macroName)
        {
            case "status":
                // Extract only the status title/text, disregard color
                var statusTitleParam = child.Elements().FirstOrDefault(e => e.Name.LocalName == "parameter" && (string?)e.Attribute(XName.Get("name", AtlassianCloudNamespace)) == "title")?.Value;
                if (!string.IsNullOrWhiteSpace(statusTitleParam))
                {
                    var statusText = statusTitleParam.Trim();
                    // Capitalize first letter for better readability
                    statusText = char.ToUpper(statusText[0]) + statusText.Substring(1);
                    yield return $"Status: {statusText}";
                }
                else
                {
                    // fallback: try rich-text-body or child.Value
                    var statusTitle = child.Elements().FirstOrDefault(e => e.Name.LocalName == "rich-text-body")?.Value ?? child.Value;
                    if (!string.IsNullOrWhiteSpace(statusTitle))
                    {
                        var statusText = statusTitle.Trim();
                        statusText = char.ToUpper(statusText[0]) + statusText.Substring(1);
                        yield return $"Status: {statusText}";
                    }
                }
                break;
            case "date":
            case "datetime":
                // Output date macro value if present
                var dateParam = child.Elements().FirstOrDefault(e => e.Name.LocalName == "parameter" && (string?)e.Attribute(XName.Get("name", AtlassianCloudNamespace)) == "date")?.Value;
                if (!string.IsNullOrWhiteSpace(dateParam))
                {
                    if (DateTime.TryParse(dateParam.Trim(), out var parsedDate))
                        yield return $"Date: {parsedDate:yyyy-MM-dd}";
                    else
                        yield return $"Date: {dateParam.Trim()}";
                }
                else
                {
                    yield return "Date: [Not specified]";
                }
                break;
            case "roadmap":
            case "roadmap-planner":
                // Skip roadmap macro content (these are large JSON blobs not useful for RAG)
                yield return "[Roadmap macro omitted]";
                break;
            case "info":
            case "note":
            case "tip":
            case "warning":
            case "error":
                var macroBody = child.Elements().FirstOrDefault(e => e.Name.LocalName == "rich-text-body");
                var macroContent = macroBody != null ? ExtractConfluenceContentFromXElement(macroBody) : ExtractConfluenceContentFromXElement(child);
                foreach (var line in macroContent)
                    yield return $"> **{macroName.ToUpperInvariant()}:** {line.Trim()}";
                break;
            case "panel":
                var panelBody = child.Elements().FirstOrDefault(e => e.Name.LocalName == "rich-text-body");
                var panelContent = panelBody != null ? ExtractConfluenceContentFromXElement(panelBody) : ExtractConfluenceContentFromXElement(child);
                foreach (var line in panelContent)
                    yield return $"> **Panel:** {line.Trim()}";
                break;
            case "excerpt":
            case "excerpt-include":
                var excerptBody = child.Elements().FirstOrDefault(e => e.Name.LocalName == "rich-text-body");
                var excerptContent = excerptBody != null ? ExtractConfluenceContentFromXElement(excerptBody) : ExtractConfluenceContentFromXElement(child);
                foreach (var line in excerptContent)
                    yield return line;
                break;
            case "expand":
            case "details":
                var expandTitle = child.Elements().FirstOrDefault(e => e.Name.LocalName == "title")?.Value ?? "Expand";
                var expandBody = child.Elements().FirstOrDefault(e => e.Name.LocalName == "rich-text-body");
                yield return $"> **Expand: {expandTitle.Trim()}**";
                if (expandBody != null)
                {
                    // Recursively extract all content from rich-text-body, including tables and nested macros
                    foreach (var line in ExtractConfluenceContentFromXElement(expandBody))
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                            yield return $"> {line.Trim().Replace("\n", "\n> ")}";
                    }
                }
                else
                {
                    // Fallback: extract from the macro node itself
                    foreach (var line in ExtractConfluenceContentFromXElement(child))
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                            yield return $"> {line.Trim().Replace("\n", "\n> ")}";
                    }
                }
                break;
            case "link":
                foreach (var line in ExtractConfluenceContentFromXElement(child))
                    yield return line;
                break;
            default:
                yield return $"[Structured macro removed: {macroName}]";
                break;
        }
    }
}
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
                foreach (var line in this.ExtractFromRootElement(node, headingLevels))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    lines.Add(line.TrimEnd('\r', '\n'));
                    nodeHeadings.Add([.. headingLevels]);
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

    // Extract content from a root-level element, handling special cases like headings
    private IEnumerable<string> ExtractFromRootElement(XElement node, string[] headingLevels)
    {
        // Handle headings specially for heading context tracking
        if (node.Name.LocalName.Length == 2 && node.Name.LocalName[0] == 'h' && char.IsDigit(node.Name.LocalName[1]))
        {
            // Heading (h1-h6)
            int nodeHeadingLevel = int.Parse(node.Name.LocalName.Substring(1, 1));
            var headingLines = this.ExtractChildrenContent(node).ToList();
            if (headingLines.Count == 0) yield break;
            
            // Only the first line is used for heading context
            var headingText = headingLines[0];
            var headingLine = new string('#', nodeHeadingLevel) + " " + headingText;
            yield return headingLine;
            
            // Update heading context
            headingLevels[nodeHeadingLevel - 1] = headingText;
            for (int i = nodeHeadingLevel; i < headingLevels.Length; i++)
                headingLevels[i] = string.Empty;
        }
        else
        {
            // For all other elements, process through ExtractFromElement
            foreach (var line in this.ExtractFromElement(node))
            {
                yield return line;
            }
        }
    }

    // Extract content from an element based on its type, then extract content from children if needed
    private IEnumerable<string> ExtractFromElement(XElement node)
    {
        // Handle namespace-specific elements first
        if (node.Name.NamespaceName == AtlassianCloudNamespace)
        {
            foreach (var line in ExtractFromAtlassionConfluenceElement(node))
            {
                yield return line;
            }
            yield break;
        }

        if (node.Name.NamespaceName == ResourceIdentifierNamespace)
        {
            foreach (var line in ExtractFromResourceIdentifierElement(node))
            {
                yield return line;
            }
            yield break;
        }

        // Handle block elements that need special processing
        switch (node.Name.LocalName)
        {
            case "p":
            case "div":
                var paragraphParts = new List<string>();
                foreach (var childNode in node.Nodes())
                {
                    if (childNode is XElement childElement)
                    {
                        foreach (var part in ExtractFromElement(childElement))
                        {
                            paragraphParts.Add(part);
                        }
                    }
                    else if (childNode is XText textNode && !string.IsNullOrWhiteSpace(textNode.Value))
                    {
                        paragraphParts.Add(textNode.Value);
                    }
                }
                var paragraphContent = string.Join("", paragraphParts)
                    .Trim()
                    .Replace("\n", " ")
                    .Replace("\r", " ");
                if (!string.IsNullOrEmpty(paragraphContent))
                    yield return paragraphContent;
                yield return ""; // Add a blank line after paragraphs/divs
                yield break;
                
            case "em":
            case "i":
            case "u":
            case "strong":
            case "b":
            case "s":
            case "del":
            case "sup":
            case "sub":
            case "code":
            case "pre":
            case "time":
                var inlineResult = ExtractFromInlineElement(node);
                if (inlineResult != null)
                    yield return inlineResult;
                yield break;
                
            case "table":
                foreach (var line in ProcessTableElement(node))
                    yield return line;
                yield break;
                
            case "ul":
            case "ol":
                _currentListIndentLevel++;
                // Process list items with proper numbering for ordered lists
                var listItems = node.Elements("li").ToList();
                for (int i = 0; i < listItems.Count; i++)
                {
                    var liElement = listItems[i];
                    var liContentParts = ExtractChildrenContent(liElement).ToList();
                    var liContent = string.Join(' ', liContentParts.Where(part => !string.IsNullOrWhiteSpace(part)));
                    
                    // Only add indentation for nested lists (indent level > 1)
                    string indent = _currentListIndentLevel > 1 ? new(' ', (_currentListIndentLevel - 1) * 2) : "";
                    
                    if (node.Name.LocalName == "ol")
                    {
                        yield return $"{indent}{i + 1}. {liContent}";
                    }
                    else
                    {
                        yield return $"{indent}- {liContent}";
                    }
                }
                _currentListIndentLevel--;
                yield return ""; // Add blank line after lists
                yield break;
                
            case "li":
                // Skip processing individual li elements if they're being processed by their parent ol/ul
                if (node.Parent?.Name.LocalName == "ol" || node.Parent?.Name.LocalName == "ul")
                {
                    yield break;
                }
                
                // Handle standalone li elements (shouldn't normally happen)
                var standaloneContent = string.Join(' ', ExtractChildrenContent(node));
                yield return $"- {standaloneContent}";
                yield break;
                
            case "blockquote":
                foreach (var line in ExtractChildrenContent(node))
                    yield return "> " + line.Replace("\n", "\n> ");
                yield break;
                
            case "hr":
                yield return "---";
                yield break;
                
            case "br":
                yield return "";
                yield break;
                
                
            case "a":
                var href = node.Attribute("href")?.Value;
                var linkLines = ExtractChildrenContent(node).ToArray();
                if (href != null)
                    yield return $"[{string.Join(" ", linkLines)}]({href})";
                else
                    foreach (var line in linkLines)
                        yield return line;
                yield break;
                
            case "span":
                var style = node.Attribute("style")?.Value;
                if (style != null && style.Contains("text-decoration") && style.Contains("line-through"))
                {
                    foreach (var line in ExtractChildrenContent(node))
                        yield return "~~" + line + "~~";
                }
                else
                {
                    foreach (var line in ExtractChildrenContent(node))
                        yield return line;
                }
                yield break;
                
            default:
                // For other elements, process children
                foreach (var line in ExtractChildrenContent(node))
                {
                    yield return line;
                }
                yield break;
        }
    }

    // Extract content from the children of an XElement
    private IEnumerable<string> ExtractChildrenContent(XElement node)
    {
        // Handle inline elements that have no child elements but need formatting
        if (!node.HasElements)
        {
            var inlineResult = ExtractFromInlineElement(node);
            if (inlineResult != null)
            {
                yield return inlineResult;
                yield break;
            }
            
            // For other elements without children, just return the text content
            if (!string.IsNullOrWhiteSpace(node.Value))
                yield return node.Value;
            yield break;
        }

        // Process all child nodes
        foreach (var childNode in node.Nodes())
        {
            if (childNode is XElement childElement)
            {
                foreach (var line in ExtractFromElement(childElement))
                {
                    yield return line;
                }
            }
            else if (childNode is XText textNode && !string.IsNullOrWhiteSpace(textNode.Value))
            {
                yield return textNode.Value;
            }
        }
    }

    private string? ExtractFromInlineElement(XElement element)
    {
        string content;
        
        if (!element.HasElements)
        {
            // Simple case: element has only text content
            content = element.Value.Trim();
        }
        else
        {
            // Complex case: element has child elements, process them recursively
            content = string.Join(" ", ExtractChildrenContent(element))
                .Trim()
                .Replace("\n", " ")
                .Replace("\r", " ");
        }
        
        if (string.IsNullOrEmpty(content))
            return null;
            
        return element.Name.LocalName switch
        {
            "em" or "i" or "u" => "*" + content + "*",
            "strong" or "b" => "**" + content + "**",
            "s" or "del" => "~~" + content + "~~",
            "sup" => "^" + content + "^",
            "sub" => "~" + content + "~",
            "code" or "pre" => "`" + content + "`",
            "time" => FormatTimeElement(element),
            _ => null
        };
    }

    private string FormatTimeElement(XElement element)
    {
        var datetimeAttr = element.Attribute("datetime")?.Value;
        if (!string.IsNullOrWhiteSpace(datetimeAttr))
        {
            if (DateTime.TryParse(datetimeAttr, out var parsedDate))
                return $"Date: {parsedDate:yyyy-MM-dd}";
            else
                return $"Date: {datetimeAttr}";
        }
        else
        {
            return "Date: [Not specified]";
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
                        yield return $"Jira Issue: {shortcutParam.Trim()}";
                    else
                        yield return "Jira Issue: [Unknown]";
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
                foreach (var line in ExtractChildrenContent(child))
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

                foreach (var line in ExtractChildrenContent(child))
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
                foreach (var line in ExtractChildrenContent(child))
                    yield return line;
                break;
            
            case "plain-text-body":
                // Handle plain-text-body for code blocks
                var codeContent = child.Value.Trim();
                if (!string.IsNullOrEmpty(codeContent))
                {
                    // Check if parent is a structured macro with name="code"
                    var parentMacro = child.Parent;
                    var isCodeMacro = parentMacro?.Name.LocalName == "structured-macro" && 
                                     parentMacro.Attribute(XName.Get("name", AtlassianCloudNamespace))?.Value == "code";
                    
                    if (isCodeMacro && parentMacro != null)
                    {
                        // Extract language parameter from parent macro
                        var languageParam = parentMacro.Elements().FirstOrDefault(e => 
                            e.Name.LocalName == "parameter" && 
                            (string?)e.Attribute(XName.Get("name", AtlassianCloudNamespace)) == "language")?.Value;
                        var language = !string.IsNullOrWhiteSpace(languageParam) ? languageParam.Trim() : "";
                        
                        // Format as markdown code block
                        yield return $"```{language}";
                        foreach (var line in codeContent.Split('\n'))
                        {
                            yield return line.TrimEnd('\r');
                        }
                        yield return "```";
                    }
                    else
                    {
                        // Treat as regular text content
                        yield return codeContent;
                    }
                }
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
            case "jira":
                // Extract Jira issue key and title if present
                var jiraKey = child.Elements().FirstOrDefault(e => e.Name.LocalName == "parameter" && (string?)e.Attribute(XName.Get("name", AtlassianCloudNamespace)) == "key")?.Value;
                var jiraTitle = child.Elements().FirstOrDefault(e => e.Name.LocalName == "parameter" && (string?)e.Attribute(XName.Get("name", AtlassianCloudNamespace)) == "title")?.Value;
                if (!string.IsNullOrWhiteSpace(jiraKey))
                {
                    if (!string.IsNullOrWhiteSpace(jiraTitle))
                        yield return $"Jira Issue: {jiraKey} - {jiraTitle.Trim()}";
                    else
                        yield return $"Jira Issue: {jiraKey}";
                }
                else
                {
                    yield return "Jira reference";
                }
                break;
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
                var macroContent = macroBody != null ? ExtractChildrenContent(macroBody) : ExtractChildrenContent(child);
                foreach (var line in macroContent)
                    yield return $"> **{macroName.ToUpperInvariant()}:** {line.Trim()}";
                break;
            case "panel":
                var panelBody = child.Elements().FirstOrDefault(e => e.Name.LocalName == "rich-text-body");
                var panelContent = panelBody != null ? ExtractChildrenContent(panelBody) : ExtractChildrenContent(child);
                foreach (var line in panelContent)
                    yield return $"> **Panel:** {line.Trim()}";
                break;
            case "excerpt":
            case "excerpt-include":
                var excerptBody = child.Elements().FirstOrDefault(e => e.Name.LocalName == "rich-text-body");
                var excerptContent = excerptBody != null ? ExtractChildrenContent(excerptBody) : ExtractChildrenContent(child);
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
                    foreach (var line in ExtractChildrenContent(expandBody))
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                            yield return $"> {line.Trim().Replace("\n", "\n> ")}";
                    }
                }
                else
                {
                    // Fallback: extract from the macro node itself
                    foreach (var line in ExtractChildrenContent(child))
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                            yield return $"> {line.Trim().Replace("\n", "\n> ")}";
                    }
                }
                break;
            case "link":
                foreach (var line in ExtractChildrenContent(child))
                    yield return line;
                break;
            case "code":
                // Extract language parameter if present
                var languageParam = child.Elements().FirstOrDefault(e => e.Name.LocalName == "parameter" && (string?)e.Attribute(XName.Get("name", AtlassianCloudNamespace)) == "language")?.Value;
                var language = !string.IsNullOrWhiteSpace(languageParam) ? languageParam.Trim() : "";
                
                // Extract code content from plain-text-body
                var plainTextBody = child.Elements().FirstOrDefault(e => e.Name.LocalName == "plain-text-body");
                if (plainTextBody != null)
                {
                    var codeContent = plainTextBody.Value.Trim();
                    if (!string.IsNullOrEmpty(codeContent))
                    {
                        // Format as markdown code block
                        yield return $"```{language}";
                        foreach (var line in codeContent.Split('\n'))
                        {
                            yield return line.TrimEnd('\r');
                        }
                        yield return "```";
                    }
                }
                break;
            default:
                yield return $"[Structured macro removed: {macroName}]";
                break;
        }
    }

    private string ProcessTableCellContent(IEnumerable<string> lines)
    {
        var cellLines = lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Trim())
            .ToList();
        
        if (cellLines.Count <= 1)
            return string.Join(" ", cellLines).Trim();
        
        // For multi-line cells, use HTML <br> tags in markdown tables
        // This preserves line breaks while maintaining table compatibility
        return string.Join("<br>", cellLines);
    }

    private IEnumerable<string> ProcessTableElement(XElement tableElement)
    {
        // Only treat rows with <th> as header rows. Do not use <colgroup> for header detection.
        var headerRows = new List<XElement>();
        var bodyRows = new List<XElement>();
        var thead = tableElement.Elements().FirstOrDefault(e => e.Name.LocalName == "thead");
        var tbody = tableElement.Elements().FirstOrDefault(e => e.Name.LocalName == "tbody");
        var tfoot = tableElement.Elements().FirstOrDefault(e => e.Name.LocalName == "tfoot");

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
            var allRows = tableElement.Elements().Where(e => e.Name.LocalName == "tr").ToList();
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
                .Select(e => ProcessTableCellContent(ExtractChildrenContent(e)));
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
                .Select(e => ProcessTableCellContent(ExtractChildrenContent(e)));
            yield return "| " + string.Join(" | ", cells) + " |";
        }
    }
}
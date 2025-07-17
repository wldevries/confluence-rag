using Microsoft.Extensions.AI;
using System.IO.Abstractions;
using System.Text.Json;

namespace ConfluenceRag;

public class ConfluenceChunker : IConfluenceChunker
{
    private readonly IFileSystem _fileSystem;
    private readonly IConfluenceMarkdownExtractor _extractor;
    private readonly ConfluenceChunkerOptions _options;

    public ConfluenceChunker(
        IFileSystem fileSystem,
        IConfluenceMarkdownExtractor extractor,
        ConfluenceChunkerOptions options)
    {
        _fileSystem = fileSystem;
        _extractor = extractor;
        _options = options;
    }

    public async Task<int> ProcessAllConfluenceJsonAndChunkAsync(string dataDir, string outputDir, IEmbeddingGenerator<string, Embedding<float>> embedder)
    {
        if (!_fileSystem.Directory.Exists(dataDir))
        {
            Console.WriteLine($"Directory not found: {dataDir}");
            return 0;
        }
        var files = _fileSystem.Directory.GetFiles(dataDir, "*.json");
        int chunkCount = 0;
        _fileSystem.Directory.CreateDirectory(outputDir);
        
        string metadataPath = _fileSystem.Path.Combine(outputDir, "metadata.jsonl");
        string embeddingsPath = _fileSystem.Path.Combine(outputDir, "embeddings.bin");
        
        using var metadataStream = _fileSystem.File.CreateText(metadataPath);
        using var embeddingsStream = _fileSystem.File.Create(embeddingsPath);
        
        const int EmbeddingSize = 384;
        const int FloatSize = 4;
        const int EmbeddingByteSize = EmbeddingSize * FloatSize;
        
        foreach (var file in files)
        {
            try
            {
                var chunkRecords = await ProcessSingleConfluenceJsonAndChunkAsync(file, embedder);
                foreach (var chunkObj in chunkRecords)
                {
                    // Write metadata (without embedding)
                    var metadata = chunkObj.Metadata;
                    
                    string metadataJson = JsonSerializer.Serialize(metadata);
                    await metadataStream.WriteLineAsync(metadataJson);
                    
                    // Write embedding as binary data
                    if (chunkObj.Embedding.Length != EmbeddingSize)
                        throw new InvalidOperationException($"Expected {EmbeddingSize} dimensions, got {chunkObj.Embedding.Length}");
                    
                    var embeddingBytes = new byte[EmbeddingByteSize];
                    Buffer.BlockCopy(chunkObj.Embedding, 0, embeddingBytes, 0, EmbeddingByteSize);
                    await embeddingsStream.WriteAsync(embeddingBytes);
                    
                    chunkCount++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing {file}: {ex.Message}");
            }
        }
        
        Console.WriteLine($"Processed {chunkCount} chunks. Output: {metadataPath} and {embeddingsPath}");
        return chunkCount;
    }

    public async Task<List<ConfluenceChunkRecord>> ProcessSingleConfluenceJsonAndChunkAsync(string file, IEmbeddingGenerator<string, Embedding<float>> embedder)
    {
        Console.WriteLine($"Processing file: {file}");
        using var doc = JsonDocument.Parse(await _fileSystem.File.ReadAllTextAsync(file));
        var root = doc.RootElement;
        string title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "untitled" : "untitled";
        string webui = root.TryGetProperty("_links", out var links) && links.TryGetProperty("webui", out var webuiProp) ? webuiProp.GetString() ?? "" : "";
        string chunkPageId = root.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
        string content = "";
        if (root.TryGetProperty("body", out var body) && body.TryGetProperty("storage", out var storage) && storage.TryGetProperty("value", out var value))
            content = value.GetString() ?? "";

        // Extract date information
        DateTime? createdDate = null;
        DateTime? lastModifiedDate = null;

        if (root.TryGetProperty("version", out var version))
        {
            if (version.TryGetProperty("when", out var whenProp) && whenProp.ValueKind == JsonValueKind.String)
            {
                if (DateTime.TryParse(whenProp.GetString(), out var parsedDate))
                    lastModifiedDate = parsedDate.Kind == DateTimeKind.Utc ? parsedDate : parsedDate.ToUniversalTime();
            }
        }

        if (root.TryGetProperty("history", out var history) && history.TryGetProperty("createdDate", out var createdDateProp) && createdDateProp.ValueKind == JsonValueKind.String)
        {
            if (DateTime.TryParse(createdDateProp.GetString(), out var parsedCreatedDate))
                createdDate = parsedCreatedDate.Kind == DateTimeKind.Utc ? parsedCreatedDate : parsedCreatedDate.ToUniversalTime();
        }
        string[] labels = Array.Empty<string>();
        if (root.TryGetProperty("labels", out var labelsProp) && labelsProp.ValueKind == JsonValueKind.Array)
        {
            labels = labelsProp.EnumerateArray()
                .Select(l => l.GetString() ?? string.Empty)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToArray();
        }
        if (string.IsNullOrWhiteSpace(content)) return new List<ConfluenceChunkRecord>();
        // Use the new extractor to get markdown for the whole document
        var markdown = _extractor.ExtractMarkdown(content);
        var chunkList = ChunkMarkdownWithHeadings(markdown, _options.MaxChunkSize, _options.OverlapSize);
        int chunkIdx = 0;
        int totalChunks = chunkList.Count;
        var result = new List<ConfluenceChunkRecord>(totalChunks);
        foreach (var chunk in chunkList)
        {
            Console.WriteLine($"\tEmbedding chunk {chunkIdx + 1}/{totalChunks} for page '{title}' (ID: {chunkPageId})");
            var embeddingMem = await embedder.GenerateAsync(chunk.chunk);
            var embedding = embeddingMem.Vector.ToArray();
            var metadata = new ConfluenceChunkMetadata(
                PageId: chunkPageId,
                WebUI: webui,
                Title: title,
                Labels: labels,
                Headings: chunk.headings,
                ChunkIndex: chunkIdx,
                ChunkText: chunk.chunk,
                CreatedDate: createdDate?.ToString("O"),
                LastModifiedDate: lastModifiedDate?.ToString("O")
            );
            
            var chunkObj = new ConfluenceChunkRecord(metadata, embedding);
            result.Add(chunkObj);
            chunkIdx++;
        }
        Console.WriteLine($"Finished processing {totalChunks} chunks for file: {file}\n");
        return result;
    }

    // New chunking method: builds overlapping chunks and tracks heading context in a single pass
    private List<(string chunk, string[] headings)> ChunkMarkdownWithHeadings(List<string> lines, int maxChunkSize, int overlapSize)
    {
        var chunks = new List<(string, string[])>();
        if (lines.Count == 0) return chunks;

        // Track current heading context (h1-h6)
        string[] currentHeadings = new string[6];
        // For each line, store the heading context at that line
        var headingContexts = new List<string[]>();

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            // Detect heading (markdown style)
            int headingLevel = GetHeadingLevel(line);
            if (headingLevel > 0 && headingLevel <= 6)
            {
                // Remove leading # and whitespace
                string headingText = line.TrimStart('#', ' ').Trim();
                currentHeadings[headingLevel - 1] = headingText;
                // Clear lower levels
                for (int j = headingLevel; j < 6; j++)
                    currentHeadings[j] = string.Empty;
            }
            // Store a copy of the current heading context for this line
            headingContexts.Add((string[])currentHeadings.Clone());
        }

        int startIndex = 0;
        while (startIndex < lines.Count)
        {
            var currentLines = new List<string>();
            int currentLength = 0;
            int endIndex = startIndex;
            // Use the heading context at the start of the chunk
            string[] chunkHeadings = headingContexts[startIndex];

            // Build chunk starting from startIndex with dynamic sizing
            for (int i = startIndex; i < lines.Count; i++)
            {
                var line = lines[i];
                var dynamicMaxSize = GetDynamicChunkSize(line, maxChunkSize);
                if (currentLength + line.Length + 1 > dynamicMaxSize && currentLines.Count > 0)
                {
                    endIndex = i;
                    break;
                }
                currentLines.Add(line);
                currentLength += line.Length + 1;
                endIndex = i + 1;
            }

            if (currentLines.Count > 0)
            {
                chunks.Add((string.Join("\n", currentLines), (string[])chunkHeadings.Clone()));
            }

            // Calculate next start position with overlap
            if (endIndex >= lines.Count)
            {
                break; // We've processed all lines
            }

            // Find overlap start position by going back from endIndex
            int overlapStart = FindOverlapStart(lines, endIndex, overlapSize);
            startIndex = Math.Max(overlapStart, startIndex + 1); // Ensure we make progress
        }

        return chunks;
    }

    // Helper: returns heading level (1-6) if line is a markdown heading, else 0
    private int GetHeadingLevel(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return 0;
        int level = 0;
        while (level < line.Length && line[level] == '#') level++;
        if (level > 0 && (line.Length == level || char.IsWhiteSpace(line[level])))
            return level;
        return 0;
    }

    // Helper: dynamic chunk size logic (same as before)
    private int GetDynamicChunkSize(string content, int baseMaxSize)
    {
        if (content.Contains("```") || content.Contains("`"))
            return (int)(baseMaxSize * 1.5);
        else if (content.Contains("|") && content.Split('|').Length > 4)
            return (int)(baseMaxSize * 1.3);
        else if (content.StartsWith("- ") || content.StartsWith("* "))
            return (int)(baseMaxSize * 0.8);
        else if (content.StartsWith("#"))
            return (int)(baseMaxSize * 0.7);
        return baseMaxSize;
    }

    // Helper: overlap logic (same as before)
    private int FindOverlapStart(List<string> lines, int endIndex, int overlapSize)
    {
        int currentOverlapSize = 0;
        int overlapStart = endIndex - 1;
        while (overlapStart >= 0 && currentOverlapSize < overlapSize)
        {
            currentOverlapSize += lines[overlapStart].Length + 1;
            if (currentOverlapSize >= overlapSize)
                break;
            overlapStart--;
        }
        return Math.Max(0, overlapStart);
    }
}

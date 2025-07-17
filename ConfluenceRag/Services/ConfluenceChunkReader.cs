using System.IO.Abstractions;
using System.Text.Json;
using ConfluenceRag.Models;

namespace ConfluenceRag.Services;

/// <summary>
/// Utility class for reading chunks from the new metadata.jsonl + embeddings.bin format
/// </summary>
public class ConfluenceChunkReader
{
    private readonly IFileSystem _fileSystem;
    private readonly string _outputDir;
    private readonly string _metadataPath;
    private readonly string _embeddingsPath;
    
    private const int EmbeddingSize = 384;
    private const int FloatSize = 4;
    private const int EmbeddingByteSize = EmbeddingSize * FloatSize;

    public ConfluenceChunkReader(IFileSystem fileSystem, string outputDir)
    {
        _fileSystem = fileSystem;
        _outputDir = outputDir;
        _metadataPath = _fileSystem.Path.Combine(outputDir, "metadata.jsonl");
        _embeddingsPath = _fileSystem.Path.Combine(outputDir, "embeddings.bin");
    }

    /// <summary>
    /// Searches for chunks by page ID
    /// </summary>
    public async Task<List<ConfluenceChunkRecord>> SearchByPageIdAsync(string pageId)
    {
        var indices = await SearchMetadataAsync(m => m.PageId == pageId);
        var results = new List<ConfluenceChunkRecord>();
        
        foreach (var index in indices)
        {
            var chunk = await ReadChunkAsync(index);
            if (chunk != null)
                results.Add(chunk);
        }
        
        return results;
    }

    /// <summary>
    /// Searches for chunks by embedding similarity
    /// </summary>
    public async Task<List<(ConfluenceChunkRecord Chunk, float Similarity)>> SearchBySimilarityAsync(float[] queryEmbedding, int topK = 10)
    {
        // Read all metadata lines
        if (!_fileSystem.File.Exists(_metadataPath) || !_fileSystem.File.Exists(_embeddingsPath))
            return new List<(ConfluenceChunkRecord, float)>();

        var metadataLines = await _fileSystem.File.ReadAllLinesAsync(_metadataPath);
        int chunkCount = metadataLines.Count(line => !string.IsNullOrWhiteSpace(line));
        var metadatas = new List<ConfluenceChunkMetadata>(chunkCount);
        var validLineIndices = new List<int>(chunkCount);
        for (int i = 0; i < metadataLines.Length; i++)
        {
            var line = metadataLines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            var metadata = JsonSerializer.Deserialize<ConfluenceChunkMetadata>(line);
            if (metadata != null)
            {
                metadatas.Add(metadata);
                validLineIndices.Add(i);
            }
        }

        // Read all embeddings in one go
        var embeddings = new List<float[]>(metadatas.Count);
        using (var stream = _fileSystem.File.OpenRead(_embeddingsPath))
        {
            for (int i = 0; i < validLineIndices.Count; i++)
            {
                long offset = (long)validLineIndices[i] * EmbeddingByteSize;
                if (offset + EmbeddingByteSize > stream.Length)
                {
                    embeddings.Add(new float[EmbeddingSize]); // fallback to zero vector
                    continue;
                }
                stream.Seek(offset, SeekOrigin.Begin);
                var embeddingBytes = new byte[EmbeddingByteSize];
                int bytesRead = stream.Read(embeddingBytes, 0, EmbeddingByteSize);
                if (bytesRead != EmbeddingByteSize)
                {
                    embeddings.Add(new float[EmbeddingSize]);
                    continue;
                }
                var embedding = new float[EmbeddingSize];
                Buffer.BlockCopy(embeddingBytes, 0, embedding, 0, EmbeddingByteSize);
                embeddings.Add(embedding);
            }
        }

        // Calculate similarities in memory
        var results = new List<(ConfluenceChunkRecord, float)>(metadatas.Count);
        for (int i = 0; i < metadatas.Count; i++)
        {
            var similarity = CalculateCosineSimilarity(queryEmbedding, embeddings[i]);
            var record = new ConfluenceChunkRecord(metadatas[i], embeddings[i]);
            results.Add((record, similarity));
        }

        return results
            .OrderByDescending(r => r.Item2)
            .Take(topK)
            .ToList();
    }

    /// <summary>
    /// Searches for chunks by text content (simple contains search)
    /// </summary>
    public async Task<List<ConfluenceChunkRecord>> SearchByTextAsync(string searchText)
    {
        var indices = await SearchMetadataAsync(m => 
            m.ChunkText.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
            m.Title.Contains(searchText, StringComparison.OrdinalIgnoreCase));
        
        var results = new List<ConfluenceChunkRecord>();
        
        foreach (var index in indices)
        {
            var chunk = await ReadChunkAsync(index);
            if (chunk != null)
                results.Add(chunk);
        }
        
        return results;
    }

    /// <summary>
    /// Gets all chunk metadata (for text-based searches)
    /// </summary>
    public async Task<List<ConfluenceChunkMetadata>> GetAllMetadataAsync()
    {
        if (!_fileSystem.File.Exists(_metadataPath))
            return new List<ConfluenceChunkMetadata>();

        var lines = await _fileSystem.File.ReadAllLinesAsync(_metadataPath);
        var results = new List<ConfluenceChunkMetadata>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            
            var metadata = JsonSerializer.Deserialize<ConfluenceChunkMetadata>(line);
            if (metadata != null)
                results.Add(metadata);
        }

        return results;
    }

    /// <summary>
    /// Reads a chunk by its index (0-based line number)
    /// </summary>
    public async Task<ConfluenceChunkRecord?> ReadChunkAsync(int index)
    {
        var metadata = await ReadMetadataAsync(index);
        if (metadata == null) return null;

        var embedding = await ReadEmbeddingAsync(index);
        if (embedding == null) return null;

        return new ConfluenceChunkRecord(metadata, embedding);
    }

    /// <summary>
    /// Gets the total number of chunks
    /// </summary>
    public async Task<int> GetChunkCountAsync()
    {
        if (!_fileSystem.File.Exists(_metadataPath))
            return 0;

        var lines = await _fileSystem.File.ReadAllLinesAsync(_metadataPath);
        return lines.Count(line => !string.IsNullOrWhiteSpace(line));
    }

    private async Task<ConfluenceChunkMetadata?> ReadMetadataAsync(int lineNumber)
    {
        if (!_fileSystem.File.Exists(_metadataPath))
            return null;

        var lines = await _fileSystem.File.ReadAllLinesAsync(_metadataPath);
        
        if (lineNumber < 0 || lineNumber >= lines.Length)
            return null;

        var line = lines[lineNumber];
        if (string.IsNullOrWhiteSpace(line))
            return null;

        return JsonSerializer.Deserialize<ConfluenceChunkMetadata>(line);
    }

    private async Task<float[]?> ReadEmbeddingAsync(int index)
    {
        if (!_fileSystem.File.Exists(_embeddingsPath))
            return null;

        long offset = (long)index * EmbeddingByteSize;
        
        using var stream = _fileSystem.File.OpenRead(_embeddingsPath);
        
        if (offset + EmbeddingByteSize > stream.Length)
            return null;

        stream.Seek(offset, SeekOrigin.Begin);
        
        var embeddingBytes = new byte[EmbeddingByteSize];
        int bytesRead = await stream.ReadAsync(embeddingBytes);
        
        if (bytesRead != EmbeddingByteSize)
            return null;

        var embedding = new float[EmbeddingSize];
        Buffer.BlockCopy(embeddingBytes, 0, embedding, 0, EmbeddingByteSize);
        
        return embedding;
    }

    private async Task<List<int>> SearchMetadataAsync(Func<ConfluenceChunkMetadata, bool> predicate)
    {
        if (!_fileSystem.File.Exists(_metadataPath))
            return new List<int>();

        var lines = await _fileSystem.File.ReadAllLinesAsync(_metadataPath);
        var results = new List<int>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            var metadata = JsonSerializer.Deserialize<ConfluenceChunkMetadata>(line);
            if (metadata != null && predicate(metadata))
            {
                results.Add(i);
            }
        }

        return results;
    }

    /// <summary>
    /// Calculates cosine similarity between two embedding vectors
    /// </summary>
    private static float CalculateCosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Vectors must have the same length");

        float dotProduct = 0f;
        float normA = 0f;
        float normB = 0f;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA == 0f || normB == 0f)
            return 0f;

        return dotProduct / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }
}
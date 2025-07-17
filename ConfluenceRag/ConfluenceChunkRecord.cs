namespace ConfluenceRag;

public record ConfluenceChunkRecord(
    string PageId,
    string WebUI,
    string Title,
    string[] Labels,
    string[] Headings,
    int ChunkIndex,
    string ChunkText,
    string? CreatedDate,
    string? LastModifiedDate,
    float[] Embedding
);

namespace ConfluenceRag.Models;

public record ConfluenceChunkRecord(
    ConfluenceChunkMetadata Metadata,
    float[] Embedding
);

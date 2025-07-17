namespace ConfluenceRag;

public record ConfluenceChunkRecord(
    ConfluenceChunkMetadata Metadata,
    float[] Embedding
);

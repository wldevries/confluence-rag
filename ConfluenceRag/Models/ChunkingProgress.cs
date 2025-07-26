namespace ConfluenceRag.Models;

public record ChunkingProgress(
    int CurrentChunk,
    int TotalChunks,
    string PageTitle,
    string PageId
);
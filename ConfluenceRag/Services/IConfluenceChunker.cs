using ConfluenceRag.Models;
using Microsoft.Extensions.AI;

namespace ConfluenceRag.Services;

public interface IConfluenceChunker
{
    Task<int> ProcessAllConfluenceJsonAndChunkAsync(string dataDir, string outputDir, IEmbeddingGenerator<string, Embedding<float>> embedder);
    Task<List<ConfluenceChunkRecord>> ProcessSingleConfluenceJsonAndChunkAsync(string file, IEmbeddingGenerator<string, Embedding<float>> embedder);
}

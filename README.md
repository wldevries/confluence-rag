# ConfluenceRag

A .NET 9.0 Confluence content fetching and processing tool. It retrieves Confluence pages and their children, processes them into semantic chunks with embeddings for use in RAG (Retrieval-Augmented Generation) systems.

## Description

This is a console application that:
- Fetches Confluence pages and their children via Atlassian REST API
- Processes XHTML storage format into semantic chunks with embeddings
- Outputs chunks as JSONL with metadata for RAG system consumption

## Project Structure

```
├── ConfluenceRag/                   # Main console application
├── confluence-pages.csv             # Configuration file for pages to fetch
├── data/                            # Source documents and fetched content
├── onnx/                            # ONNX embedding models
├── output/                          # Generated chunks and processed data
└── *.ps1                            # PowerShell utility scripts
```

## Setup

### Prerequisites
- .NET 9.0 SDK
- Atlassian API key (for Confluence access)
- ONNX embedding model files (see configuration below)

### Configuration

#### 1. Download ONNX Embedding Model

Download the CPU variant of the all-MiniLM-L6-v2 model from [Hugging Face](https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2):

1. Download `model.onnx` and `vocab.txt` files
2. Place them in `onnx/all-MiniLM-L6-v2/` directory
3. Ensure you get the **CPU variant** (not GPU/CUDA)

#### 2. Set up API Keys and Configuration

Create `set-secrets.ps1` with your Atlassian configuration:

```powershell
$env:ATLASSIAN_USERNAME = "your-email@company.com"
$env:ATLASSIAN_API_KEY = "YOUR_ATLASSIAN_API_TOKEN_HERE"
$env:ATLASSIAN_BASE_URL = "https://yourcompany.atlassian.net/wiki"
$env:EMBEDDING_MODEL_PATH = "onnx/all-MiniLM-L6-v2"  # Optional: override default model path

Write-Host "Atlassian environment variables set."
```

#### 3. Configure Pages to Fetch

Edit `confluence-pages.csv` to specify which Confluence pages to fetch:

```csv
PageId,Name
12345,Page Title
67890,Another Page
```

### Getting Started

1. Clone the repository
2. Configure pages in `confluence-pages.csv`
3. Set up your API keys in `set-secrets.ps1`
4. Run the setup script: `.\set-secrets.ps1`
5. Fetch and process Confluence content: `.\fetch-confluence.ps1`

## Utility PowerShell Scripts

- `fetch-confluence.ps1`: Download and process Confluence content from pages configured in confluence-pages.csv
- `set-secrets.ps1`: Set up environment variables and API keys required for the application

## Direct Commands

```bash
# Fetch a specific page and its children
dotnet run --project ConfluenceRag/ConfluenceRag.csproj -- fetch [pageId]

# Fetch people data from Confluence and update data/people.json
dotnet run --project ConfluenceRag/ConfluenceRag.csproj -- fetch-people

# Process all fetched pages into chunks
dotnet run --project ConfluenceRag/ConfluenceRag.csproj -- chunk

# Analyze all chunked data for statistics and quality
dotnet run --project ConfluenceRag/ConfluenceRag.csproj -- analyze

# Test chunking on a single file
dotnet run --project ConfluenceRag/ConfluenceRag.csproj -- test-chunk "data/pages/[PageId]_[Title].json"

# Search processed chunks (RAG search)
dotnet run --project ConfluenceRag/ConfluenceRag.csproj -- search [query]
```
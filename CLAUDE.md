# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a .NET 9.0 Confluence content fetching and processing tool. It retrieves Confluence pages and their children, processes them into semantic chunks with embeddings for use in RAG (Retrieval-Augmented Generation) systems.

## Key Projects

- **ConfluenceRag** - Main console application for fetching and processing Confluence content

## Build and Run Commands

### PowerShell Scripts (Recommended)
```powershell
# Fetch content from Confluence (reads confluence-pages.csv)
.\fetch-confluence.ps1

# Set up environment variables and secrets
.\set-secrets.ps1
```

### Direct .NET Commands
```bash
# Build project
dotnet build

# Run ConfluenceRag
dotnet run --project ConfluenceRag/ConfluenceRag.csproj

# ConfluenceRag specific commands
dotnet run --project ConfluenceRag/ConfluenceRag.csproj -- chunk          # Process all Confluence files
dotnet run --project ConfluenceRag/ConfluenceRag.csproj -- fetch [pageId] # Fetch page and children
dotnet run --project ConfluenceRag/ConfluenceRag.csproj -- test-chunk [file] # Test single file chunking
```

## Required Configuration

### Environment Variables/Secrets
- `ATLASSIAN_USERNAME` - Your Atlassian account email address
- `ATLASSIAN_API_KEY` - Atlassian API key for Confluence access
- `ATLASSIAN_BASE_URL` - Your Atlassian instance URL (e.g., https://company.atlassian.net/wiki)
- `CONFLUENCE_CHUNKS_PATH` - Path to processed Confluence chunks
- `EMBEDDING_MODEL_PATH` - Path to ONNX embedding model directory (defaults to onnx/all-MiniLM-L6-v2)

### Key File Paths
- `confluence-pages.csv` - Configuration file with PageId,Name columns for pages to fetch
- `/data/` - Source documents and fetched Confluence content
- `/output/` - Generated chunks and processed data
- `/onnx/` - ONNX model files for embeddings
- `/output/confluence/metadata.jsonl` - Metadata chunk database (LARGE FILE - do not read entire file)
- `/output/confluence/embeddings.bin` - Chunk embeddings in binary form (LARGE FILE - do not read entire file)

### Important File Handling Notes
- **DO NOT** attempt to read `/output/confluence/chunks.jsonl` as a whole file - it contains thousands of chunks with embeddings and is far too large for text processing tools
- Use `head`, `tail`, or line-specific tools when examining chunks.jsonl
- For testing individual Confluence pages, use the `test-chunk` command to process single files and output to stdout

### Configuration Files
- `confluence-pages.csv` - Defines which Confluence pages to fetch with format:
  ```csv
  PageId,Name
  12345,Page Title
  67890,Another Page
  ```
- `set-secrets.ps1` - Script to configure environment variables (create manually):
  ```powershell
  $env:ATLASSIAN_USERNAME = "your-email@company.com"
  $env:ATLASSIAN_API_KEY = "YOUR_ATLASSIAN_API_TOKEN_HERE"
  $env:ATLASSIAN_BASE_URL = "https://yourcompany.atlassian.net/wiki"
  ```

## Architecture Overview

### Content Processing Pipeline
- **Fetching**: Retrieves Confluence pages and their children via Atlassian REST API
- **Processing**: Converts XHTML storage format to semantic chunks with embeddings
- **Storage**: Outputs chunks as JSONL with metadata and embeddings for RAG systems

### Data Flow
1. confluence-pages.csv → fetch-confluence.ps1 → ConfluenceRag fetch
2. Confluence API → Local JSON files → Processing → JSONL chunks with embeddings
3. Output ready for consumption by RAG systems

## Coding Guidelines

### .NET Console Applications
- Use **Spectre.Console** for all console output, progress reporting, and exception formatting
- Use `ILogger<T>` in services
- Use async/await for all I/O operations
- Use dependency injection for services and configuration
- Handle errors with try/catch and display exceptions using `AnsiConsole.WriteException()`
- Use configuration providers (IConfiguration, Options pattern) for secrets and settings

### Unit tests
- Do not use text from actual Confluence pages. Use generated fake texts in new unit tests.

### File System Abstraction
- **Required**: Use `TestableIO.System.IO.Abstractions` for all file system operations
- **Testing**: Use `TestableIO.System.IO.Abstractions.TestingHelpers` for unit tests
- **Implementation**: Inject `IFileSystem` instead of using static `File`, `Directory`, `Path` classes
- **Priority**: ConfluenceRag project must be refactored to use file system abstractions
- **Benefits**: Enables proper unit testing without actual file system dependencies

### Content Processing Patterns
- Maintain semantic chunk structure with embeddings and metadata
- Preserve source attribution with URLs and titles
- Use local ONNX embeddings for privacy-preserving semantic search
- Handle Confluence storage format properly with namespace awareness

### Confluence Storage Format
The ConfluenceRag tool processes Confluence pages stored in XHTML-based XML format with custom namespaces:

- **Storage Format**: XHTML-based XML with custom Confluence elements
- **Primary Namespaces**:
  - `ac:` (Atlassian Confluence) - `http://atlassian.com/ac` - for macros and Confluence elements
  - `ri:` (Resource Identifier) - `http://atlassian.com/ri` - for links, users, and page references
  - `at:` (Atlassian) - for certain attributes

- **Key Elements Handled**:
  - `<ri:user>` - User references with account-id and userkey attributes
  - `<time>` - Date/time elements with datetime attributes
  - `<ac:structured-macro>` - Confluence macros (date, status, info panels, etc.)
  - `<ac:link>`, `<ac:image>`, `<ac:emoticon>` - Confluence-specific content

- **People Resolution**: Maps user account IDs to display names using `{CONFLUENCE_FETCH_DIR}/atlassian/people.json`

**Reference Documentation**:
- Official: https://confluence.atlassian.com/display/doc/confluence+storage+format
- REST API: https://developer.atlassian.com/cloud/confluence/rest/v2/

## Testing and Quality

Test the chunking process with individual files using the `test-chunk` command before processing all pages.

## Development Environment Notes

### Shell Environment
The development environment uses **MSYS2/MinGW64** (Git Bash), which provides a Unix-like shell environment on Windows:
- Shell: `/usr/bin/bash` 
- System: `MINGW64_NT-10.0-26100` (MinGW64 on Windows 10/11)
- Environment: `$MSYSTEM = MINGW64`

This means:
- Unix commands (`ls`, `rm`, `grep`) work alongside Windows tools
- File paths can be accessed in both Unix (`/d/vibe/confluence-rag`) and Windows (`D:\vibe\confluence-rag`) formats
- PowerShell scripts should be run from Windows PowerShell, not the bash environment
- When using the Bash tool, remember it operates in the MSYS2 environment, not native Windows
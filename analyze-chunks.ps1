# Analyze chunk statistics from generated JSONL output

param(
    [string]$ChunksFile = ".\output\confluence\metadata.jsonl",
    [switch]$Verbose
)

Write-Host "Analyzing chunk statistics..." -ForegroundColor Cyan
Write-Host "Reading chunks from: $ChunksFile" -ForegroundColor Gray

if (-not (Test-Path $ChunksFile)) {
    Write-Host "Error: Chunks file not found at $ChunksFile" -ForegroundColor Red
    Write-Host "Please run the chunking process first:" -ForegroundColor Yellow
    Write-Host "  dotnet run --project .\ConfluenceRag\ConfluenceRag.csproj" -ForegroundColor Yellow
    exit 1
}

# Read and parse all chunks
$chunks = @()
$lineNumber = 0
Get-Content $ChunksFile | ForEach-Object {
    $lineNumber++
    try {
        $chunk = $_ | ConvertFrom-Json
        $chunks += $chunk
    }
    catch {
        Write-Host "Warning: Failed to parse line $lineNumber" -ForegroundColor Yellow
    }
}

if ($chunks.Count -eq 0) {
    Write-Host "No chunks found in file!" -ForegroundColor Red
    exit 1
}

Write-Host "`nChunk Analysis Results" -ForegroundColor Green
Write-Host "=====================" -ForegroundColor Green

# Basic statistics
$totalChunks = $chunks.Count
$chunkSizes = $chunks | ForEach-Object { $_.ChunkText.Length }
$minSize = ($chunkSizes | Measure-Object -Minimum).Minimum
$maxSize = ($chunkSizes | Measure-Object -Maximum).Maximum
$avgSize = [math]::Round(($chunkSizes | Measure-Object -Average).Average, 1)
$medianSize = ($chunkSizes | Sort-Object)[[math]::Floor($chunkSizes.Count / 2)]

Write-Host "`nBasic Statistics:" -ForegroundColor White
Write-Host "  Total chunks: $totalChunks"
Write-Host "  Smallest chunk: $minSize characters"
Write-Host "  Largest chunk: $maxSize characters"
Write-Host "  Average chunk size: $avgSize characters"
Write-Host "  Median chunk size: $medianSize characters"

# Token estimation (rough approximation: 1 token ≈ 4 characters for English)
$minTokens = [math]::Round($minSize / 4, 0)
$maxTokens = [math]::Round($maxSize / 4, 0)
$avgTokens = [math]::Round($avgSize / 4, 0)
$totalTokens = [math]::Round(($chunkSizes | Measure-Object -Sum).Sum / 4, 0)

Write-Host "`nToken Estimates (1 token ≈ 4 chars):" -ForegroundColor White
Write-Host "  Smallest chunk: ~$minTokens tokens"
Write-Host "  Largest chunk: ~$maxTokens tokens"
Write-Host "  Average chunk size: ~$avgTokens tokens"
Write-Host "  Total tokens: ~$totalTokens tokens"

# Size distribution
$sizeRanges = @{
    'Very Small (0-200)' = ($chunks | Where-Object { $_.ChunkText.Length -le 200 }).Count
    'Small (201-500)' = ($chunks | Where-Object { $_.ChunkText.Length -gt 200 -and $_.ChunkText.Length -le 500 }).Count
    'Medium (501-1000)' = ($chunks | Where-Object { $_.ChunkText.Length -gt 500 -and $_.ChunkText.Length -le 1000 }).Count
    'Large (1001-2000)' = ($chunks | Where-Object { $_.ChunkText.Length -gt 1000 -and $_.ChunkText.Length -le 2000 }).Count
    'Very Large (2000+)' = ($chunks | Where-Object { $_.ChunkText.Length -gt 2000 }).Count
}

Write-Host "`nSize Distribution:" -ForegroundColor White
foreach ($range in $sizeRanges.Keys | Sort-Object) {
    $count = $sizeRanges[$range]
    $percentage = [math]::Round(($count / $totalChunks) * 100, 1)
    Write-Host "  $range`: $count chunks ($percentage%)"
}

# Page distribution
$pageStats = $chunks | Group-Object PageId | Sort-Object Count -Descending
$pagesWithMostChunks = $pageStats | Select-Object -First 5

Write-Host "`nPage Statistics:" -ForegroundColor White
Write-Host "  Total pages: $($pageStats.Count)"
Write-Host "  Average chunks per page: $([math]::Round($totalChunks / $pageStats.Count, 1))"

Write-Host "`nPages with most chunks:" -ForegroundColor White
foreach ($page in $pagesWithMostChunks) {
    $pageTitle = ($chunks | Where-Object { $_.PageId -eq $page.Name } | Select-Object -First 1).Title
    $truncatedTitle = if ($pageTitle.Length -gt 50) { $pageTitle.Substring(0, 47) + "..." } else { $pageTitle }
    Write-Host "  $($page.Count) chunks: $truncatedTitle"
}

# Heading analysis
$chunksWithHeadings = $chunks | Where-Object { $_.Headings -and $_.Headings.Count -gt 0 }
$headingLevels = @{}
$chunks | ForEach-Object {
    if ($_.Headings) {
        for ($i = 0; $i -lt $_.Headings.Count; $i++) {
            if ($_.Headings[$i]) {
                $level = "H$($i + 1)"
                if (-not $headingLevels.ContainsKey($level)) {
                    $headingLevels[$level] = 0
                }
                $headingLevels[$level]++
            }
        }
    }
}

Write-Host "`nHeading Structure:" -ForegroundColor White
Write-Host "  Chunks with headings: $($chunksWithHeadings.Count) / $totalChunks ($([math]::Round(($chunksWithHeadings.Count / $totalChunks) * 100, 1))%)"
if ($headingLevels.Count -gt 0) {
    Write-Host "  Heading level distribution:"
    foreach ($level in $headingLevels.Keys | Sort-Object) {
        Write-Host "    $level`: $($headingLevels[$level]) occurrences"
    }
}

# Label analysis
$allLabels = $chunks | ForEach-Object { $_.Labels } | Where-Object { $_ } | ForEach-Object { $_ } | Group-Object | Sort-Object Count -Descending
Write-Host "`nLabel Analysis:" -ForegroundColor White
if ($allLabels.Count -gt 0) {
    Write-Host "  Total unique labels: $($allLabels.Count)"
    Write-Host "  Most common labels:"
    $allLabels | Select-Object -First 10 | ForEach-Object {
        Write-Host "    $($_.Name): $($_.Count) chunks"
    }
} else {
    Write-Host "  No labels found in chunks"
}

# Quality indicators
$emptyChunks = ($chunks | Where-Object { -not $_.ChunkText -or $_.ChunkText.Trim().Length -eq 0 }).Count
$shortChunks = ($chunks | Where-Object { $_.ChunkText.Length -lt 100 }).Count
$veryLongChunks = ($chunks | Where-Object { $_.ChunkText.Length -gt 2000 }).Count

Write-Host "`nQuality Indicators:" -ForegroundColor White
Write-Host "  Empty chunks: $emptyChunks"
Write-Host "  Very short chunks (<100 chars): $shortChunks"
Write-Host "  Very long chunks (>2000 chars): $veryLongChunks"

if ($shortChunks -gt 0) {
    Write-Host "`nSample short chunks:" -ForegroundColor Yellow
    $chunks | Where-Object { $_.ChunkText.Length -lt 100 } | Select-Object -First 3 | ForEach-Object {
        $preview = $_.ChunkText -replace "`n", " " -replace "`r", ""
        if ($preview.Length -gt 80) { $preview = $preview.Substring(0, 77) + "..." }
        Write-Host "    $($_.ChunkText.Length) chars: $preview" -ForegroundColor Gray
    }
    if ($Verbose) {
        # Debug: Print all very small chunks (<=200 chars) with PageId and Title
        Write-Host "`nDebug: Full details of very small chunks (<=200 chars):" -ForegroundColor Magenta
        $chunks | Where-Object { $_.ChunkText.Length -le 200 } | ForEach-Object {
            Write-Host "---" -ForegroundColor DarkGray
            Write-Host "PageId: $($_.PageId)" -ForegroundColor Cyan
            Write-Host "Title: $($_.Title)" -ForegroundColor Cyan
            Write-Host "Chunk length: $($_.ChunkText.Length)" -ForegroundColor Cyan
            Write-Host "ChunkText:" -ForegroundColor DarkYellow
            Write-Host $_.ChunkText -ForegroundColor Gray
        }
    }
}

if ($veryLongChunks -gt 0) {
    Write-Host "`nSample very long chunks:" -ForegroundColor Yellow
    $chunks | Where-Object { $_.ChunkText.Length -gt 2000 } | Select-Object -First 3 | ForEach-Object {
        $preview = $_.ChunkText.Substring(0, [math]::Min(100, $_.ChunkText.Length)) -replace "`n", " " -replace "`r", ""
        Write-Host "    $($_.ChunkText.Length) chars: $preview..." -ForegroundColor Gray
    }
    if ($Verbose) {
        # Debug: Print all large chunks with PageId and Title
        Write-Host "`nDebug: Full details of large chunks (>1000 chars):" -ForegroundColor Magenta
        $chunks | Where-Object { $_.ChunkText.Length -gt 1000 } | ForEach-Object {
            Write-Host "---" -ForegroundColor DarkGray
            Write-Host "PageId: $($_.PageId)" -ForegroundColor Cyan
            Write-Host "Title: $($_.Title)" -ForegroundColor Cyan
            Write-Host "Chunk length: $($_.ChunkText.Length)" -ForegroundColor Cyan
            Write-Host "ChunkText:" -ForegroundColor DarkYellow
            Write-Host $_.ChunkText -ForegroundColor Gray
        }
    }
}

# Date analysis
$chunksWithDates = $chunks | Where-Object { $_.CreatedDate -or $_.LastModifiedDate }
Write-Host "`nDate Information:" -ForegroundColor White
if ($chunksWithDates.Count -gt 0) {
    Write-Host "  Chunks with date information: $($chunksWithDates.Count) / $totalChunks ($([math]::Round(($chunksWithDates.Count / $totalChunks) * 100, 1))%)"
    
    # Analyze creation dates
    $chunksWithCreated = $chunks | Where-Object { $_.CreatedDate }
    if ($chunksWithCreated.Count -gt 0) {
        $oldestCreated = ($chunksWithCreated | Measure-Object -Property CreatedDate -Minimum).Minimum
        $newestCreated = ($chunksWithCreated | Measure-Object -Property CreatedDate -Maximum).Maximum
        Write-Host "  Created date range: $($oldestCreated.ToString('yyyy-MM-dd')) to $($newestCreated.ToString('yyyy-MM-dd'))"
    }

    # Analyze last modified dates
    $chunksWithModified = $chunks | Where-Object { $_.LastModifiedDate }
    if ($chunksWithModified.Count -gt 0) {
        $oldestModified = ($chunksWithModified | Measure-Object -Property LastModifiedDate -Minimum).Minimum
        $newestModified = ($chunksWithModified | Measure-Object -Property LastModifiedDate -Maximum).Maximum
        Write-Host "  Modified date range: $($oldestModified.ToString('yyyy-MM-dd')) to $($newestModified.ToString('yyyy-MM-dd'))"

        # Recent activity (last 30 days)
        $thirtyDaysAgo = (Get-Date).AddDays(-30)
        $recentlyModified = $chunksWithModified | Where-Object { $_.LastModifiedDate -gt $thirtyDaysAgo }
        if ($recentlyModified.Count -gt 0) {
            Write-Host "  Recently modified (last 30 days): $($recentlyModified.Count) chunks"
        }
    }
} else {
    Write-Host "  No date information found in chunks"
}

# Embedding statistics
$embeddingDimensions = $chunks[0].Embedding.Count
$totalEmbeddings = $chunks.Count * $embeddingDimensions
Write-Host "`nEmbedding Statistics:" -ForegroundColor White
Write-Host "  Embedding dimensions: $embeddingDimensions"
Write-Host "  Total embedding values: $totalEmbeddings"
Write-Host "  Estimated embedding size: $([math]::Round($totalEmbeddings * 4 / 1024 / 1024, 2)) MB (float32)"

Write-Host "`nAnalysis complete!" -ForegroundColor Green

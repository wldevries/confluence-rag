# Fetch Confluence pages as configured in confluence-pages.csv

$csvPath = "${PSScriptRoot}\confluence-pages.csv"
if (-not (Test-Path $csvPath)) {
    Write-Error "confluence-pages.csv not found. Please create the file with PageId,Name columns."
    exit 1
}

try {
    $pages = Import-Csv $csvPath
    if (-not $pages -or $pages.Count -eq 0) {
        Write-Error "confluence-pages.csv is empty or invalid."
        exit 1
    }
    
    # Validate required columns exist
    $firstPage = $pages[0]
    if (-not $firstPage.PSObject.Properties.Name -contains "PageId" -or -not $firstPage.PSObject.Properties.Name -contains "Name") {
        Write-Error "confluence-pages.csv must contain PageId and Name columns."
        exit 1
    }
} catch {
    Write-Error "Failed to read confluence-pages.csv: $($_.Exception.Message)"
    exit 1
}

# Check if set-secrets.ps1 exists
if (-not (Test-Path "${PSScriptRoot}\set-secrets.ps1")) {
    Write-Error "set-secrets.ps1 not found. Please create the file to configure environment variables."
    exit 1
}

. "${PSScriptRoot}\set-secrets.ps1"

# Check if ATLASSIAN_API_KEY is set
if (-not $env:ATLASSIAN_API_KEY) {
    Write-Error "ATLASSIAN_API_KEY environment variable is not set. Please configure it in set-secrets.ps1."
    exit 1
}

Write-Host "Fetching people metadata ..." -ForegroundColor Cyan
dotnet run --project .\ConfluenceRag\ConfluenceRag.csproj -- fetch-people
Write-Host "Done fetching people metadata." -ForegroundColor Green

foreach ($page in $pages) {
    Write-Host "Fetching $($page.Name) (ID: $($page.PageId)) ..." -ForegroundColor Cyan
    dotnet run --project .\ConfluenceRag\ConfluenceRag.csproj -- fetch $($page.PageId)
    Write-Host "Done fetching $($page.Name).`n" -ForegroundColor Green
}

Write-Host "Embedding pages into the database..." -ForegroundColor Cyan
dotnet run --project .\ConfluenceRag\ConfluenceRag.csproj -- chunk
Write-Host "Done embedding pages." -ForegroundColor Green

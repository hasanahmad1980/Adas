# Upload DLSS/Streamline zips to GitHub releases on RankFTW/rhi-repo
# Prerequisites: gh CLI installed and authenticated (gh auth login)
# Usage: Run this script from any directory. It will upload all zips from the source folder.

$SourceFolder = "C:\Users\Mark\OneDrive\Desktop\DLSS"
$Repo = "RankFTW/rhi-repo"

# Get all zip files
$files = Get-ChildItem -Path $SourceFolder -Filter "*.zip"

foreach ($file in $files) {
    $name = $file.BaseName  # e.g. "nvngx_dlss_310.6.0" or "streamline_2.11.1"
    
    # Determine tag name based on filename pattern
    if ($name -match "^nvngx_dlss_(.+)$") {
        $version = $Matches[1]
        $tag = "dlss-$version"
        $title = "DLSS SR $version"
    }
    elseif ($name -match "^nvngx_dlssd_(.+)$") {
        $version = $Matches[1]
        $tag = "dlssd-$version"
        $title = "DLSS RR $version"
    }
    elseif ($name -match "^nvngx_dlssnr_(.+)$") {
        $version = $Matches[1]
        $tag = "dlssnr-$version"
        $title = "DLSS NR $version"
    }
    elseif ($name -match "^nvngx_dlssg_(.+)$") {
        $version = $Matches[1]
        $tag = "dlssg-$version"
        $title = "DLSS FG $version"
    }
    elseif ($name -match "^streamline_(.+)$") {
        $version = $Matches[1]
        $tag = "streamline-$version"
        $title = "Streamline $version"
    }
    elseif ($name -match "^DLSS-Enabler-(.+)$") {
        $version = $Matches[1]
        $tag = "DLSS-Enabler-$version"
        $title = "DLSS-Enabler-$version"
    }
    elseif ($name -match "^renodx-dlss_(.+)$") {
        $version = $Matches[1]
        $tag = "renodx-dlss-$version"
        $title = "RenoDX DLSS5 $version"
    }
    else {
        Write-Host "SKIP: Unknown filename pattern: $($file.Name)" -ForegroundColor Yellow
        continue
    }

    # Check if release already exists
    gh release view $tag --repo $Repo 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "SKIP: $tag already exists" -ForegroundColor DarkGray
        continue
    }

    Write-Host "Creating release: $tag ($title) with asset $($file.Name)..." -ForegroundColor Cyan
    
    # Create release and upload asset in one command
    gh release create $tag $file.FullName --repo $Repo --title $title --notes $title --latest=false
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  OK: $tag" -ForegroundColor Green
    }
    else {
        Write-Host "  FAILED: $tag (exit code $LASTEXITCODE)" -ForegroundColor Red
    }
}

Write-Host "`nDone. Total files processed: $($files.Count)" -ForegroundColor White

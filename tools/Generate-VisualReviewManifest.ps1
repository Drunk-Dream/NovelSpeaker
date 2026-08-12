param(
    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$visualRoot = Join-Path $RepositoryRoot 'artifacts/visual-review'
$rootManifest = Join-Path $visualRoot 'manifest.json'
$entries = @()

$childManifests = Get-ChildItem $visualRoot -Recurse -Filter manifest.json -File |
    Where-Object { $_.FullName -ne $rootManifest } |
    Sort-Object FullName

foreach ($manifestFile in $childManifests) {
    $manifest = Get-Content $manifestFile.FullName -Raw | ConvertFrom-Json
    $relativeManifest = [IO.Path]::GetRelativePath($visualRoot, $manifestFile.FullName).Replace('\', '/')
    $category = $relativeManifest.Split('/')[0]
    $artifactId = if ($manifest.PSObject.Properties['ArtifactId']) { $manifest.ArtifactId } else { $manifest.artifactId }

    $scenes = if ($manifest.PSObject.Properties['Scenes']) { $manifest.Scenes } else { $manifest.Scenarios }
    foreach ($scene in $scenes) {
        $scenario = if ($scene.PSObject.Properties['Scenario']) {
            $scene.Scenario
        } elseif ($scene.PSObject.Properties['scene']) {
            $scene.scene
        } else {
            'default'
        }
        $theme = $scene.Theme.ToString().ToLowerInvariant()
        $pngName = if ($scene.PSObject.Properties['Png']) { $scene.Png } else { $scene.png }
        $expectedHash = if ($scene.PSObject.Properties['Sha256']) { $scene.Sha256 } else { $scene.sha256 }
        $pngPath = Join-Path $manifestFile.DirectoryName $pngName
        if (-not (Test-Path $pngPath -PathType Leaf)) {
            throw "Missing visual review screenshot: $pngPath"
        }

        $actualHash = (Get-FileHash $pngPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne $expectedHash.ToString().ToLowerInvariant()) {
            throw "Visual review hash mismatch: $pngPath"
        }

        $entries += [pscustomobject][ordered]@{
            category = $category
            artifactId = $artifactId
            scenario = $scenario
            theme = $theme
            dpi = [int]$scene.Dpi
            png = [IO.Path]::GetRelativePath($visualRoot, $pngPath).Replace('\', '/')
            sha256 = $actualHash
        }
    }
}

$root = [ordered]@{
    schemaVersion = 1
    entries = @($entries | Sort-Object category, artifactId, scenario, theme, dpi, png)
}

$root | ConvertTo-Json -Depth 5 | Set-Content $rootManifest -Encoding utf8

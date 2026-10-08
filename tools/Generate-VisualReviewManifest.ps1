param(
    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$visualRoot = Join-Path $RepositoryRoot 'artifacts/visual-review'
$rootManifest = Join-Path $visualRoot 'manifest.json'
$entries = @()

New-Item -ItemType Directory -Path $visualRoot -Force | Out-Null

function Get-RelativePathCompat {
    param(
        [string] $BasePath,
        [string] $TargetPath
    )

    $baseFullPath = [IO.Path]::GetFullPath($BasePath)
    if (-not $baseFullPath.EndsWith([IO.Path]::DirectorySeparatorChar)) {
        $baseFullPath += [IO.Path]::DirectorySeparatorChar
    }

    $baseUri = New-Object Uri($baseFullPath)
    $targetUri = New-Object Uri([IO.Path]::GetFullPath($TargetPath))
    return [Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString())
}

$childManifests = Get-ChildItem $visualRoot -Recurse -Filter manifest.json -File |
    Where-Object { $_.FullName -ne $rootManifest } |
    Sort-Object FullName

foreach ($manifestFile in $childManifests) {
    $manifest = Get-Content $manifestFile.FullName -Raw | ConvertFrom-Json
    $relativeManifest = Get-RelativePathCompat $visualRoot $manifestFile.FullName
    $category = $relativeManifest.Split('/')[0]
    $artifactId = $manifest.ArtifactId
    $scenesProperty = $manifest.PSObject.Properties['scenes']
    if ($null -eq $scenesProperty) {
        throw "Visual review manifest is missing required 'scenes' collection: $($manifestFile.FullName)"
    }

    if ($scenesProperty.Value -isnot [System.Array] -or $scenesProperty.Value.Count -eq 0) {
        throw "Visual review manifest requires a non-empty 'scenes' array: $($manifestFile.FullName)"
    }

    $scenes = $scenesProperty.Value
    $sceneIndex = 0
    foreach ($scene in $scenes) {
        if ($null -eq $scene.PSObject.Properties['scene'] -or
            [string]::IsNullOrWhiteSpace([string]$scene.scene)) {
            throw "Visual review manifest entry $sceneIndex is missing required 'scene': $($manifestFile.FullName)"
        }

        $scenario = $scene.scene
        $theme = $scene.theme.ToString().ToLowerInvariant()
        $pngName = $scene.png
        $expectedHash = $scene.sha256
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
            png = (Get-RelativePathCompat $visualRoot $pngPath).Replace('\', '/')
            sha256 = $actualHash
        }

        $sceneIndex++
    }
}

$root = [ordered]@{
    schemaVersion = 1
    entries = @($entries | Sort-Object category, artifactId, scenario, theme, dpi, png)
}

$root | ConvertTo-Json -Depth 5 | Set-Content $rootManifest -Encoding utf8

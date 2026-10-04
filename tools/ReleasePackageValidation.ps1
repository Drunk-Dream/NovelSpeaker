$ReleaseRequiredRootFiles = @(
    'NovelSpeaker.exe',
    'LICENSE',
    'THIRD-PARTY-NOTICES.txt',
    'NAudio.dll',
    'NAudio.Wasapi.dll',
    'coreclr.dll',
    'hostfxr.dll',
    'hostpolicy.dll',
    'Microsoft.Data.Sqlite.dll',
    'SQLitePCLRaw.provider.winsqlite3.dll'
)
$ReleaseForbiddenUserDataNames = @('Data', 'Books', 'Cache', 'Operations', 'Logs', 'app.db', 'settings.json')
$ReleaseForbiddenFixtures = @('demo-tone.wav', 'demo-tone.mp3', 'corrupt-tone.mp3')
$ReleaseTemporaryExtensions = @('.tmp', '.temp', '.partial', '.bak', '.orig', '.rej')

function Get-ReleasePackageDirectoryEntries {
    param([Parameter(Mandatory)][string] $Path)

    $rootPath = (Resolve-Path $Path).Path
    Get-ChildItem $rootPath -Recurse -Force | ForEach-Object {
        $relativePath = $_.FullName.Substring($rootPath.Length)
        $relativePath = $relativePath.TrimStart([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
        $relativePath = $relativePath.Replace('\', '/')
        if ($_.PSIsContainer) { "$relativePath/" } else { $relativePath }
    }
}

function Assert-ReleasePackageEntries {
    param(
        [Parameter(Mandatory)][string[]] $Entries,
        [Parameter(Mandatory)][string] $ArtifactName
    )

    foreach ($required in $ReleaseRequiredRootFiles) {
        if ($Entries -notcontains $required) {
            throw "$ArtifactName is missing required root file: $required"
        }
    }

    $violations = @(
        foreach ($entry in $Entries) {
            $normalized = $entry.Replace('\', '/').TrimEnd('/')
            $segments = @($normalized.Split('/'))
            $name = $segments[-1]

            if ($name -eq 'NovelSpeaker.App.exe') {
                "$entry (legacy executable name)"
            }
            if ($segments | Where-Object { $_ -in $ReleaseForbiddenUserDataNames }) {
                "$entry (user data name)"
            }
            if ($segments -contains 'TestAssets') {
                "$entry (TestAssets)"
            }
            if ($name -in $ReleaseForbiddenFixtures) {
                "$entry (test audio fixture)"
            }
            if ($name -match 'Tests\.dll$') {
                "$entry (test assembly)"
            }
            if (($segments | Where-Object { $_ -match 'StyleGallery' -or $_ -eq 'visual-review' })) {
                "$entry (Style Gallery asset)"
            }

            $extension = [IO.Path]::GetExtension($name)
            if ($name -in $ReleaseTemporaryExtensions -or
                $extension -in $ReleaseTemporaryExtensions -or
                $name.EndsWith('~', [StringComparison]::Ordinal)) {
                "$entry (temporary file)"
            }
        }
    )

    if ($violations.Count -gt 0) {
        throw "$ArtifactName contains forbidden entries: $($violations -join ', ')"
    }
}

function Assert-ReleasePackageDirectory {
    param([Parameter(Mandatory)][string] $Path)

    $entries = @(Get-ReleasePackageDirectoryEntries $Path)
    Assert-ReleasePackageEntries -Entries $entries -ArtifactName 'Publish output'
}

function Assert-ReleasePackageZip {
    param([Parameter(Mandatory)][string] $Path)

    $archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path $Path).Path)
    try {
        $entries = @($archive.Entries | ForEach-Object { $_.FullName })
    }
    finally {
        $archive.Dispose()
    }

    Assert-ReleasePackageEntries -Entries $entries -ArtifactName 'ZIP'
}

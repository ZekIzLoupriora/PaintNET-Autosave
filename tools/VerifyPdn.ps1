param(
    [Parameter(Mandatory = $true)]
    [string] $Path
)

$resolved = (Resolve-Path -LiteralPath $Path).Path
$zip = [System.IO.Compression.ZipFile]::OpenRead($resolved)
try {
    $names = @($zip.Entries | ForEach-Object FullName)
    if ('header' -notin $names) { throw "Not a recognizable native PDN archive; the header entry is missing." }
    if ($names.Count -lt 2) { throw "The archive has no layer payload entries." }
    [pscustomobject]@{
        Path = $resolved
        Bytes = (Get-Item -LiteralPath $resolved).Length
        Entries = $names.Count
        ValidZip = $true
    }
}
finally { $zip.Dispose() }

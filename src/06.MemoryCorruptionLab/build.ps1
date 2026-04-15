param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$project = Join-Path $PSScriptRoot "managed\Csharp14.SystemsMemory.MemoryCorruptionLab.csproj"

dotnet build $project -c $Configuration
if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "Built $project ($Configuration)."
Write-Host "Run: dotnet run --project src/06.MemoryCorruptionLab/managed -- --mode healthy"

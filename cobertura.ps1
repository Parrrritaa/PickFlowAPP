# Genera el reporte de cobertura en cobertura/reporte/index.html
Remove-Item -Recurse -Force "$PSScriptRoot\cobertura" -ErrorAction SilentlyContinue
dotnet test "$PSScriptRoot\Tests\PickingApp.Tests" --settings "$PSScriptRoot\Tests\coverage.runsettings" --collect "XPlat Code Coverage" --results-directory "$PSScriptRoot\cobertura"
if (-not (Get-Command reportgenerator -ErrorAction SilentlyContinue)) {
    dotnet tool install -g dotnet-reportgenerator-globaltool
}
reportgenerator "-reports:$PSScriptRoot\cobertura\**\coverage.cobertura.xml" "-targetdir:$PSScriptRoot\cobertura\reporte" "-reporttypes:Html;TextSummary"
Get-Content "$PSScriptRoot\cobertura\reporte\Summary.txt" | Select-Object -First 12
Start-Process "$PSScriptRoot\cobertura\reporte\index.html"

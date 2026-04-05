# .NET 10 一键打包脚本 - 框架依赖模式
$projectName = "BlogTools"
$outputDir = ".\Release_Standalone" # 改为一个更正式的名称

$ErrorActionPreference = "Stop"
$projectFile = Join-Path $PSScriptRoot "$projectName.csproj"
$outputDir = Join-Path $PSScriptRoot "Release"

if (Test-Path $outputDir) {
    Write-Host "Cleaning directory ($outputDir)..."
    try {
        Remove-Item -LiteralPath $outputDir -Recurse -Force
    }
    catch {
        throw "Unable to clean '$outputDir'. Close any app running from Release and try again."
    }
}

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

Write-Host "Publishing framework-dependent win-x64 build..."
dotnet publish $projectFile -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true `
    -p:PublishReadyToRun=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $outputDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

Write-Host "`nPublish succeeded." -ForegroundColor Green
Write-Host "Output folder: $outputDir" -ForegroundColor Yellow
Write-Host "Target machines must have the .NET Desktop Runtime installed." -ForegroundColor Yellow
explorer $outputDir
return

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n✅ 打包成功！" -ForegroundColor Green
    Write-Host "📂 请将整个 [$outputDir] 文件夹分发给用户。" -ForegroundColor Yellow
    # 自动打开文件夹方便查看
    explorer $outputDir
}

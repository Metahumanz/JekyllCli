param(
    [string]$ExePath = "C:\VScodework\JekyllCli\Tools\bin\Debug\net10.0-windows\JekyllCli.exe",
    [string]$SettingsPath = "",
    [string]$BlogPath = "C:\VScodework\JekyllCli\Blog",
    [string]$OutputDir = "C:\VScodework\JekyllCli\docs\images\real"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $ExePath)) {
    throw "Executable not found: $ExePath"
}

$captureArgs = @("--capture-doc-screenshots=$OutputDir")
if (-not [string]::IsNullOrWhiteSpace($BlogPath)) {
    $captureArgs += "--capture-blog=$BlogPath"
}

& $ExePath @captureArgs

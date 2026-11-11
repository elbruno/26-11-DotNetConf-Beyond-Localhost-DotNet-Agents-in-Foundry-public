#requires -Version 7.0
<#
Purpose: Save non-secret chat and Decision-1 endpoints/deployments for all five demos.
Run with & .\demos\Set-DemoEnvironment.ps1 in PowerShell 7, not pwsh -File.
Expected result: four visible settings in an ignored local JSON file and this process.
No keys, credential variables, authentication, builds, inference or deployment.
Hosted agent names are fixed in the manifests, not stored in these four settings.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$settingsPath = Join-Path $PSScriptRoot 'shared\demo-settings.local.json'
$saved = if (Test-Path -LiteralPath $settingsPath) {
    Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json -AsHashtable
} else { @{} }

function Get-Default([string]$Name, [string]$Fallback = '') {
    $current = [Environment]::GetEnvironmentVariable($Name, 'Process')
    if (-not [string]::IsNullOrWhiteSpace($current)) { return $current }
    if ($saved.ContainsKey($Name) -and -not [string]::IsNullOrWhiteSpace($saved[$Name])) { return $saved[$Name] }
    return $Fallback
}

function Read-Setting([string]$Label, [string]$Default) {
    $prompt = if ($Default) { "$Label [$Default]" } else { $Label }
    $value = Read-Host -Prompt $prompt
    if ($value -eq '') { return $Default }
    return $value
}

$projectEndpoint = Read-Setting 'Foundry chat project endpoint' (Get-Default 'FOUNDRY_PROJECT_ENDPOINT')
$modelDeployment = Read-Setting 'Existing chat model deployment' (Get-Default 'AZURE_AI_MODEL_DEPLOYMENT_NAME' 'gpt-6-luna')
$decisionEndpoint = Read-Setting 'Foundry Decision-1 resource endpoint' (Get-Default 'Decision__Endpoint')
$decisionDeployment = Read-Setting 'Existing Microsoft-Decision-1 deployment' (Get-Default 'Decision__Deployment')

# Validate all four inputs before changing any process setting.
$endpoint = $null
if ($projectEndpoint -match '[\s\p{Cc}]' -or
    -not [Uri]::TryCreate($projectEndpoint, [UriKind]::Absolute, [ref]$endpoint) -or
    $endpoint.Scheme -ne 'https' -or -not $endpoint.Host -or
    $endpoint.UserInfo -or $endpoint.Query -or $endpoint.Fragment -or
    $endpoint.AbsolutePath -notmatch '^/api/projects/[^/]+/?\z') {
    throw 'Invalid Foundry project endpoint. Use an HTTPS /api/projects/<project> URL without credentials, query or fragment. Nothing was changed.'
}
$endpoint = $null
if ($decisionEndpoint -match '[\s\p{Cc}]' -or
    -not [Uri]::TryCreate($decisionEndpoint, [UriKind]::Absolute, [ref]$endpoint) -or
    $endpoint.Scheme -ne 'https' -or -not $endpoint.Host -or
    $endpoint.UserInfo -or $endpoint.Query -or $endpoint.Fragment -or
    $endpoint.AbsolutePath -ne '/') {
    throw 'Invalid Decision-1 endpoint. Use an HTTPS native resource root without a project path, credentials, query or fragment. Nothing was changed.'
}
foreach ($deployment in @($modelDeployment, $decisionDeployment)) {
    if ($deployment -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,127}\z') {
        throw 'Invalid model deployment name. Use 1 to 128 letters, digits, dots, underscores or hyphens, starting with a letter or digit. Nothing was changed.'
    }
}

$settings = [ordered]@{
    FOUNDRY_PROJECT_ENDPOINT = $projectEndpoint
    AZURE_AI_MODEL_DEPLOYMENT_NAME = $modelDeployment
    Decision__Endpoint = $decisionEndpoint
    Decision__Deployment = $decisionDeployment
}
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $settingsPath) | Out-Null
$temporary = "$settingsPath.tmp"
try {
    $settings | ConvertTo-Json | Set-Content -LiteralPath $temporary -Encoding utf8
    [IO.File]::Move($temporary, $settingsPath, $true)
}
finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
}
$env:FOUNDRY_PROJECT_ENDPOINT = $projectEndpoint
$env:AZURE_AI_MODEL_DEPLOYMENT_NAME = $modelDeployment
$env:Decision__Endpoint = $decisionEndpoint
$env:Decision__Deployment = $decisionDeployment
Write-Host "FOUNDRY_PROJECT_ENDPOINT = $projectEndpoint"
Write-Host "AZURE_AI_MODEL_DEPLOYMENT_NAME = $modelDeployment"
Write-Host "Decision__Endpoint = $decisionEndpoint"
Write-Host "Decision__Deployment = $decisionDeployment"
Write-Host 'Saved non-secret settings for future demo runs and configured this terminal. No keys, sign-in or model calls.'

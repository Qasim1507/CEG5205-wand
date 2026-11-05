$ErrorActionPreference = 'Stop'

$projectPath = $PSScriptRoot
$simulatorDir = Join-Path $env:LOCALAPPDATA 'Programs\MetaXRSimulatorPortable\PFiles\MetaXRSimulator\v207.1'
$simulatorExe = Join-Path $simulatorDir 'MetaXRSimulator.exe'
$runtimeJson = Join-Path $simulatorDir 'meta_openxr_simulator.json'
$unityExe = 'C:\Program Files\Unity\Hub\Editor\6000.6.4f1\Editor\Unity.exe'

foreach ($requiredFile in @($simulatorExe, $runtimeJson, $unityExe)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Required file is missing: $requiredFile"
    }
}

if (-not (Get-Process -Name MetaXRSimulator -ErrorAction SilentlyContinue)) {
    Start-Process -FilePath $simulatorExe -WorkingDirectory $simulatorDir
}

# OpenXR's loader reads this override from the Unity process environment.
# It leaves the machine-wide OpenXR runtime unchanged.
$env:XR_RUNTIME_JSON = $runtimeJson
Start-Process -FilePath $unityExe -ArgumentList @('-projectPath', ('"' + $projectPath + '"'))
Write-Host "Started Unity with Meta XR Simulator for: $projectPath"
Write-Host 'Open Assets/Scenes/QuestSurgeryGame.unity and press Play.'

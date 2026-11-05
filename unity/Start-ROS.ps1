param(
    [ValidateSet('2', '3', '4Sim')]
    [string]$Part = '3'
)

$ErrorActionPreference = 'Stop'

# WSL may stop its Docker service when the last Linux session exits. Keep a
# small Linux process alive for the duration of the Windows login session.
$keeper = wsl -d Ubuntu -u root -- pgrep -f '^sleep infinity$' 2>$null
if ($LASTEXITCODE -ne 0 -or -not $keeper) {
    Start-Process -FilePath 'wsl.exe' -ArgumentList @('-d','Ubuntu','-u','root','--','sleep','infinity') -WindowStyle Hidden
    Start-Sleep -Seconds 2
}

wsl -d Ubuntu -u root -- systemctl start docker
if ($LASTEXITCODE -ne 0) { throw 'Could not start Docker in Ubuntu WSL.' }

$image = 'unity-robotics:desktop-target-noetic'
wsl -d Ubuntu -u root -- /usr/bin/docker image inspect $image *> $null
if ($LASTEXITCODE -ne 0) {
    $linuxProject = (wsl -d Ubuntu -u root -- wslpath -a $PSScriptRoot).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Could not locate the Unity project inside Ubuntu WSL.' }
    wsl -d Ubuntu -u root -- /usr/bin/docker build -t $image -f "$linuxProject/docker/Dockerfile.noetic" $linuxProject
    if ($LASTEXITCODE -ne 0) { throw 'Could not build the ROS image.' }
}

$stages = @{
    '2' = @{ Container = 'unity-pnp-part2'; Launch = 'part_2.launch' }
    '3' = @{ Container = 'unity-pick-and-place-ros'; Launch = 'part_3.launch' }
    '4Sim' = @{ Container = 'unity-pnp-part4-sim'; Launch = 'part_4_sim.launch' }
}
$selected = $stages[$Part]
$containerName = $selected.Container
$known = @{}
foreach ($line in @(wsl -d Ubuntu -u root -- /usr/bin/docker ps -a --format '{{.Names}} {{.State}}')) {
    $parts = $line.Trim().Split(' ', 2)
    if ($parts.Length -eq 2) { $known[$parts[0]] = $parts[1] }
}

foreach ($other in $stages.Values) {
    if ($other.Container -eq $containerName) { continue }
    if ($known[$other.Container] -eq 'running') {
        wsl -d Ubuntu -u root -- /usr/bin/docker stop $other.Container | Out-Null
    }
}

if ($known.ContainsKey($containerName)) {
    $imageId = (wsl -d Ubuntu -u root -- /usr/bin/docker image inspect --format '{{.Id}}' $image).Trim()
    $containerImageId = (wsl -d Ubuntu -u root -- /usr/bin/docker container inspect --format '{{.Image}}' $containerName).Trim()
    if ($imageId -ne $containerImageId) {
        if ($known[$containerName] -eq 'running') {
            wsl -d Ubuntu -u root -- /usr/bin/docker stop $containerName | Out-Null
        }
        wsl -d Ubuntu -u root -- /usr/bin/docker rm $containerName | Out-Null
        $known.Remove($containerName)
    }
}

if ($known.ContainsKey($containerName)) {
    if ($known[$containerName] -ne 'running') {
        wsl -d Ubuntu -u root -- /usr/bin/docker start $containerName
    }
} else {
    wsl -d Ubuntu -u root -- /usr/bin/docker run -d --name $containerName -p 10000:10000 $image roslaunch niryo_moveit $selected.Launch
}

if ($LASTEXITCODE -ne 0) { throw 'The ROS container did not start.' }
Write-Output "ROS Part $Part is starting in $containerName. Check logs with: wsl -d Ubuntu -u root -- /usr/bin/docker logs $containerName"

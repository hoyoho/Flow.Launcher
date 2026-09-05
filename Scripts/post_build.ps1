param(
    [string]$config = "Release",
    [string]$solution = (Join-Path $PSScriptRoot ".." -Resolve),
    [string]$channel = "win",
    [string]$flowVersion = ""
)
Write-Host "Config: $config"
Write-Host "Channel: $channel"
Write-Host "FlowVersion: $flowVersion"

function Build-Version {
    if (![string]::IsNullOrEmpty($flowVersion)) {
        $v = $flowVersion
    } elseif (![string]::IsNullOrEmpty($env:flowVersion)) {
        $v = $env:flowVersion
    } elseif (![string]::IsNullOrEmpty($env:FlowVersion) -and ![string]::IsNullOrEmpty($env:BUILD_NUMBER)) {
        $v = "$env:FlowVersion-build.$env:BUILD_NUMBER"
    } else {
        $targetPath = Join-Path $solution "Output/Release/Flow.Launcher.dll" -Resolve
        $v = (Get-Command ${targetPath}).FileVersionInfo.FileVersion

        $versionParts = $v.Split('.')
        if ($versionParts.Length -eq 4) {
            $v = "$($versionParts[0]).$($versionParts[1]).$($versionParts[2])-build.$($versionParts[3])"
        }
    }

    Write-Host "Build Version: $v"
    return $v
}

function Build-Path {
    if (![string]::IsNullOrEmpty($env:APPVEYOR_BUILD_FOLDER)) {
        $p = $env:APPVEYOR_BUILD_FOLDER
    } elseif (![string]::IsNullOrEmpty($solution)) {
        $p = $solution
    } else {
        $p = Get-Location
    }

    Write-Host "Build Folder: $p"
    Set-Location $p

    return $p
}

function Delete-Unused ($path, $config) {
    $target = "$path\Output\$config"
    $included = Get-ChildItem $target -Filter "*.dll"
    foreach ($i in $included){
        $deleteList = Get-ChildItem $target\Plugins -Include $i -Recurse | Where { $_.VersionInfo.FileVersion -eq $i.VersionInfo.FileVersion -And $_.Name -eq "$i" }
        $deleteList | ForEach-Object{ Write-Host Deleting duplicated $_.Name with version $_.VersionInfo.FileVersion at location $_.Directory.FullName }
        $deleteList | Remove-Item
    }
    Remove-Item -Path $target -Include "*.xml" -Recurse
}

function Remove-CreateDumpExe ($path, $config) {
    $target = "$path\Output\$config"

    $depjson = Get-Content $target\Flow.Launcher.deps.json -raw
    $depjson -replace '(?s)(.createdump.exe": {.*?}.*?\n)\s*', "" | Out-File $target\Flow.Launcher.deps.json -Encoding UTF8
    Remove-Item -Path $target -Include "*createdump.exe" -Recurse
}


function Validate-Directory ($output) {
    New-Item $output -ItemType Directory -Force
}

function Install-Vpk {
    $vpkPath = Join-Path $env:USERPROFILE ".dotnet\tools\vpk.exe"
    if (!(Test-Path $vpkPath)) {
        dotnet tool install --global vpk | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to install vpk tool"
        }
    }

    return $vpkPath
}

function Pack-Velopack-Installer ($path, $version, $output, $channel) {
    Write-Host "Begin pack velopack installer"
    Write-Host "Version: $version"
    Write-Host "Channel: $channel"

    $input = "$path\Output\Release"
    $icon = "$path\Flow.Launcher\Resources\app.ico"
    $packId = "FlowLauncher"
    $mainExe = "Flow.Launcher.exe"

    Write-Host "Input path:  $input"
    Write-Host "Output path: $output"
    Write-Host "Icon: $icon"

    $vpk = Install-Vpk

    & $vpk pack --packId $packId --packVersion $version --packDir $input --mainExe $mainExe `
        --packTitle "Flow Launcher" --packAuthors "Flow-Launcher Team" `
        --icon $icon --channel $channel --outputDir $output | Write-Output

    if ($LASTEXITCODE -ne 0) {
        throw "vpk pack failed with exit code $LASTEXITCODE"
    }

    $setupExe = Get-ChildItem $output -Filter "*.exe" | Where-Object { $_.Name -like "FlowLauncher*" } | Select-Object -First 1
    if ($setupExe) {
        Move-Item $setupExe.FullName "$output\Flow-Launcher-Setup.exe" -Force
    }

    $portableZip = Get-ChildItem $output -Filter "*.zip" | Where-Object { $_.Name -like "FlowLauncher*" } | Select-Object -First 1
    if ($portableZip) {
        Move-Item $portableZip.FullName "$output\Flow-Launcher-Portable.zip" -Force
    }

    Write-Host "End pack velopack installer"
}

function Publish-Self-Contained ($p) {

    $csproj  = Join-Path "$p" "Flow.Launcher/Flow.Launcher.csproj" -Resolve
    $profile = Join-Path "$p" "Flow.Launcher/Properties/PublishProfiles/Net9.0-SelfContained.pubxml" -Resolve

    # we call dotnet publish on the main project.
    # The other projects should have been built in Release at this point.
    dotnet publish -c Release $csproj /p:PublishProfile=$profile
}

function Main {
    $p = Build-Path
    $v = Build-Version

    if ($config -eq "Release"){

        Delete-Unused $p $config

        Publish-Self-Contained $p

        Remove-CreateDumpExe $p $config

        $o = "$p\Output\Packages"
        Validate-Directory $o
        Pack-Velopack-Installer $p $v $o $channel
    }
}

Main

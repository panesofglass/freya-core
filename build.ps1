[xml]$doc = Get-Content .\src\Directory.Build.props
$version = $doc.Project.PropertyGroup.VersionPrefix # the version under development, update after a release
$versionSuffix = '-build.0' # manually incremented for local builds

function isVersionTag($tag){
    $v = New-Object Version
    [Version]::TryParse($tag, [ref]$v)
}

if ($env:appveyor){
    $versionSuffix = '-build.' + $env:appveyor_build_number
    if ($env:appveyor_repo_tag -eq 'true' -and (isVersionTag($env:appveyor_repo_tag_name))){
        $version = $env:appveyor_repo_tag_name
        $versionSuffix = ''
    }
    Update-AppveyorBuild -Version "$version$versionSuffix"
}

dotnet build -c Release Freya.Core.sln /p:Version=$version$versionSuffix
dotnet test --no-build -c Release tests/Freya.Core.Tests/Freya.Core.Tests.fsproj
dotnet test --no-build -c Release tests/Freya.Core.Hopac.Tests/Freya.Core.Hopac.Tests.fsproj
dotnet pack --no-build -c Release src/Freya.Core /p:Version=$version$versionSuffix -o $psscriptroot/bin
dotnet pack --no-build -c Release src/Freya.Core.Hopac /p:Version=$version$versionSuffix -o $psscriptroot/bin
dotnet tests/Freya.Core.Benchmarks/bin/Release/netcoreapp2.1/Freya.Core.Benchmarks.dll

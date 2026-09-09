param(
    [switch]$SkipTests,
    [switch]$BuildInstaller
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $repositoryRoot '.dotnet\dotnet.exe'
$dotnet = if (Test-Path $localDotnet) { $localDotnet } else { 'dotnet' }
$artifacts = Join-Path $repositoryRoot 'artifacts'
$publish = Join-Path $artifacts 'publish'
$requiredDlss5Payload = @(
    'renodx-dlss.addon64',
    'renodx-dlss5-4.70.addon64',
    'renodx-dlss5-4.55.addon64',
    'dlss5-bridge.addon64',
    'dlss5-feed.addon64',
    'dlss5-feed.addon32',
    'dlss5-feed-host64.exe',
    'DLSS5_Feed.fx',
    'feed-vk-layer.zip',
    'dlss5-feed-0.15.1.addon64',
    'dlss5-feed-0.15.1.addon32',
    'dlss5-feed-host64-0.15.1.exe',
    'DLSS5_Feed-0.15.1.fx',
    'feed-vk-layer-0.15.1-x64.zip',
    'feed-vk-layer-0.15.1-x86.zip',
    'dlss5-opengl-bridge.addon64',
    'ReShade.fxh',
    'ReShadeUI.fxh',
    'DrawText.fxh',
    'streamline.zip',
    'optiscaler-nr.zip',
    'optiscaler-split.zip',
    'optiscaler-multipass.zip',
    'standalone-dlssnr.addon64',
    'nvngx.dll',
    'DLSS5_AIO_Feed.fx',
    'vort-shaders.zip',
    'ReShade-6.8.0-32.dll',
    'ReShade-6.8.0-64.dll',
    'ReShade-6.3.3-32.dll',
    'ReShade-6.3.3-64.dll'
)

$expectedDlss5Hashes = @{
    'renodx-dlss.addon64'                  = 'C575F3F7C8CA6F4EF3BAA2194EA8E37E06E8EA32EE0701ECFB99035291304B00'
    'renodx-dlss5-4.70.addon64'            = 'D5ADF82EB44B065F4C590AC91FE824BAB07AFEA0EB9F994BDE936710C8593952'
    'renodx-dlss5-4.55.addon64'            = '9150097CDEE2953CDC9894D2E5606EA5100E6C8F95FC7BB1B407328B4391A07A'
    'dlss5-bridge.addon64'                 = '4F2ACECC1026AE89AC0B92767BE66CEEA2662AD0EF88710B89C7DA7840D548D4'
    'dlss5-feed.addon64'                   = 'E6861ABEF41BC90934352A967017DD019BCE6D746D35910B67C7DD20F061C0E2'
    'dlss5-feed.addon32'                   = '7D55A608650ACB2DBF0A4F4BF782AB45FF8EEC4700A8EBF4676B441697B3D8AB'
    'dlss5-feed-host64.exe'                = 'B8944065E087536FA137B0450488017A4B58AD00E2ACB6EE67912395ADEC8233'
    'DLSS5_Feed.fx'                        = 'CBC997A1D0B9B0E00B8C4E912A09BC4B1AEF968AD36269502CBE386499264222'
    'feed-vk-layer.zip'                    = '93EA11417103B8ED9B947BB549EFF6B77C43A66DECDDB99634E39EAB11242711'
    'dlss5-feed-0.15.1.addon64'            = '3AFC8EFB5F516E94A2A068B2E90EAED360D1E30CA2C29B623E0CFADC1F05D50D'
    'dlss5-feed-0.15.1.addon32'            = 'FB69357075CBA536B42F2E693992FDE0C1775058B1FD9F10FBDF1E7EBA3443E7'
    'dlss5-feed-host64-0.15.1.exe'         = '034E6CDF382E6EA9164AFD63C5B8A8AA0312894CB342593BC933E9FA435F8D02'
    'DLSS5_Feed-0.15.1.fx'                 = 'CDAC08A721B14B97187DD86C5B5BEAD157C9063D7EE859A0F131A8EE791695F1'
    'feed-vk-layer-0.15.1-x64.zip'         = '8F941999B3B3F7640EF5078CD0DEB6A638D87D885CD5543CAEE7F989F34D8616'
    'feed-vk-layer-0.15.1-x86.zip'         = '787A575304E02F2FA1B1283EC9176257CF4EEDEAFADBC3A24128AAF2B14FC80D'
    'dlss5-opengl-bridge.addon64'          = '7E563A056F3C3621B6AF1641BCF952A4B7B108D04E285A362F61D7B479C611BA'
    'ReShade.fxh'                          = 'BFA89183CFEB99968A58E751CD23AFA2F5FF56332B3CF7F08F23E39879BADC70'
    'ReShadeUI.fxh'                        = 'B95EEF44289C97FF027E5C70EC579F5FBB3C375037544C4F2913C4F33C7D5D53'
    'DrawText.fxh'                         = '3509C6C02C33A4390AE5410740CDD287B410618CBA2915A4733B148A5798A0FC'
    'streamline.zip'                       = 'CD9DF8F513E2BA9D13A9278100FAA2AD025599F59AEF26DBFF3420DAC74F1F09'
    'optiscaler-nr.zip'                    = '8EECE7A4D7DE6DE5917F0C99AC60540B2D77022E7699BBA717B0A6D9E1829BCE'
    'optiscaler-split.zip'                 = '38BB8DDA6EF288FA3546DBF294886E9223DB767F36D7FB933F71C0A1E4CF4449'
    'optiscaler-multipass.zip'             = '3E735F825B85872D20C35C0D75404C1A28B7B6E11CA28EF06243A18225191037'
    'standalone-dlssnr.addon64'            = '597C2A3B69FD8299C9D29E9619D4814FCA173D1E44FB77B064D6F41944796509'
    'nvngx.dll'                            = '30E67B2885A1CDF9B3A979CB1B2F8F26BCC7A9CF8D1BFF58925BC248B441EE7A'
    'DLSS5_AIO_Feed.fx'                    = '5CCDD34F1E9F55C10B8F46EF0BA654BCD29B77E1AF9096D44E66E3478E82E192'
    'vort-shaders.zip'                     = '1D7127DB1038266314EB84FAFCCC161829C48C5FAF81FC149C1877E0B94CB6C5'
    'ReShade-6.8.0-32.dll'                 = 'DA430E0A9C6EECEFA0D1B27D05E16C426FB5D04E808B194D914EAAC4B31BC0F8'
    'ReShade-6.8.0-64.dll'                 = '0CEE63F9C9F13F3AC909C5B4903F4DBB4B719A7AB3B4F13B0DEAF83C814B94F7'
    'ReShade-6.3.3-32.dll'                 = '94DDADBAE44CA4A7BE5B797753A8323D985948B0480C383D22EF402C00E031F2'
    'ReShade-6.3.3-64.dll'                 = '8B38372587AAB7289954ED9CA1BDB298ADC9435292E0C0111632832B75DDC49A'
}

function Assert-Dlss5Payload {
    param(
        [Parameter(Mandatory)]
        [string]$Directory,

        [Parameter(Mandatory)]
        [string]$Description
    )

    $missing = @(
        $requiredDlss5Payload | Where-Object {
            -not (Test-Path (Join-Path $Directory $_) -PathType Leaf)
        }
    )
    if ($missing.Count -gt 0) {
        throw "$Description is missing required DLSS 5 payload(s): $($missing -join ', ')."
    }

    $empty = @(
        $requiredDlss5Payload | Where-Object {
            (Get-Item (Join-Path $Directory $_)).Length -eq 0
        }
    )
    if ($empty.Count -gt 0) {
        throw "$Description contains empty DLSS 5 payload(s): $($empty -join ', ')."
    }

    $changed = @(
        $expectedDlss5Hashes.GetEnumerator() | ForEach-Object {
            $path = Join-Path $Directory $_.Key
            if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $_.Value) { $_.Key }
        }
    )
    if ($changed.Count -gt 0) {
        throw "$Description contains DLSS 5 payload(s) that do not match the reviewed upstream package: $($changed -join ', ')."
    }
}

Assert-Dlss5Payload `
    -Directory (Join-Path $repositoryRoot 'RenoDXCommander\Assets\DLSS5') `
    -Description 'Source tree'

$env:DOTNET_CLI_HOME = Join-Path $repositoryRoot '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $repositoryRoot '.nuget'
$env:APPDATA = Join-Path $repositoryRoot '.appdata'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_ROOT = if (Test-Path $localDotnet) { Join-Path $repositoryRoot '.dotnet' } else { $env:DOTNET_ROOT }
Remove-Item Env:MSBuildSDKsPath -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $env:APPDATA -Force | Out-Null

# Publish into a clean directory so removed/renamed payloads cannot leak into
# later installers from an earlier build.
$artifactsFullPath = [System.IO.Path]::GetFullPath($artifacts).TrimEnd([System.IO.Path]::DirectorySeparatorChar)
$publishFullPath = [System.IO.Path]::GetFullPath($publish)
if (-not $publishFullPath.StartsWith($artifactsFullPath + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean publish path outside the artifacts directory: $publishFullPath"
}
if (Test-Path -LiteralPath $publishFullPath) {
    Remove-Item -LiteralPath $publishFullPath -Recurse -Force
}
New-Item -ItemType Directory -Path $publishFullPath -Force | Out-Null

& $dotnet restore (Join-Path $repositoryRoot 'RenoDXCommander.Tests\RenoDXCommander.Tests.csproj') `
    --configfile (Join-Path $repositoryRoot 'NuGet.Config') -p:Platform=x64
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }

if (-not $SkipTests) {
    & $dotnet test (Join-Path $repositoryRoot 'RenoDXCommander.Tests\RenoDXCommander.Tests.csproj') `
        -c Release -p:Platform=x64 --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}

& $dotnet publish (Join-Path $repositoryRoot 'RenoDXCommander\RenoDXCommander.csproj') `
    -c Release -r win-x64 -p:Platform=x64 -p:PublishSingleFile=true `
    --self-contained true --no-restore -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

Assert-Dlss5Payload `
    -Directory (Join-Path $publish 'Assets\DLSS5') `
    -Description 'Publish output'

if ($BuildInstaller) {
    $compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    $compilerPath = if ($compiler -ne $null) { $compiler.Source } else { $null }
    if ($compilerPath -eq $null) {
        $knownCompilerPaths = @(
            (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
        )
        $compilerPath = $knownCompilerPaths | Where-Object { Test-Path $_ } | Select-Object -First 1
    }
    if ($compilerPath -eq $null) { throw 'Inno Setup 6 (ISCC.exe) was not found.' }
    & $compilerPath (Join-Path $repositoryRoot 'Adas Setup.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
}

Write-Host "Adas publish output: $publish"

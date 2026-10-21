param([switch]$SkipPublish)
$ErrorActionPreference='Stop'
$taskRoot=$PSScriptRoot
Set-Location $taskRoot
$sdk=Join-Path $taskRoot '.tools\dotnet\dotnet.exe'
if(!(Test-Path $sdk)){$sdk=(Get-Command dotnet -ErrorAction Stop).Source}
$portable=Join-Path $taskRoot 'dist\Qivoryx-0.4.0-Portable'
# Windows PowerShell 5.1 requires a BOM to read Russian source correctly.
$backend=Join-Path $taskRoot 'Tweaker\Backend.ps1'
[IO.File]::WriteAllText($backend,[IO.File]::ReadAllText($backend),(New-Object Text.UTF8Encoding $true))
if(!$SkipPublish){
    & $sdk publish Tweaker\Tweaker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o $portable
    if($LASTEXITCODE -ne 0){throw 'Publish failed'}
}
Copy-Item docs\USER-GUIDE.md (Join-Path $portable 'README.md') -Force
Set-Content -LiteralPath (Join-Path $portable 'portable.flag') -Value 'Qivoryx portable mode' -Encoding ascii
Get-ChildItem $portable -Filter '*.pdb' | Remove-Item -Force
$zip=Join-Path $taskRoot 'dist\Qivoryx-0.4.0-Portable-x64.zip'
Add-Type -AssemblyName System.IO.Compression.FileSystem
if(Test-Path $zip){Remove-Item -LiteralPath $zip -Force}
$zipFile=[IO.File]::Create($zip)
$zipWriter=[IO.Compression.ZipArchive]::new($zipFile,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach($file in Get-ChildItem -LiteralPath $portable -File -Recurse){
        $relative=[IO.Path]::GetRelativePath($portable,$file.FullName)
        if($relative.StartsWith('Data\',[StringComparison]::OrdinalIgnoreCase)){continue}
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zipWriter,$file.FullName,$relative.Replace('\','/'),[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally {$zipWriter.Dispose();$zipFile.Dispose()}
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler=Join-Path $framework 'csc.exe'
$refs=@("/r:$framework\WPF\PresentationFramework.dll","/r:$framework\WPF\PresentationCore.dll","/r:$framework\WPF\WindowsBase.dll","/r:$framework\System.Xaml.dll","/r:$framework\System.IO.Compression.dll","/r:$framework\System.IO.Compression.FileSystem.dll")
$uninstaller=Join-Path $taskRoot 'artifacts\Uninstall.exe'
New-Item -ItemType Directory -Force artifacts | Out-Null
& $compiler /nologo /target:winexe /optimize+ /define:UNINSTALL "/resource:$taskRoot\Tweaker\Themes\Controls.xaml,Controls" "/win32icon:$taskRoot\Tweaker\Assets\app.ico" "/out:$uninstaller" @refs Installer\Setup.cs
if($LASTEXITCODE -ne 0){throw 'Uninstaller compilation failed'}
$setup=Join-Path $taskRoot 'dist\Qivoryx-0.4.0-Setup-x64.exe'
& $compiler /nologo /target:winexe /optimize+ "/resource:$taskRoot\Tweaker\Themes\Controls.xaml,Controls" "/win32icon:$taskRoot\Tweaker\Assets\app.ico" "/resource:$zip,Payload" "/resource:$uninstaller,Uninstaller" "/out:$setup" @refs Installer\Setup.cs
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed'}
Get-FileHash $zip,$setup -Algorithm SHA256 | Select-Object @{n='File';e={Split-Path $_.Path -Leaf}},Hash | ConvertTo-Json | Set-Content dist\SHA256.json -Encoding utf8
Get-Item $zip,$setup | Select-Object Name,Length

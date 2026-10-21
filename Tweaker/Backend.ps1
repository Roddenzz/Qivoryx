param([Parameter(Mandatory=$true)][string]$Request, [Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding
$r = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($Request)) | ConvertFrom-Json
$store = if(Test-Path (Join-Path $PSScriptRoot 'portable.flag')){Join-Path $PSScriptRoot 'Data'}elseif(Test-Path (Join-Path $env:LOCALAPPDATA 'Monochrome')){Join-Path $env:LOCALAPPDATA 'Monochrome'}else{Join-Path $env:LOCALAPPDATA 'Qivoryx'}
New-Item -ItemType Directory -Path $store -Force | Out-Null
$notes = New-Object 'System.Collections.Generic.List[string]'
function Native([string]$File,[string[]]$Arguments,[int[]]$SuccessCodes=@()) {
    $previousPreference=$ErrorActionPreference
    try { $ErrorActionPreference='Continue'; $text = (& $File @Arguments 2>&1 | Out-String); $code=$LASTEXITCODE }
    finally { $ErrorActionPreference=$previousPreference }
    $notes.Add("$File $($Arguments -join ' ')`n$text")
    $already=@(-1978335189,-1978335135,-1978334963)
    $isWinget=([IO.Path]::GetFileName($File) -ieq 'winget.exe')
    if ($code -notin (@(0,3010,1641)+$SuccessCodes) -and !($isWinget -and $code -in $already)) { throw "Ошибка $File ($code): $text" }
    if($isWinget -and $code -in $already){$notes.Add('Пакет уже установлен; применимого обновления нет.')}
    if ($code -in @(3010,1641)) { $notes.Add('Требуется перезагрузка Windows.') }
    return $text
}
function WriteBackup($Kind,$Value) {
    $dir=Join-Path $store 'Backups'
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    $path=Join-Path $dir ("$Kind-"+(Get-Date -Format 'yyyyMMdd-HHmmss-fff')+'.json')
    $Value | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $path -Encoding UTF8
    return $path
}
function Apps {
    $list=New-Object 'System.Collections.Generic.List[object]'
    foreach ($root in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall','HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall')) {
        Get-ItemProperty "$root\*" -ErrorAction SilentlyContinue | Where-Object DisplayName | ForEach-Object {
            $list.Add([pscustomobject]@{Id=$_.PSPath;Name=$_.DisplayName;Version=$_.DisplayVersion;Publisher=$_.Publisher;Type='Win32';SizeMB=$([math]::Round($_.EstimatedSize/1024));Removable=[bool]($_.UninstallString);Quiet=[bool]($_.QuietUninstallString -or $_.WindowsInstaller -eq 1);Scope=$(if($root.StartsWith('HKCU')){'Пользователь'}else{'Компьютер'})})
        }
    }
    Get-AppxPackage -ErrorAction Stop | ForEach-Object {
        $list.Add([pscustomobject]@{Id=$_.PackageFullName;Name=$_.Name;Version=$_.Version.ToString();Publisher=$_.Publisher;Type='Appx';SizeMB=0;Removable=(!$_.NonRemovable);Quiet=$true;Scope='Пользователь'})
    }
    $managed=Join-Path $store 'Programs'
    if(Test-Path $managed){
      Get-ChildItem -LiteralPath $managed -Directory | ForEach-Object {
        $marker=Join-Path $_.FullName '.qivoryx-package.json'
        if(Test-Path $marker){$p=Get-Content -LiteralPath $marker -Raw -Encoding UTF8 | ConvertFrom-Json;$list.Add([pscustomobject]@{Id=$_.FullName;Name=$p.Name;Version=$p.Version;Publisher='Portable / Qivoryx';Type='ManagedPortable';SizeMB=0;Removable=$true;Quiet=$true;Scope='Пользователь'})}
      }
    }
    return @($list | Sort-Object Name,Version,Id -Unique)
}
function ExpandSafeZip([string]$Archive,[string]$Destination) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $root=[IO.Path]::GetFullPath($Destination)+[IO.Path]::DirectorySeparatorChar
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    $zip=[IO.Compression.ZipFile]::OpenRead($Archive)
    try {
      foreach($entry in $zip.Entries){
        $path=[IO.Path]::GetFullPath((Join-Path $Destination $entry.FullName))
        if(!$path.StartsWith($root,[StringComparison]::OrdinalIgnoreCase)){throw 'Архив содержит путь вне папки распаковки.'}
        if(!$entry.Name){New-Item -ItemType Directory -Force -Path $path | Out-Null;continue}
        New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($path)) | Out-Null
        $input=$entry.Open();$outputFile=[IO.File]::Create($path)
        try {$input.CopyTo($outputFile)} finally {$input.Dispose();$outputFile.Dispose()}
      }
    } finally {$zip.Dispose()}
}
function ServiceInventory {
    $perf=@{}; $counts=@{}
    Get-CimInstance Win32_Service | ForEach-Object { if($_.ProcessId -gt 0){$counts[[int]$_.ProcessId]++} }
    Get-CimInstance Win32_PerfFormattedData_PerfProc_Process -ErrorAction SilentlyContinue | ForEach-Object { if($_.IDProcess -gt 0){$perf[[int]$_.IDProcess]=$_} }
    $cores=[Environment]::ProcessorCount
    return @(Get-CimInstance Win32_Service | ForEach-Object {
        $p=$perf[[int]$_.ProcessId]; $reg=Get-ItemProperty -LiteralPath ("HKLM:\SYSTEM\CurrentControlSet\Services\"+$_.Name) -ErrorAction SilentlyContinue
        [pscustomobject]@{Name=$_.Name;DisplayName=$_.DisplayName;Description=$_.Description;State=$_.State;StartMode=$_.StartMode;Delayed=[bool]$reg.DelayedAutoStart;ProcessId=$_.ProcessId;Cpu=$(if($p){[math]::Round($p.PercentProcessorTime/$cores,1)}else{0});MemoryMB=$(if($p){[math]::Round($p.WorkingSetPrivate/1MB,1)}else{0});Shared=[int]$counts[[int]$_.ProcessId]}
    } | Sort-Object DisplayName)
}
function AssertService([string]$Name) {
    if($Name -notmatch '^[a-zA-Z0-9_.\-]+$'){throw 'Некорректное имя службы.'}
    $critical=@('RpcSs','RpcEptMapper','DcomLaunch','PlugPlay','Power','ProfSvc','SamSs','LSM','EventLog','BFE','MpsSvc','WinDefend','WdNisSvc','SecurityHealthService','wscsvc','gpsvc','Schedule','nsi','Dhcp','Dnscache','Winmgmt','CryptSvc','UserManager','StateRepository','Appinfo','BrokerInfrastructure','CoreMessagingRegistrar','SystemEventsBroker','SENS','KeyIso')
    if($Name -in $critical){throw 'Изменение этой системной службы заблокировано: она необходима для входа, сети, безопасности или работы Windows.'}
}
function CleanRoots {
    return @([pscustomobject]@{Id='user';Name='Временные файлы пользователя';Path=$env:TEMP},[pscustomobject]@{Id='windows';Name='Временные файлы Windows';Path=(Join-Path $env:WINDIR 'Temp')},[pscustomobject]@{Id='shader';Name='Кэш шейдеров DirectX';Path=(Join-Path $env:LOCALAPPDATA 'D3DSCache')},[pscustomobject]@{Id='crash';Name='Дампы сбоев приложений';Path=(Join-Path $env:LOCALAPPDATA 'CrashDumps')})
}
function CleanFiles($Root) {
    # Traverse explicitly: never descend through a junction or symbolic link.
    if(!(Test-Path -LiteralPath $Root.Path)){return @()}
    $rootItem=Get-Item -LiteralPath $Root.Path -Force
    if($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Папка очистки является ссылкой. Операция остановлена.'}
    $stack=New-Object 'System.Collections.Generic.Stack[string]';$stack.Push($rootItem.FullName)
    $cutoff=(Get-Date).AddDays(-2)
    while($stack.Count -gt 0){
        $dir=$stack.Pop()
        foreach($item in @(Get-ChildItem -LiteralPath $dir -Force -ErrorAction SilentlyContinue)){
            if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){continue}
            if($item.PSIsContainer){$stack.Push($item.FullName)}elseif($item.LastWriteTime -lt $cutoff){$item}
        }
    }
}
function ActivePlan {
    $raw=Native 'powercfg.exe' @('/getactivescheme')
    $m=[regex]::Match($raw,'[a-fA-F0-9]{8}(-[a-fA-F0-9]{4}){3}-[a-fA-F0-9]{12}')
    if(!$m.Success){throw 'Не удалось определить активный план питания.'}
    return $m.Value
}
$settings=@{
 min=@('54533251-82be-4824-96c1-47b60b740d00','893dee8e-2bef-41e0-89c6-b55d0929964c',0,100)
 max=@('54533251-82be-4824-96c1-47b60b740d00','bc5038f7-23e0-4960-96da-33abaf5935ec',1,100)
 boost=@('54533251-82be-4824-96c1-47b60b740d00','be337238-0d82-4146-a960-4f3749d470c7',0,6)
 parking=@('54533251-82be-4824-96c1-47b60b740d00','0cc5b647-c1df-4637-891a-dec35c318583',0,100)
 epp=@('54533251-82be-4824-96c1-47b60b740d00','36687f9e-e3a5-4dbf-b1dc-15eb381c6863',0,100)
 cooling=@('54533251-82be-4824-96c1-47b60b740d00','94d3a615-a899-4ac5-ae2b-e4d8f634367f',0,1)
 pcie=@('501a4d13-42af-4429-9fd1-a8218c268e20','ee12f906-d277-404b-b6da-e5fa1a576df5',0,2)
 usb=@('2a737441-1930-4402-8d77-b2bebba308a3','48e6b7a6-50f5-4782-a5d4-53bb8f07e226',0,1)
 disk=@('0012ee47-9041-4b5d-9b77-535fba8b1442','6738e2c4-e8a5-4a42-b16a-e040e769756e',0,3600)
 display=@('7516b95f-f776-4464-8c53-06167f40cc99','3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e',0,7200)
}
. (Join-Path $PSScriptRoot 'Modules\Startup.ps1')
. (Join-Path $PSScriptRoot 'Modules\Performance.ps1')
. (Join-Path $PSScriptRoot 'Modules\Tuning.ps1')
. (Join-Path $PSScriptRoot 'Modules\Gpu.ps1')
try {
    $data=$null; $message='Готово'
    switch($r.op){
      'summary' {
        $os=Get-CimInstance Win32_OperatingSystem; $cpu=Get-CimInstance Win32_Processor | Select-Object -First 1
        $def=Get-MpComputerStatus -ErrorAction SilentlyContinue
        $data=@{Computer=$env:COMPUTERNAME;OS=$os.Caption;Build=$os.BuildNumber;CPU=$cpu.Name;CpuLoad=$cpu.LoadPercentage;RAMTotal=[math]::Round($os.TotalVisibleMemorySize/1MB,1);RAMUsed=[math]::Round(($os.TotalVisibleMemorySize-$os.FreePhysicalMemory)/1MB,1);Uptime=[math]::Round(((Get-Date)-$os.LastBootUpTime).TotalHours,1);Defender=[bool]$def.AntivirusEnabled;Realtime=[bool]$def.RealTimeProtectionEnabled;Tamper=[bool]$def.IsTamperProtected;DefenderKnown=($null -ne $def);Services=@(Get-Service).Count;Running=@(Get-Service | Where-Object Status -eq Running).Count;Drives=@(Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3' | ForEach-Object {@{Name=$_.DeviceID;Total=[math]::Round($_.Size/1GB,1);Free=[math]::Round($_.FreeSpace/1GB,1)}})}
      }
      'startupList' {$data=Get-StartupEntries}
      'startupDisable' {$data=Disable-StartupEntry ([string]$r.id);$message=$data.Message}
      'startupRestore' {$data=Restore-StartupEntry ([string]$r.id);$message=$data.Message}
      'performanceReport' {$data=Get-PerformanceReport}
      'visualTweaks' {$data=@(Get-VisualTweaks)}
      'visualSet' {$data=Set-VisualTweak ([string]$r.id);$message=$data.Message}
      'visualRestore' {$data=Restore-VisualTweak ([string]$r.id);$message=$data.Message}
      'storageOptimize' {$data=Invoke-StorageMaintenance ([string]$r.letter);$message=$data.Message}
      'componentCleanup' {$data=Invoke-ComponentCleanup;$message=$data.Message}
      'componentCheck' {$data=Invoke-ComponentCheck;$message=$data.Message}
      'gpuRead' {$data=Get-GpuReport}
      'gpuPanel' {$data=Invoke-GpuPanel ([string]$r.name);$message=$data.Message}
      'gpuDisplayPreview' {$data=Invoke-GpuDisplayPreview $r;$message=$data.Message}
      'tuningRead' {$data=Get-TuningState}
      'tuningApply' {$data=Invoke-TuningApply ([string]$r.id);$message=$data.Message}
      'tuningRestore' {$data=Invoke-TuningRestore ([string]$r.id);$message=$data.Message}
      'services' {$data=ServiceInventory}
      'service' {
        AssertService $r.name
        $svc=Get-CimInstance Win32_Service -Filter ("Name='"+$r.name+"'")
        if(!$svc){throw 'Служба не найдена.'}
        $reg=Get-ItemProperty -LiteralPath ("HKLM:\SYSTEM\CurrentControlSet\Services\"+$r.name)
        $backup=WriteBackup 'service' @{Name=$svc.Name;StartMode=$svc.StartMode;State=$svc.State;Delayed=[bool]$reg.DelayedAutoStart}
        switch($r.action){
          'start' {Start-Service -Name $r.name -ErrorAction Stop}
          'stop' {Stop-Service -Name $r.name -ErrorAction Stop}
          'auto' {Native 'sc.exe' @('config',$r.name,'start=','auto') | Out-Null}
          'manual' {Native 'sc.exe' @('config',$r.name,'start=','demand') | Out-Null}
          'disable' {Stop-Service -Name $r.name -ErrorAction Stop; Native 'sc.exe' @('config',$r.name,'start=','disabled') | Out-Null}
          default {throw 'Неизвестное действие службы.'}
        }
        $data=@{Backup=$backup};$message='Служба изменена. Предыдущее состояние сохранено.'
      }
      'restoreService' {
        $backupDir=[IO.Path]::GetFullPath((Join-Path $store 'Backups'))+[IO.Path]::DirectorySeparatorChar
        $path=[IO.Path]::GetFullPath([string]$r.path)
        if(!$path.StartsWith($backupDir,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($path) -notlike 'service-*.json'){throw 'Неверный файл резервной копии.'}
        $b=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        AssertService $b.Name
        $mode=switch($b.StartMode){'Auto'{if($b.Delayed){'delayed-auto'}else{'auto'}}'Manual'{'demand'}'Disabled'{'disabled'}default{throw 'Неверный тип запуска.'}}
        Native 'sc.exe' @('config',$b.Name,'start=',$mode) | Out-Null
        if($b.State -eq 'Running'){Start-Service $b.Name -ErrorAction Stop}else{Stop-Service $b.Name -ErrorAction Stop}
        $message='Состояние службы восстановлено.'
      }
      'apps' {$data=@(Apps)}
      'uninstall' {
        # Resolve again from the OS; never trust a caller-provided uninstall command.
        $app=@(Apps | Where-Object Id -eq $r.id) | Select-Object -First 1
        if(!$app -or !$app.Removable){throw 'Windows не предоставляет способ удаления этого компонента.'}
        if($app.Type -eq 'ManagedPortable') {
          $root=[IO.Path]::GetFullPath((Join-Path $store 'Programs'))+[IO.Path]::DirectorySeparatorChar
          $target=[IO.Path]::GetFullPath($app.Id)
          if(!$target.StartsWith($root,[StringComparison]::OrdinalIgnoreCase) -or !(Test-Path (Join-Path $target '.qivoryx-package.json'))){throw 'Неверная папка portable-пакета.'}
          $stack=New-Object 'System.Collections.Generic.Stack[string]';$stack.Push($target)
          while($stack.Count){$dir=$stack.Pop();if((Get-Item -LiteralPath $dir).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Найдена ссылка. Удаление отменено.'};foreach($child in @(Get-ChildItem -LiteralPath $dir -Force)){if($child.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Найдена ссылка. Удаление отменено.'};if($child.PSIsContainer){$stack.Push($child.FullName)}}}
          $marker=Get-Content (Join-Path $target '.qivoryx-package.json') -Raw -Encoding UTF8 | ConvertFrom-Json
          if($marker.Id -notmatch '^[A-Za-z0-9_.-]+$'){throw 'Неверный идентификатор portable-пакета.'}
          Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction Stop
          $shortcut=Join-Path ([Environment]::GetFolderPath('Programs')) ('Qivoryx Tools\'+$marker.Id+'.lnk')
          if(Test-Path -LiteralPath $shortcut){Remove-Item -LiteralPath $shortcut -Force}
        }
        elseif($app.Type -eq 'Appx') {Remove-AppxPackage -Package $app.Id -ErrorAction Stop}
        else {
          $reg=Get-ItemProperty -LiteralPath $app.Id
          if($reg.WindowsInstaller -eq 1 -and $reg.PSChildName -match '^\{[0-9A-Fa-f-]{36}\}$'){
            Native 'msiexec.exe' @('/x',$reg.PSChildName,'/qn','/norestart') | Out-Null
          } else {
            $cmd=$reg.QuietUninstallString; $quiet=$true
            if(!$cmd){$cmd=$reg.UninstallString;$quiet=$false}
            if($cmd -match '^"([^"]+)"\s*(.*)$'){$exe=$Matches[1];$arg=$Matches[2]}
            elseif($cmd -match '^(.+?\.exe)\s*(.*)$'){$exe=$Matches[1];$arg=$Matches[2]}
            else {throw 'Не удалось безопасно разобрать команду удаления. Используйте Параметры Windows.'}
            $si=New-Object Diagnostics.ProcessStartInfo;$si.FileName=$exe;$si.Arguments=$arg;$si.UseShellExecute=$false;$si.CreateNoWindow=$quiet
            $p=[Diagnostics.Process]::Start($si);$p.WaitForExit()
            if($p.ExitCode -notin @(0,3010,1641)){throw "Деинсталлятор завершился с кодом $($p.ExitCode)."}
          }
        }
        $remaining=@(Apps | Where-Object Id -eq $r.id)
        if($remaining.Count -gt 0){$message='Деинсталлятор завершён, но запись ещё есть. Возможно, требуется перезагрузка.'}else{$message='Приложение удалено.'}
      }
      'edge' {
        $setup=Get-ChildItem "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\*\Installer\setup.exe" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if(!$setup){throw 'Деинсталлятор Edge не найден. На этой сборке Windows удаление может быть недоступно.'}
        Native $setup.FullName @('--uninstall','--system-level','--force-uninstall','--verbose-logging') | Out-Null
        if(Test-Path "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"){throw 'Edge остался установлен. Эта сборка Windows не разрешила удаление. WebView2 и файлы системы не изменялись.'}
        $message='Удаление Edge завершено. Windows Update может установить его повторно.'
      }
      'install' {
        $catalog=Get-Content (Join-Path $PSScriptRoot 'catalog.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        $app=$catalog | Where-Object Id -eq $r.id | Select-Object -First 1
        if(!$app){throw 'Приложение отсутствует в каталоге.'}
        $winget=Get-Command winget.exe -ErrorAction SilentlyContinue
        $installed=$false
        if($winget){
          try {Native $winget.Source @('install','--id',$app.Id,'--exact','--source','winget','--silent','--disable-interactivity','--accept-package-agreements','--accept-source-agreements') | Out-Null;$installed=$true}
          catch {$notes.Add($_.Exception.Message)}
        }
        if(!$installed){
          if(!$app.Url){throw 'Winget недоступен или установка не удалась. Для этого пакета нет проверенного прямого установщика. Установите App Installer из Microsoft Store или откройте сайт издателя.'}
          $uri=[uri]$app.Url
          if($uri.Scheme -ne 'https'){throw 'Разрешены только HTTPS-источники.'}
          $dir=Join-Path $store ('Downloads\'+[guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Force $dir | Out-Null
          $path=Join-Path $dir ('setup.'+$app.Extension)
          [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
          Invoke-WebRequest $app.Url -OutFile $path -UseBasicParsing -TimeoutSec 300
          if($app.SHA256){
            if($app.SHA256 -notmatch '^[0-9A-Fa-f]{64}$' -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $app.SHA256){throw 'SHA-256 установщика не совпадает с официальным манифестом winget. Запуск отменён.'}
          } else {
            $sig=Get-AuthenticodeSignature -LiteralPath $path
            if(!$app.Signer -or $sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch $app.Signer){throw 'Цифровая подпись установщика не соответствует издателю. Запуск отменён.'}
          }
          $installer=$path
          if($app.Extension -eq 'zip'){
            $unpack=Join-Path $dir 'Unpacked';ExpandSafeZip $path $unpack
            $installer=[IO.Path]::GetFullPath((Join-Path $unpack $app.NestedPath))
            if(!$installer.StartsWith([IO.Path]::GetFullPath($unpack)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or !(Test-Path -LiteralPath $installer)){throw 'Исполняемый файл внутри архива не найден или путь недопустим.'}
          }
          if($app.InstallerType -eq 'portable'){
            $target=Join-Path $store ('Programs\'+$app.Id)
            if(Test-Path $target){throw 'Управляемая portable-копия уже есть. Удалите её перед повторной установкой.'}
            New-Item -ItemType Directory -Force -Path $target | Out-Null
            if($app.Extension -eq 'zip'){Copy-Item -Path (Join-Path $unpack '*') -Destination $target -Recurse -Force;$entry=Join-Path $target $app.NestedPath}
            else {$entry=Join-Path $target ($app.Name -replace '[^a-zA-Z0-9_-]','_');$entry+='.exe';Copy-Item -LiteralPath $path -Destination $entry}
            @{Id=$app.Id;Name=$app.Name;Version=$app.Version} | ConvertTo-Json | Set-Content (Join-Path $target '.qivoryx-package.json') -Encoding UTF8
            $menu=Join-Path ([Environment]::GetFolderPath('Programs')) 'Qivoryx Tools';New-Item -ItemType Directory -Force $menu | Out-Null
            $shellObject=New-Object -ComObject WScript.Shell;$link=$shellObject.CreateShortcut((Join-Path $menu ($app.Id+'.lnk')));$link.TargetPath=$entry;$link.WorkingDirectory=$target;$link.Save()
          }
          elseif($app.InstallerType -in @('msix','appx')){Add-AppxPackage -Path $installer -ErrorAction Stop}
          elseif($app.Extension -eq 'msi' -or $app.InstallerType -in @('msi','wix')){
            $installArgs=@('/i',$installer)+@($app.Args);if(!$app.InstallerType){$installArgs+=@('/qn','/norestart')};Native 'msiexec.exe' $installArgs @($app.SuccessCodes) | Out-Null
          }
          else {Native $installer @($app.Args) @($app.SuccessCodes) | Out-Null}
          $downloadRoot=[IO.Path]::GetFullPath((Join-Path $store 'Downloads'))+[IO.Path]::DirectorySeparatorChar
          if([IO.Path]::GetFullPath($dir).StartsWith($downloadRoot,[StringComparison]::OrdinalIgnoreCase)){Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue}
        }
        $message='Установщик успешно завершился. Если пакет потребовал перезагрузку, это указано в журнале.'
      }
      'powerRead' {
        $plan=ActivePlan;$raw=Native 'powercfg.exe' @('/qh',$plan)
        $values=@{}
        foreach($key in $settings.Keys){
          $guid=$settings[$key][1];$pos=$raw.IndexOf($guid,[StringComparison]::OrdinalIgnoreCase)
          if($pos -ge 0){
            $tail=$raw.Substring($pos);$end=[regex]::Match($tail.Substring(36),'[a-fA-F0-9]{8}(-[a-fA-F0-9]{4}){3}-[a-fA-F0-9]{12}')
            if($end.Success){$tail=$tail.Substring(0,36+$end.Index)}
            $hex=[regex]::Matches($tail,'0x([0-9a-fA-F]{8})')
            if($hex.Count -ge 2){$values[$key]=@{AC=[convert]::ToInt32($hex[$hex.Count-2].Groups[1].Value,16);DC=[convert]::ToInt32($hex[$hex.Count-1].Groups[1].Value,16)}}
          }
        }
        $data=@{Plan=$plan;Values=$values}
      }
      'powerApply' {
        if($r.source -notin @('AC','DC')){throw 'Выберите питание от сети или батареи.'}
        foreach($prop in $r.values.PSObject.Properties){
          if(!$settings.ContainsKey($prop.Name)){throw 'Неизвестный параметр питания.'}
          $s=$settings[$prop.Name];$v=[int]$prop.Value
          if($v -lt $s[2] -or $v -gt $s[3]){throw 'Значение вне диапазона.'}
        }
        if([int]$r.values.min -gt [int]$r.values.max){throw 'Минимальное состояние процессора не может превышать максимальное.'}
        $old=ActivePlan;$new=[guid]::NewGuid().ToString()
        $path=WriteBackup 'power' @{Original=$old;Created=$new}
        Native 'powercfg.exe' @('/duplicatescheme',$old,$new) | Out-Null
        try {
          Native 'powercfg.exe' @('/changename',$new,'Qivoryx - custom') | Out-Null
          foreach($prop in $r.values.PSObject.Properties){$s=$settings[$prop.Name];Native 'powercfg.exe' @("/set$($r.source.ToLower())valueindex",$new,$s[0],$s[1],[string]$prop.Value) | Out-Null}
          Native 'powercfg.exe' @('/setactive',$new) | Out-Null
        } catch {
          $failure=$_
          try {Native 'powercfg.exe' @('/setactive',$old) | Out-Null} catch {$notes.Add('Ошибка возврата исходного плана: '+$_.Exception.Message)}
          try {Native 'powercfg.exe' @('/delete',$new) | Out-Null} catch {$notes.Add('Ошибка удаления копии плана: '+$_.Exception.Message)}
          throw $failure
        }
        $data=@{Backup=$path};$message='Параметры применены к копии плана. Исходный план сохранён.'
      }
      'powerRestore' {
        $latest=Get-ChildItem (Join-Path $store 'Backups\power-*.json') | Sort-Object Name -Descending | Select-Object -First 1
        if(!$latest){throw 'Резервная копия плана не найдена.'}
        $b=Get-Content $latest.FullName -Raw | ConvertFrom-Json
        if($b.Original -notmatch '^[0-9a-fA-F-]{36}$'){throw 'Неверный GUID плана.'}
        Native 'powercfg.exe' @('/setactive',$b.Original) | Out-Null
        $message='Включён предыдущий план питания.'
      }
      'cleanScan' {
        $data=@(CleanRoots | ForEach-Object {$root=$_;$files=@(CleanFiles $root);[pscustomobject]@{Id=$root.Id;Name=$root.Name;Path=$root.Path;Count=$files.Count;Bytes=($files | Measure-Object Length -Sum).Sum}})
      }
      'clean' {
        $deleted=0L;$skipped=0;$count=0
        foreach($root in @(CleanRoots | Where-Object {$_.Id -in @($r.ids)})){
          foreach($file in @(CleanFiles $root)){
            try {
              # Revalidate the entire ancestry immediately before deleting.
              $parent=$file.Directory;$safe=$true
              while($parent){if($parent.Attributes -band [IO.FileAttributes]::ReparsePoint){$safe=$false;break};$parent=$parent.Parent}
              if(!$safe){$skipped++;continue}
              $size=$file.Length;Remove-Item -LiteralPath $file.FullName -Force -ErrorAction Stop;$deleted+=$size;$count++
            } catch {$skipped++}
          }
        }
        $data=@{Bytes=$deleted;Count=$count;Skipped=$skipped};$message="Удалено файлов: $count. Занятых или недоступных пропущено: $skipped."
      }
      'restorePoint' {Checkpoint-Computer -Description 'Qivoryx - before changes' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop;$message='Запрос создания точки восстановления выполнен. Проверьте её в Защите системы.'}
      'defenderStatus' {$m=Get-MpComputerStatus -ErrorAction Stop;$data=@{Enabled=$m.AntivirusEnabled;Realtime=$m.RealTimeProtectionEnabled;Tamper=$m.IsTamperProtected;Version=$m.AMProductVersion}}
      default {throw 'Неизвестная операция.'}
    }
    $result=@{ok=$true;data=$data;message=$message;log=($notes -join "`r`n")}
} catch {$result=@{ok=$false;data=$null;message=$_.Exception.Message;log=($notes -join "`r`n")}}
$result | ConvertTo-Json -Depth 16 -Compress | Set-Content -LiteralPath $Output -Encoding UTF8

# Read-only diagnostics and narrowly scoped, reversible desktop settings.
# This module is dot-sourced by Backend.ps1; no action is executed on import.
$script:visualDefinitions=@(
    @{Id='client';Name='Анимации окон';Get=0x1042;Set=0x1043},
    @{Id='menus';Name='Анимации меню';Get=0x1002;Set=0x1003},
    @{Id='tooltips';Name='Анимации подсказок';Get=0x1016;Set=0x1017}
)
function Initialize-DesktopApi {
    if('Qivoryx.DesktopSettings' -as [type]){return}
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
namespace Qivoryx {
    public static class DesktopSettings {
        [DllImport("user32.dll", EntryPoint="SystemParametersInfoW", SetLastError=true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetValue(uint action, uint parameter, out int value, uint flags);
        [DllImport("user32.dll", EntryPoint="SystemParametersInfoW", SetLastError=true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetValue(uint action, uint parameter, IntPtr value, uint flags);
        public static bool Read(uint action) {
            int value;
            if(!GetValue(action, 0, out value, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return value != 0;
        }
        public static void Write(uint action, bool enabled) {
            if(!SetValue(action, 0, new IntPtr(enabled ? 1 : 0), 3)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }
}
'@
}
function Get-VisualDefinition([string]$Id) {
    $definition=$script:visualDefinitions | Where-Object {$_.Id -ceq $Id} | Select-Object -First 1
    if(!$definition){throw 'Неизвестная настройка интерфейса.'}
    return $definition
}
function Read-VisualValue($Definition) {Initialize-DesktopApi;return [Qivoryx.DesktopSettings]::Read([uint32]$Definition.Get)}
function Write-VisualValue($Definition,[bool]$Value) {Initialize-DesktopApi;[Qivoryx.DesktopSettings]::Write([uint32]$Definition.Set,$Value)}
function Get-VisualBackupPath([string]$Id) {
    Get-VisualDefinition $Id | Out-Null
    $dir=Join-Path $store 'Backups'
    # Never follow a redirected portable-data folder when writing backups.
    $ancestor=[IO.DirectoryInfo]::new([IO.Path]::GetFullPath($dir))
    while($ancestor){if($ancestor.Exists -and ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Папка резервных копий содержит ссылку.'};$ancestor=$ancestor.Parent}
    $path=Join-Path $dir ('visual-'+$Id+'.json')
    if((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Файл резервной копии является ссылкой.'}
    return $path
}
function Get-VisualOwner {return [Security.Principal.WindowsIdentity]::GetCurrent().User.Value}
function Read-VisualBackup([string]$Id) {
    $path=Get-VisualBackupPath $Id
    if(!(Test-Path -LiteralPath $path)){return $null}
    $backup=Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
    if($backup.Schema -ne 1 -or $backup.Id -cne $Id -or $backup.Owner -cne (Get-VisualOwner) -or $backup.Original -isnot [bool] -or $backup.Applied -isnot [bool] -or $backup.Applied -ne $false -or $backup.State -notin @('Prepared','Applied','Restored','RolledBack')){throw 'Резервная копия настройки повреждена или относится к другому пользователю.'}
    return $backup
}
function Save-VisualBackup([string]$Id,$Backup) {
    $path=Get-VisualBackupPath $Id
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($path)) -Force | Out-Null
    $Backup | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $path -Encoding UTF8
}
function Get-VisualTweaks {
    foreach($definition in $script:visualDefinitions){
        try {
            $enabled=Read-VisualValue $definition
            $backup=Read-VisualBackup $definition.Id
            [pscustomobject]@{Id=$definition.Id;Name=$definition.Name;Available=$true;Enabled=$enabled;CanRestore=($null -ne $backup -and $backup.State -in @('Prepared','Applied'));Error=''}
        }catch{[pscustomobject]@{Id=$definition.Id;Name=$definition.Name;Available=$false;Enabled=$null;CanRestore=$false;Error=$_.Exception.Message}}
    }
}
function Set-VisualTweak([string]$Id) {
    $definition=Get-VisualDefinition $Id
    $current=Read-VisualValue $definition
    $previous=Read-VisualBackup $Id
    if(!$current){return [pscustomobject]@{Changed=$false;Message='Эта анимация уже выключена. Ничего не изменено.'}}
    if($previous -and $previous.State -in @('Prepared','Applied')){throw 'После применения настройка изменена вне Qivoryx. Сохранённое состояние не перезаписано.'}
    $backup=[pscustomobject]@{Schema=1;Id=$Id;Owner=(Get-VisualOwner);Original=[bool]$current;Applied=$false;State='Prepared';Created=(Get-Date).ToString('o')}
    Save-VisualBackup $Id $backup
    try {
        Write-VisualValue $definition $false
        if(Read-VisualValue $definition){throw 'Windows не применила настройку.'}
        $backup.State='Applied';Save-VisualBackup $Id $backup
    }catch{
        $failure=$_
        try {Write-VisualValue $definition $current;if((Read-VisualValue $definition) -ne $current){throw 'Проверка отката не пройдена.'};$backup.State='RolledBack';Save-VisualBackup $Id $backup}catch{$notes.Add('Автоматический откат настройки не завершён: '+$_.Exception.Message)}
        throw $failure
    }
    return [pscustomobject]@{Changed=$true;Message='Анимация выключена. Исходное значение сохранено; его можно вернуть на этой странице.'}
}
function Restore-VisualTweak([string]$Id) {
    $definition=Get-VisualDefinition $Id
    $backup=Read-VisualBackup $Id
    if(!$backup -or $backup.State -notin @('Prepared','Applied')){throw 'Активная резервная копия этой настройки не найдена.'}
    $current=Read-VisualValue $definition
    if($current -ne $backup.Applied){throw 'Настройка изменена вне Qivoryx. Откат отменён, чтобы не перезаписать новое значение.'}
    Write-VisualValue $definition $backup.Original
    if((Read-VisualValue $definition) -ne $backup.Original){throw 'Windows не восстановила исходную настройку. Резервная копия сохранена.'}
    $backup.State='Restored';Save-VisualBackup $Id $backup
    return [pscustomobject]@{Changed=$true;Message='Исходная настройка интерфейса восстановлена.'}
}
function Get-PerformanceReport {
    $before=@{};$sampleStart=[Diagnostics.Stopwatch]::StartNew()
    foreach($p in @(Get-Process -ErrorAction SilentlyContinue)){try{$before[$p.Id]=@{Cpu=$p.TotalProcessorTime.TotalSeconds;Started=$p.StartTime.ToUniversalTime().Ticks}}catch{}}
    Start-Sleep -Milliseconds 1000
    $elapsed=$sampleStart.Elapsed.TotalSeconds
    $cores=[Environment]::ProcessorCount
    $processes=@(foreach($p in @(Get-Process -ErrorAction SilentlyContinue)){
        if($p.Id -eq 0){continue}
        $cpu=$null
        try {
            $b=$before[$p.Id]
            if($b -and $b.Started -eq $p.StartTime.ToUniversalTime().Ticks){$cpu=[math]::Round([math]::Max(0,[math]::Min(100,($p.TotalProcessorTime.TotalSeconds-$b.Cpu)/$elapsed/$cores*100)),1)}
        }catch{}
        try {$ram=[math]::Round($p.WorkingSet64/1MB,1)}catch{$ram=0}
        [pscustomobject]@{Name=$p.ProcessName;Pid=$p.Id;Cpu=$cpu;MemoryMB=$ram}
    })
    $os=Get-CimInstance Win32_OperatingSystem
    $total=[double]$os.TotalVisibleMemorySize*1024;$available=[double]$os.FreePhysicalMemory*1024
    $cpuLoads=@(Get-CimInstance Win32_Processor | Where-Object {$null -ne $_.LoadPercentage} | Select-Object -ExpandProperty LoadPercentage)
    $cpu=if($cpuLoads.Count){[math]::Round(($cpuLoads | Measure-Object -Average).Average,1)}else{$null}
    $drives=@(Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3' | Where-Object {$_.Size -gt 0} | ForEach-Object {[pscustomobject]@{Letter=$_.DeviceID.Substring(0,1);Name=$_.DeviceID;Free=[long]$_.FreeSpace;Total=[long]$_.Size;FreePercent=[math]::Round($_.FreeSpace/$_.Size*100,1);FileSystem=$_.FileSystem}})
    return [pscustomobject]@{Timestamp=(Get-Date).ToString('o');SampleSeconds=[math]::Round($elapsed,2);Cpu=$cpu;MemoryTotal=[long]$total;MemoryAvailable=[long]$available;MemoryUsedPercent=[math]::Round(($total-$available)/$total*100,1);Processes=$processes.Count;TopCpu=@($processes | Sort-Object @{Expression={if($null -eq $_.Cpu){-1}else{$_.Cpu}};Descending=$true},@{Expression='MemoryMB';Descending=$true} | Select-Object -First 8);TopMemory=@($processes | Sort-Object MemoryMB -Descending | Select-Object -First 8);Drives=$drives;VisualTweaks=@(Get-VisualTweaks)}
}
function Assert-MaintenanceVolume([string]$Letter) {
    if($Letter -cnotmatch '^[A-Za-z]$'){throw 'Укажите одну букву локального диска.'}
    $normalized=$Letter.ToUpperInvariant()
    $volume=Get-CimInstance Win32_LogicalDisk -Filter ("DeviceID='"+$normalized+":' AND DriveType=3")
    if(!$volume -or $volume.FileSystem -notin @('NTFS','ReFS')){throw 'Нужен доступный локальный том NTFS или ReFS.'}
    return $normalized
}
function Invoke-StorageMaintenance([string]$Letter) {
    $valid=Assert-MaintenanceVolume $Letter
    # Deliberately omit -Defrag/-ReTrim: Windows selects the operation for the medium.
    $output=Optimize-Volume -DriveLetter $valid -Verbose -ErrorAction Stop 4>&1 | Out-String
    $notes.Add($output)
    return [pscustomobject]@{Message="Обслуживание диска ${valid}: завершено. Тип операции выбрала Windows."}
}
function Invoke-ComponentCleanup {
    Native (Join-Path $env:WINDIR 'System32\dism.exe') @('/Online','/Cleanup-Image','/StartComponentCleanup','/NoRestart') | Out-Null
    return [pscustomobject]@{Message='Обслуживание хранилища компонентов завершено. /ResetBase не использовался.'}
}
function Invoke-ComponentCheck {
    $details=Native (Join-Path $env:WINDIR 'System32\dism.exe') @('/Online','/Cleanup-Image','/CheckHealth','/NoRestart')
    return [pscustomobject]@{Message='Проверен ранее сохранённый статус хранилища компонентов. Это не полное сканирование и не исправление ошибок.';Details=$details}
}

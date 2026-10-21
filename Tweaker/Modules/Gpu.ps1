function Initialize-GpuNative {
    if('QivoryxDisplayNative' -as [type]){return}
    Add-Type -TypeDefinition @'
using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public sealed class QivoryxDisplay {
    public string Name, Adapter, AdapterId, MonitorId;
    public uint Width, Height, Bits, Frequency, Orientation, Flags, FixedOutput;
    public int PositionX, PositionY;
    public uint SavedFrequency;
    public bool SavedGeometryMatches;
    public int[] Modes;
}
public static class QivoryxDisplayNative {
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct Device {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string Description;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string Key;
    }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct Mode {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string DeviceName;
        public ushort SpecVersion, DriverVersion, Size, DriverExtra;
        public uint Fields;
        public int PositionX, PositionY;
        public uint Orientation, FixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string FormName;
        public ushort LogPixels;
        public uint Bits, Width, Height, Flags, Frequency, IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }
    [DllImport("user32.dll", EntryPoint="EnumDisplayDevicesW", CharSet=CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] static extern bool EnumDevices(string device, uint index, ref Device info, uint flags);
    [DllImport("user32.dll", EntryPoint="EnumDisplaySettingsExW", CharSet=CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] static extern bool EnumSettings(string device, int index, ref Mode mode, uint flags);
    [DllImport("user32.dll", EntryPoint="ChangeDisplaySettingsExW", CharSet=CharSet.Unicode)]
    static extern int ChangeSettings(string device, ref Mode mode, IntPtr window, uint flags, IntPtr parameter);
    static Mode EmptyMode() { return new Mode { Size=(ushort)Marshal.SizeOf(typeof(Mode)) }; }
    static Mode Current(string name) {
        Mode m=EmptyMode();
        if(!EnumSettings(name,-1,ref m,0)) throw new InvalidOperationException("Не удалось прочитать режим монитора.");
        return m;
    }
    public static int ModeSize() { return Marshal.SizeOf(typeof(Mode)); }
    public static QivoryxDisplay[] Read() {
        var result=new List<QivoryxDisplay>();
        for(uint index=0;index<64;index++) {
            var d=new Device { Size=Marshal.SizeOf(typeof(Device)) };
            if(!EnumDevices(null,index,ref d,0)) break;
            if((d.Flags & 1)==0 || (d.Flags & 8)!=0) continue;
            Mode current=EmptyMode();if(!EnumSettings(d.Name,-1,ref current,0)) continue;
            Mode saved=EmptyMode();bool hasSaved=EnumSettings(d.Name,-2,ref saved,0);
            bool savedGeometry=hasSaved && saved.Width==current.Width && saved.Height==current.Height && saved.Bits==current.Bits && saved.Orientation==current.Orientation && saved.Flags==current.Flags && saved.PositionX==current.PositionX && saved.PositionY==current.PositionY && saved.FixedOutput==current.FixedOutput;
            var monitor=new Device { Size=Marshal.SizeOf(typeof(Device)) };
            string monitorId="";if(EnumDevices(d.Name,0,ref monitor,1)) monitorId=monitor.Id ?? "";
            var frequencies=new SortedSet<int>();
            for(int i=0;i<20000;i++) {
                Mode candidate=EmptyMode();if(!EnumSettings(d.Name,i,ref candidate,0)) break;
                if(candidate.Width==current.Width && candidate.Height==current.Height && candidate.Bits==current.Bits && candidate.Orientation==current.Orientation && candidate.Flags==current.Flags && candidate.Frequency>1 && candidate.Frequency<=2000)
                    frequencies.Add((int)candidate.Frequency);
            }
            result.Add(new QivoryxDisplay { Name=d.Name, Adapter=d.Description, AdapterId=d.Id ?? "", MonitorId=monitorId,
                Width=current.Width,Height=current.Height,Bits=current.Bits,Frequency=current.Frequency,Orientation=current.Orientation,Flags=current.Flags,PositionX=current.PositionX,PositionY=current.PositionY,FixedOutput=current.FixedOutput,SavedFrequency=hasSaved ? saved.Frequency : 0,SavedGeometryMatches=savedGeometry,Modes=frequencies.ToArray() });
        }
        return result.ToArray();
    }
    public static void SetFrequency(string name, int frequency, bool save, bool test) {
        QivoryxDisplay display=Read().FirstOrDefault(d=>String.Equals(d.Name,name,StringComparison.OrdinalIgnoreCase));
        if(display==null || !display.Modes.Contains(frequency)) throw new InvalidOperationException("Частота не объявлена драйвером для текущего режима.");
        Mode mode=Current(name);mode.Frequency=(uint)frequency;mode.Fields |= 0x00400000; // DM_DISPLAYFREQUENCY
        int code=ChangeSettings(name,ref mode,IntPtr.Zero,test ? 2u : (save ? 1u : 0u),IntPtr.Zero);
        if(code!=0) throw new InvalidOperationException("Windows не применила режим экрана (код "+code+").");
    }
}
'@ -ErrorAction Stop
}
function Get-GpuNativeDisplays {Initialize-GpuNative;return @([QivoryxDisplayNative]::Read())}
function Set-GpuNativeFrequency([string]$Device,[int]$Frequency,[bool]$Save,[bool]$Test) {Initialize-GpuNative;[QivoryxDisplayNative]::SetFrequency($Device,$Frequency,$Save,$Test)}
function Get-GpuOwnerSid {return [Security.Principal.WindowsIdentity]::GetCurrent().User.Value}
function Get-GpuSafeDirectory([string]$Name) {
    if($Name -notin @('Backups','GpuPreview')){throw 'Неверная папка графической настройки.'}
    $directory=[IO.Path]::GetFullPath((Join-Path $store $Name));$ancestor=[IO.DirectoryInfo]::new($directory)
    while($ancestor){
        if(Test-Path -LiteralPath $ancestor.FullName){$item=Get-Item -LiteralPath $ancestor.FullName -Force;if(!$item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Папка данных графики или её родитель является ссылкой.'}}
        $ancestor=$ancestor.Parent
    }
    return $directory
}
function Convert-GpuMode($Value) {
    if(!$Value -or $Value.Name -isnot [string] -or $Value.Name -notmatch '^\\\\\.\\DISPLAY[0-9]+$' -or $Value.AdapterId -isnot [string] -or $Value.MonitorId -isnot [string]){throw 'Повреждён идентификатор монитора.'}
    $numbers=@{}
    foreach($key in @('Width','Height','Bits','Frequency','Orientation','Flags','FixedOutput','PositionX','PositionY')){
        $v=$Value.$key
        $minimum=if($key -in @('PositionX','PositionY')){-100000}else{0}
        if($v -isnot [ValueType] -or [double]$v -ne [long]$v -or [long]$v -lt $minimum -or [long]$v -gt 100000){throw 'Повреждён режим монитора.'}
        $numbers[$key]=[long]$v
    }
    if($numbers.Width -lt 1 -or $numbers.Height -lt 1 -or $numbers.Bits -lt 1 -or $numbers.Bits -gt 64 -or $numbers.Frequency -lt 2 -or $numbers.Frequency -gt 2000 -or $numbers.Orientation -gt 3 -or $numbers.FixedOutput -gt 2){throw 'Недопустимые параметры режима монитора.'}
    return [pscustomobject]@{Name=[string]$Value.Name;AdapterId=[string]$Value.AdapterId;MonitorId=[string]$Value.MonitorId;Width=$numbers.Width;Height=$numbers.Height;Bits=$numbers.Bits;Frequency=$numbers.Frequency;Orientation=$numbers.Orientation;Flags=$numbers.Flags;FixedOutput=$numbers.FixedOutput;PositionX=$numbers.PositionX;PositionY=$numbers.PositionY}
}
function Test-GpuSameMode($First,$Second) {
    $a=Convert-GpuMode $First;$b=Convert-GpuMode $Second
    return ($a|ConvertTo-Json -Compress) -ceq ($b|ConvertTo-Json -Compress)
}
function Get-GpuCurrent([string]$Device) {
    $display=@(Get-GpuNativeDisplays | Where-Object Name -eq $Device) | Select-Object -First 1
    if(!$display){throw 'Монитор больше не подключён. Изменение отменено.'}
    return $display
}
function Get-GpuSavedBackup([string]$Device) {
    $directory=Get-GpuSafeDirectory 'Backups'
    if(!(Test-Path -LiteralPath $directory)){return $null}
    foreach($file in @(Get-ChildItem -LiteralPath $directory -Filter 'gpu-display-*.json' -File | Sort-Object Name -Descending)){
        if($file.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Резервная копия графики является ссылкой.'}
        $b=Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
        if($b.Version -ne 1 -or [string]$b.OwnerSid -cne [string](Get-GpuOwnerSid)){continue}
        $before=Convert-GpuMode $b.Before;$applied=Convert-GpuMode $b.Applied
        if($applied.Name -ne $Device){continue}
        if($b.Completed -ne $true -or $b.Restored -eq $true){continue}
        # A backup may only change frequency; forged geometry/identity is never restored.
        $copy=Convert-GpuMode $applied;$copy.Frequency=$before.Frequency
        if(!(Test-GpuSameMode $copy $before)){throw 'Резервная копия меняет другие параметры экрана.'}
        return [pscustomobject]@{Path=$file.FullName;Before=$before;Applied=$applied;Record=$b}
    }
    return $null
}
function Write-GpuBackupRecord([string]$Path,$Record) {
    $directory=Get-GpuSafeDirectory 'Backups';$full=[IO.Path]::GetFullPath($Path)
    if([IO.Path]::GetDirectoryName($full) -ine $directory -or [IO.Path]::GetFileName($full) -notmatch '^gpu-display-[0-9-]+\.json$'){throw 'Неверный путь копии режима экрана.'}
    if((Test-Path -LiteralPath $full) -and ((Get-Item -LiteralPath $full -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Резервная копия является ссылкой.'}
    $temporary=$full+'.'+[guid]::NewGuid().ToString('N')+'.tmp'
    try{[IO.File]::WriteAllText($temporary,($Record|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($true));if([IO.File]::Exists($full)){[IO.File]::Replace($temporary,$full,[NullString]::Value)}else{[IO.File]::Move($temporary,$full)}}finally{if([IO.File]::Exists($temporary)){[IO.File]::Delete($temporary)}}
}
function Test-GpuAsymmetricX3D([string]$Name,[int]$Cores) {
    # Explicit, known asymmetric dual-CCD models; do not infer future X3D/X3D2 topology.
    return $Cores -gt 8 -and $Name -match '(?i)\bRyzen\s+9\s+(?:7900X3D|7950X3D|9900X3D|9950X3D)\b'
}
function Get-GpuX3DReadiness {
    try{$cpu=Get-CimInstance Win32_Processor -ErrorAction Stop|Select-Object -First 1}catch{return $null}
    if(!(Test-GpuAsymmetricX3D ([string]$cpu.Name) ([int]$cpu.NumberOfCores))){return $null}
    $service='Не удалось прочитать';$gameBar='Не удалось прочитать';$gameMode='Не удалось прочитать'
    try{
        $svc=Get-CimInstance Win32_Service -Filter "Name='amd3dvcacheSvc'" -ErrorAction Stop
        $service=if($svc){[string]$svc.State+' / запуск: '+[string]$svc.StartMode}else{'Служба не обнаружена'}
    }catch{}
    try{$package=Get-AppxPackage -Name 'Microsoft.XboxGamingOverlay' -ErrorAction Stop|Select-Object -First 1;$gameBar=if($package){'Установлена для текущего пользователя ('+[string]$package.Version+')'}else{'Не обнаружена у текущего пользователя'}}catch{}
    try{
        $key=Get-Item -LiteralPath 'HKCU:\Software\Microsoft\GameBar' -ErrorAction SilentlyContinue
        $parts=@(foreach($name in @('AutoGameModeEnabled','AllowAutoGameMode')){
            if($key -and $key.GetValueNames() -contains $name){$name+' = '+[string]$key.GetValue($name)}else{$name+' не задано явно'}
        });$gameMode=$parts -join '; '
    }catch{}
    return @{Model=[string]$cpu.Name;Service=$service;GameBar=$gameBar;GameMode=$gameMode}
}
function Get-GpuReport {
    $adapters=@(Get-CimInstance Win32_VideoController | ForEach-Object {
        $vendor=if($_.PNPDeviceID -match 'VEN_10DE'){'NVIDIA'}elseif($_.PNPDeviceID -match 'VEN_1002'){'AMD'}elseif($_.PNPDeviceID -match 'VEN_8086'){'Intel'}else{'Другой'}
        [pscustomobject]@{Name=$_.Name;Vendor=$vendor;Driver=$_.DriverVersion;Status=$_.Status;PnpId=$_.PNPDeviceID}
    })
    $displays=@(foreach($display in @(Get-GpuNativeDisplays)){
        $canRestore=$false;$errorText=$null
        try{$backup=Get-GpuSavedBackup $display.Name;if($backup){$canRestore=Test-GpuSameMode $display $backup.Applied}}catch{$errorText=$_.Exception.Message}
        [pscustomobject]@{Name=$display.Name;Adapter=$display.Adapter;Width=$display.Width;Height=$display.Height;Frequency=$display.Frequency;Modes=@($display.Modes);CanRestore=$canRestore;Error=$errorText}
    })
    return @{Adapters=$adapters;Displays=$displays;HasNvidia=[bool]@($adapters|Where-Object Vendor -eq 'NVIDIA').Count;HasAmd=[bool]@($adapters|Where-Object Vendor -eq 'AMD').Count;HasIntel=[bool]@($adapters|Where-Object Vendor -eq 'Intel').Count;X3D=(Get-GpuX3DReadiness)}
}
function Write-GpuPreviewState([string]$Token,$Value) {
    if($Token -notmatch '^[a-f0-9]{32}$'){throw 'Неверный идентификатор проверки экрана.'}
    $directory=Get-GpuSafeDirectory 'GpuPreview'
    $path=Join-Path $directory ('state-'+$Token+'.json')
    if((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Файл состояния экрана является ссылкой.'}
    $temporary=$path+'.'+[guid]::NewGuid().ToString('N')+'.tmp'
    try{
        [IO.File]::WriteAllText($temporary,($Value|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($true))
        if([IO.File]::Exists($path)){[IO.File]::Replace($temporary,$path,[NullString]::Value)}else{[IO.File]::Move($temporary,$path)}
    }finally{if([IO.File]::Exists($temporary)){[IO.File]::Delete($temporary)}}
}
function Wait-GpuPreviewAnswer([string]$Token,[DateTime]$Deadline) {
    $directory=Get-GpuSafeDirectory 'GpuPreview';$path=Join-Path $directory ('answer-'+$Token+'.txt')
    $watch=[Diagnostics.Stopwatch]::StartNew()
    while($watch.Elapsed.TotalSeconds -lt 15){
        $null=Get-GpuSafeDirectory 'GpuPreview'
        if(Test-Path -LiteralPath $path){
            if((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){return $false}
            try{return ([IO.File]::ReadAllText($path).Trim() -ceq 'confirm')}catch{return $false}
        }
        Start-Sleep -Milliseconds 100
    }
    return $false
}
function Restore-GpuPreviewIfUnchanged($Before,$Applied,[bool]$RestorePersistent=$false) {
    try {
        $current=Get-GpuCurrent $Before.Name
        if(!(Test-GpuSameMode $current $Applied)){return $false}
        Set-GpuNativeFrequency $Before.Name ([int]$Before.Frequency) $RestorePersistent $false
        if(!(Test-GpuSameMode (Get-GpuCurrent $Before.Name) $Before)){throw 'Windows не подтвердила возврат режима.'}
        return $true
    }catch{$notes.Add('Возврат предпросмотра экрана: '+$_.Exception.Message);return $false}
}
function Invoke-GpuDisplayPreview($Request) {
    if([string]$Request.token -notmatch '^[a-f0-9]{32}$'){throw 'Неверный идентификатор проверки экрана.'}
    if($Request.restore -isnot [bool]){throw 'Неверный режим операции экрана.'}
    $device=[string]$Request.device;$native=Get-GpuCurrent $device;$before=Convert-GpuMode $native
    if(!$before.MonitorId -or !$before.AdapterId){throw 'Windows не предоставила устойчивый идентификатор монитора. Используйте параметры экрана.'}
    if(!$native.SavedGeometryMatches -or [int]$native.SavedFrequency -ne [int]$before.Frequency){throw 'Активный режим отличается от сохранённого режима Windows. Закройте полноэкранные приложения или используйте параметры экрана.'}
    $backup=$null
    if($Request.restore){
        $backup=Get-GpuSavedBackup $device
        if(!$backup -or !(Test-GpuSameMode $before $backup.Applied)){throw 'Исходный режим недоступен для автоматического отката: параметры или монитор изменились.'}
        $target=Convert-GpuMode $backup.Before
    }else{
        if($Request.frequency -isnot [ValueType] -or [double]$Request.frequency -ne [int]$Request.frequency){throw 'Частота должна быть целым числом из списка драйвера.'}
        $frequency=[int]$Request.frequency
        if($frequency -notin @($native.Modes)){throw 'Драйвер не объявил выбранную частоту для этого режима.'}
        $target=Convert-GpuMode $before;$target.Frequency=$frequency
    }
    if(Test-GpuSameMode $before $target){return @{Message='Эта частота уже используется. Экран не изменён.';Confirmed=$false}}
    if([int]$target.Frequency -notin @($native.Modes)){throw 'Исходная частота больше не поддерживается драйвером.'}
    $previewDirectory=Get-GpuSafeDirectory 'GpuPreview';New-Item -ItemType Directory -Path $previewDirectory -Force | Out-Null
    $backupDirectory=Get-GpuSafeDirectory 'Backups';New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
    $lock=$null;$previewStarted=$false;$committed=$false;$persistentAttempted=$false;$deadline=[DateTime]::UtcNow
    try {
        $lockPath=Join-Path $previewDirectory 'active.lock'
        if((Test-Path -LiteralPath $lockPath) -and ((Get-Item -LiteralPath $lockPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Блокировка предпросмотра является ссылкой.'}
        $lock=[IO.File]::Open($lockPath,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
        # Never accept an old/precreated answer; the UI token must be fresh.
        if(Test-Path -LiteralPath (Join-Path $previewDirectory ('answer-'+$Request.token+'.txt'))){throw 'Этот идентификатор предпросмотра уже использовался.'}
        if(!(Test-GpuSameMode (Get-GpuCurrent $device) $before)){throw 'Параметры экрана изменились перед предпросмотром.'}
        Set-GpuNativeFrequency $device ([int]$target.Frequency) $false $true
        if(!$Request.restore){
            $record=@{Version=1;OwnerSid=(Get-GpuOwnerSid);Before=$before;Applied=$target;Completed=$false;Restored=$false}
            $backupPath=WriteBackup 'gpu-display' $record
        }else{$record=$backup.Record;$backupPath=$backup.Path}
        $previewStarted=$true
        Set-GpuNativeFrequency $device ([int]$target.Frequency) $false $false
        if(!(Test-GpuSameMode (Get-GpuCurrent $device) $target)){throw 'Контрольное чтение не подтвердило выбранную частоту.'}
        $deadline=[DateTime]::UtcNow.AddSeconds(15)
        Write-GpuPreviewState $Request.token @{Phase='preview';DeadlineUtc=$deadline.ToString('o');Device=$device;Frequency=$target.Frequency}
        $confirmed=Wait-GpuPreviewAnswer $Request.token $deadline
        if(!$confirmed -or [DateTime]::UtcNow -ge $deadline){
            $returned=Restore-GpuPreviewIfUnchanged $before $target;$previewStarted=$false
            $text=if($returned){'Предпросмотр отменён. Предыдущая частота восстановлена.'}else{'Предпросмотр завершён. Режим изменился извне или монитор отключён; внешние изменения сохранены. Проверьте параметры экрана.'}
            return @{Message=$text;Confirmed=$false}
        }
        $checked=Get-GpuCurrent $device
        if(!(Test-GpuSameMode $checked $target)){throw 'Экран изменён другой программой; сохранение отменено.'}
        if(!$checked.SavedGeometryMatches -or [int]$checked.SavedFrequency -ne [int]$before.Frequency){throw 'Сохранённый режим Windows изменён другой программой; сохранение отменено.'}
        $persistentAttempted=$true
        Set-GpuNativeFrequency $device ([int]$target.Frequency) $true $false
        if(!(Test-GpuSameMode (Get-GpuCurrent $device) $target)){throw 'Windows не подтвердила сохранение режима.'}
        if($Request.restore){$record.Restored=$true}else{$record.Completed=$true}
        Write-GpuBackupRecord $backupPath $record
        $committed=$true
        return @{Message=$(if($Request.restore){'Исходная частота монитора восстановлена.'}else{'Частота монитора сохранена. Предыдущий режим доступен для возврата.'});Confirmed=$true}
    }catch{
        $failure=$_
        if($previewStarted -and !$committed){$null=Restore-GpuPreviewIfUnchanged $before $target $persistentAttempted}
        throw $failure
    }finally{
        if($lock){$lock.Dispose()}
        try{Write-GpuPreviewState $Request.token @{Phase='finished';DeadlineUtc=$deadline.ToString('o');Confirmed=$committed}}catch{}
    }
}
function Invoke-GpuPanel([string]$Name) {
    $start=New-Object Diagnostics.ProcessStartInfo;$start.UseShellExecute=$true
    switch($Name){
        'windows' {$start.FileName='ms-settings:display-advancedgraphics'}
        'display' {$start.FileName='ms-settings:display-advanced'}
        'nvidia' {
            $report=Get-GpuReport;if(!$report.HasNvidia){throw 'NVIDIA не обнаружена в системе.'}
            $paths=@((Join-Path $env:ProgramFiles 'NVIDIA Corporation\Control Panel Client\nvcplui.exe'),(Join-Path $env:WINDIR 'System32\nvcplui.exe'))
            $exe=$paths|Where-Object {Test-Path -LiteralPath $_}|Select-Object -First 1
            if($exe){$start.FileName=$exe}else{
                $package=Get-AppxPackage -Name 'NVIDIACorp.NVIDIAControlPanel' -ErrorAction SilentlyContinue|Select-Object -First 1
                if(!$package){throw 'Панель NVIDIA не найдена. Установите официальный драйвер и NVIDIA Control Panel.'}
                $manifest=$package|Get-AppxPackageManifest;$id=[string]@($manifest.Package.Applications.Application)[0].Id
                if($id -notmatch '^[A-Za-z0-9._-]+$' -or $package.PackageFamilyName -notmatch '^NVIDIACorp\.NVIDIAControlPanel_[A-Za-z0-9]+$'){throw 'Не удалось определить приложение панели NVIDIA.'}
                $start.FileName=(Join-Path $env:WINDIR 'explorer.exe');$start.Arguments='shell:AppsFolder\'+$package.PackageFamilyName+'!'+$id
            }
        }
        'amd' {
            $report=Get-GpuReport;if(!$report.HasAmd){throw 'AMD не обнаружена в системе.'}
            $paths=@((Join-Path $env:ProgramFiles 'AMD\CNext\CNext\RadeonSoftware.exe'),(Join-Path $env:ProgramFiles 'AMD\CNext\CNext\RadeonSettings.exe'))
            $exe=$paths|Where-Object {Test-Path -LiteralPath $_}|Select-Object -First 1
            if(!$exe){throw 'AMD Software не найдена. Установите официальный пакет драйвера с панелью управления.'};$start.FileName=$exe
        }
        default {throw 'Неизвестная панель графики.'}
    }
    [Diagnostics.Process]::Start($start)|Out-Null
    return @{Message='Открыта панель настроек графики.'}
}

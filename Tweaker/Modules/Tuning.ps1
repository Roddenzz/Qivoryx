# Individual reversible desktop settings.
function Assert-PBId([string]$Id) {
    if($Id -notin @('menu','mouse','capture','transparency')){throw 'Неизвестная настройка.'}
}
function Get-PBOwnerSid {return [Security.Principal.WindowsIdentity]::GetCurrent().User.Value}
function Assert-PBBackupDirectory {
    $directory=[IO.Path]::GetFullPath((Join-Path $store 'Backups'))
    $ancestor=[IO.DirectoryInfo]::new($directory)
    while($ancestor){
        if(Test-Path -LiteralPath $ancestor.FullName){
            $item=Get-Item -LiteralPath $ancestor.FullName -Force
            if(!$item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Папка резервных копий или её родитель является ссылкой. Операция отменена.'}
        }
        $ancestor=$ancestor.Parent
    }
    return $directory
}
function Write-PBRecord([string]$Path,$Record) {
    $directory=Assert-PBBackupDirectory
    $full=[IO.Path]::GetFullPath($Path)
    if([IO.Path]::GetDirectoryName($full) -ine $directory -or [IO.Path]::GetFileName($full) -notmatch '^playbook-(menu|mouse|capture|transparency)-[0-9-]+\.json$'){throw 'Неверный путь резервной копии настройки.'}
    if((Test-Path -LiteralPath $full) -and ((Get-Item -LiteralPath $full -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Резервная копия является ссылкой.'}
    $temporary=$full+'.'+[guid]::NewGuid().ToString('N')+'.tmp'
    try {
        [IO.File]::WriteAllText($temporary,($Record | ConvertTo-Json -Depth 10),[Text.UTF8Encoding]::new($true))
        if([IO.File]::Exists($full)){[IO.File]::Replace($temporary,$full,[NullString]::Value)}else{[IO.File]::Move($temporary,$full)}
    }finally{if([IO.File]::Exists($temporary)){[IO.File]::Delete($temporary)}}
}
function Get-PBRegistryDefinition([string]$Id) {
    switch($Id){
        'capture' { return @(@{Path='System\GameConfigStore';Name='GameDVR_Enabled'},@{Path='Software\Microsoft\Windows\CurrentVersion\GameDVR';Name='AppCaptureEnabled'}) }
        'transparency' { return @(@{Path='Software\Microsoft\Windows\CurrentVersion\Themes\Personalize';Name='EnableTransparency'}) }
        default {throw 'Твик не является настройкой реестра.'}
    }
}
function Initialize-PBNative {
    if('QivoryxTuningNative' -as [type]){return}
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class QivoryxTuningNative {
    [DllImport("user32.dll", EntryPoint="SystemParametersInfoW", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetNumber(uint action, uint parameter, ref int value, uint flags);
    [DllImport("user32.dll", EntryPoint="SystemParametersInfoW", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetNumber(uint action, uint parameter, IntPtr value, uint flags);
    [DllImport("user32.dll", EntryPoint="SystemParametersInfoW", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Mouse(uint action, uint parameter, [In, Out] int[] values, uint flags);
}
'@ -ErrorAction Stop
}
function Get-PBSnapshot([string]$Id) {
    Assert-PBId $Id
    if($Id -eq 'menu'){
        Initialize-PBNative
        $value=0
        # WinUser.h: SPI_GETMENUSHOWDELAY is decimal 106, hexadecimal 0x006A.
        if(![QivoryxTuningNative]::GetNumber(0x006A,0,[ref]$value,0)){throw 'Windows не вернула задержку открытия меню.'}
        return [pscustomobject]@{Kind='Menu';Value=$value}
    }
    if($Id -eq 'mouse'){
        Initialize-PBNative
        $values=[int[]]@(0,0,0)
        if(![QivoryxTuningNative]::Mouse(3,0,$values,0)){throw 'Windows не вернула параметры ускорения мыши.'}
        return [pscustomobject]@{Kind='Mouse';Value=@($values)}
    }
    $values=@(foreach($definition in @(Get-PBRegistryDefinition $Id)){
        $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($definition.Path,$false)
        try {
            $exists=$null -ne $key -and $definition.Name -in @($key.GetValueNames())
            if($exists){
                [pscustomobject]@{Exists=$true;Kind=$key.GetValueKind($definition.Name).ToString();Value=$key.GetValue($definition.Name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)}
            }else{[pscustomobject]@{Exists=$false;Kind=$null;Value=$null}}
        } finally {if($key){$key.Dispose()}}
    })
    return [pscustomobject]@{Kind='Registry';Values=$values}
}
function Convert-PBSnapshot([string]$Id,$State) {
    Assert-PBId $Id
    if($Id -eq 'menu'){
        if($State.Kind -ne 'Menu' -or $State.Value -isnot [ValueType] -or [double]$State.Value -ne [int]$State.Value -or [int]$State.Value -lt 0 -or [int]$State.Value -gt 65535){throw 'Повреждена копия задержки меню.'}
        return [pscustomobject]@{Kind='Menu';Value=[int]$State.Value}
    }
    if($Id -eq 'mouse'){
        $v=@($State.Value)
        if($State.Kind -ne 'Mouse' -or $v.Count -ne 3){throw 'Повреждена копия параметров мыши.'}
        foreach($n in $v){if($n -isnot [ValueType] -or [double]$n -ne [int]$n -or [int]$n -lt 0 -or [int]$n -gt 100){throw 'Недопустимые параметры мыши в копии.'}}
        if([int]$v[2] -gt 2){throw 'Недопустимый режим ускорения мыши.'}
        return [pscustomobject]@{Kind='Mouse';Value=@([int]$v[0],[int]$v[1],[int]$v[2])}
    }
    $definitions=@(Get-PBRegistryDefinition $Id);$records=@($State.Values)
    if($State.Kind -ne 'Registry' -or $records.Count -ne $definitions.Count){throw 'Повреждена копия значений реестра.'}
    $values=@(foreach($record in $records){
        if($record.Exists -isnot [bool]){throw 'Неверный признак существования значения в копии.'}
        if(!$record.Exists){[pscustomobject]@{Exists=$false;Kind=$null;Value=$null};continue}
        $value=$record.Value
        switch([string]$record.Kind){
            'DWord' {if($value -isnot [ValueType] -or [double]$value -ne [int]$value){throw 'Неверный DWORD в копии.'};$value=[int]$value}
            'QWord' {if($value -isnot [ValueType] -or [double]$value -ne [long]$value){throw 'Неверный QWORD в копии.'};$value=[long]$value}
            'String' {if($value -isnot [string]){throw 'Неверная строка в копии.'}}
            'ExpandString' {if($value -isnot [string]){throw 'Неверная строка в копии.'}}
            'MultiString' {foreach($item in @($value)){if($item -isnot [string]){throw 'Неверный список строк в копии.'}};$value=[string[]]@($value)}
            'Binary' {foreach($item in @($value)){if($item -isnot [ValueType] -or [double]$item -ne [int]$item -or [int]$item -lt 0 -or [int]$item -gt 255){throw 'Неверные двоичные данные в копии.'}};$value=[byte[]]@($value)}
            default {throw 'Неподдерживаемый тип реестра в копии.'}
        }
        [pscustomobject]@{Exists=$true;Kind=[string]$record.Kind;Value=$value}
    })
    return [pscustomobject]@{Kind='Registry';Values=$values}
}
function Get-PBTarget([string]$Id) {
    Assert-PBId $Id
    switch($Id){
        'menu' {return [pscustomobject]@{Kind='Menu';Value=100}}
        'mouse' {return [pscustomobject]@{Kind='Mouse';Value=@(0,0,0)}}
        default {return [pscustomobject]@{Kind='Registry';Values=@(Get-PBRegistryDefinition $Id | ForEach-Object {[pscustomobject]@{Exists=$true;Kind='DWord';Value=0}})}}
    }
}
function Test-PBSame([string]$Id,$First,$Second) {
    $a=Convert-PBSnapshot $Id $First;$b=Convert-PBSnapshot $Id $Second
    return ($a | ConvertTo-Json -Depth 8 -Compress) -ceq ($b | ConvertTo-Json -Depth 8 -Compress)
}
function Set-PBSnapshot([string]$Id,$Snapshot) {
    $state=Convert-PBSnapshot $Id $Snapshot
    if($Id -eq 'menu'){
        Initialize-PBNative
        if(![QivoryxTuningNative]::SetNumber(0x006B,[uint32]$state.Value,[IntPtr]::Zero,3)){throw 'Windows не применила задержку меню.'}
        return
    }
    if($Id -eq 'mouse'){
        Initialize-PBNative;$values=[int[]]@($state.Value)
        if(![QivoryxTuningNative]::Mouse(4,0,$values,3)){throw 'Windows не применила параметры мыши.'}
        return
    }
    $definitions=@(Get-PBRegistryDefinition $Id)
    for($i=0;$i -lt $definitions.Count;$i++){
        $d=$definitions[$i];$v=$state.Values[$i]
        if($v.Exists){$key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($d.Path)}else{$key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($d.Path,$true)}
        if(!$key){continue}
        try {if($v.Exists){$key.SetValue($d.Name,$v.Value,[Microsoft.Win32.RegistryValueKind]([Enum]::Parse([Microsoft.Win32.RegistryValueKind],$v.Kind)))}else{$key.DeleteValue($d.Name,$false)}}
        finally {$key.Dispose()}
    }
}
function Get-PBBackup([string]$Id) {
    Assert-PBId $Id
    $directory=Assert-PBBackupDirectory
    if(!(Test-Path -LiteralPath $directory)){return $null}
    foreach($latest in @(Get-ChildItem -LiteralPath $directory -Filter ('playbook-'+$Id+'-*.json') -File | Sort-Object Name -Descending)){
        if($latest.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Резервная копия является ссылкой.'}
        $backup=Get-Content -LiteralPath $latest.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
        if($backup.Id -ne $Id -or $backup.Version -ne 1){throw 'Копия принадлежит другому твику или версии.'}
        if([string]$backup.OwnerSid -cne [string](Get-PBOwnerSid)){throw 'Резервная копия принадлежит другому пользователю Windows. Откат отменён.'}
        $before=Convert-PBSnapshot $Id $backup.Before;$applied=Convert-PBSnapshot $Id $backup.Applied
        if(!(Test-PBSame $Id $applied (Get-PBTarget $Id))){throw 'Копия содержит недопустимые применённые значения.'}
        # A verified rollback after a failed apply must not hide an older valid backup.
        if($backup.Completed -ne $true){
            if($backup.RolledBack -eq $true){continue}
            throw 'Последняя операция не завершена; её откат не подтверждён. Проверьте текущие параметры и копию вручную.'
        }
        return [pscustomobject]@{Path=$latest.FullName;Before=$before;Applied=$applied;Restored=[bool]$backup.Restored}
    }
    return $null
}
function Get-TuningState {
    return @(foreach($id in @('menu','mouse','capture','transparency')){
        try {
            $snapshot=Get-PBSnapshot $id;$target=Get-PBTarget $id
            $optimized=Test-PBSame $id $snapshot $target
            if($id -eq 'menu'){$optimized=$snapshot.Value -le 100}
            if($id -eq 'mouse'){$optimized=$snapshot.Value[2] -eq 0}
            $label=switch($id){
                'menu' {"$($snapshot.Value) мс"}
                'mouse' {if($snapshot.Value[2] -eq 0){'Ускорение выключено'}else{'Ускорение включено'}}
                default {if($optimized){'Выключено'}else{'Смешанное / стандартное'}}
            }
            $canRestore=$false;$restoreBlocked=$false;$backupError=$null
            try {$backup=Get-PBBackup $id;if($backup -and !$backup.Restored){$canRestore=Test-PBSame $id $snapshot $backup.Applied;$restoreBlocked=!$canRestore}}
            catch {$backupError=$_.Exception.Message}
            [pscustomobject]@{Id=$id;Available=$true;Optimized=$optimized;CurrentLabel=$label;CanRestore=$canRestore;RestoreBlocked=$restoreBlocked;Error=$backupError}
        } catch {[pscustomobject]@{Id=$id;Available=$false;Optimized=$false;CurrentLabel='Недоступно';CanRestore=$false;RestoreBlocked=$false;Error=$_.Exception.Message}}
    })
}
function Invoke-TuningApply([string]$Id) {
    Assert-PBId $Id
    $before=Get-PBSnapshot $Id;$target=Get-PBTarget $Id
    if($Id -eq 'menu' -and $before.Value -le 100){return @{Message='Задержка меню уже не выше 100 мс. Ваше более быстрое значение сохранено без изменений.';Backup=$null}}
    if($Id -eq 'mouse' -and $before.Value[2] -eq 0){return @{Message='Ускорение указателя уже выключено. Дополнительных изменений не требуется.';Backup=$null}}
    if(Test-PBSame $Id $before $target){return @{Message='Эти значения уже заданы. Система не изменена.';Backup=$null}}
    # Persist before touching the OS, including absent values and their exact registry types.
    $null=Assert-PBBackupDirectory
    $record=@{Version=1;Id=$Id;OwnerSid=(Get-PBOwnerSid);Before=$before;Applied=$target;Completed=$false;Restored=$false;RolledBack=$false}
    $path=WriteBackup ('playbook-'+$Id) $record
    $null=Assert-PBBackupDirectory
    try {
        Set-PBSnapshot $Id $target
        $observed=Get-PBSnapshot $Id
        if(!(Test-PBSame $Id $observed $target)){throw 'Контрольное чтение не подтвердило применение твика.'}
        $record.Completed=$true
        Write-PBRecord $path $record
    } catch {
        $failure=$_
        try {
            Set-PBSnapshot $Id $before
            if(!(Test-PBSame $Id (Get-PBSnapshot $Id) $before)){throw 'Контрольное чтение не подтвердило возврат исходных значений.'}
            $record.Completed=$false;$record.RolledBack=$true
            Write-PBRecord $path $record
        }catch{$notes.Add('Не удалось подтвердить автоматический возврат исходных значений: '+$_.Exception.Message)}
        throw $failure
    }
    return @{Message='Настройка применена. Исходные значения сохранены; откат доступен на этой странице. Для записи игр и прозрачности может потребоваться выход из учётной записи.';Backup=$path}
}
function Invoke-TuningRestore([string]$Id) {
    Assert-PBId $Id
    $backup=Get-PBBackup $Id
    if(!$backup -or $backup.Restored){throw 'Активная резервная копия этого твика не найдена.'}
    $current=Get-PBSnapshot $Id
    if(!(Test-PBSame $Id $current $backup.Applied)){throw 'Настройки изменены вне Qivoryx. Автоматический откат отменён, чтобы не затереть эти изменения.'}
    try {
        Set-PBSnapshot $Id $backup.Before
        if(!(Test-PBSame $Id (Get-PBSnapshot $Id) $backup.Before)){throw 'Контрольное чтение не подтвердило восстановление.'}
        $record=Get-Content -LiteralPath $backup.Path -Raw -Encoding UTF8 | ConvertFrom-Json
        $record.Restored=$true
        Write-PBRecord $backup.Path $record
    } catch {
        $failure=$_
        try {Set-PBSnapshot $Id $current}catch{$notes.Add('Ошибка возврата состояния после неудачного отката: '+$_.Exception.Message)}
        throw $failure
    }
    return @{Message='Восстановлены исходные значения этого твика, включая отсутствовавшие записи. Для записи игр и прозрачности может потребоваться выход из учётной записи.';Backup=$backup.Path}
}

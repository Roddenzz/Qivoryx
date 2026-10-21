# Native integration of the Defender stages from the supplied sys.ps1.
# Fixed targets only; Windows protection failures are reported, never bypassed.
function Get-RemovalDefenderStatus {
    try {
        $state=Get-MpComputerStatus -ErrorAction Stop
        return [pscustomobject]@{Enabled=$state.AntivirusEnabled;Realtime=$state.RealTimeProtectionEnabled;Tamper=$state.IsTamperProtected;Version=$state.AMProductVersion;Available=$true}
    } catch {return [pscustomobject]@{Enabled=$null;Realtime=$null;Tamper=$null;Version='Не определена';Available=$false}}
}
function Get-DefenderRemovalTargets {
    $targets=New-Object 'System.Collections.Generic.List[object]'
    foreach($name in @('WinDefend','WdNisSvc','WdBoot','WdFilter','WdNisDrv')) {
        $servicePath='HKLM:\SYSTEM\CurrentControlSet\Services\'+$name
        $targets.Add([pscustomobject]@{Kind='Служба';Name=$name;Path=$servicePath;Exists=((Test-Path -LiteralPath $servicePath) -or [bool](Get-Service -Name $name -ErrorAction SilentlyContinue))})
    }
    foreach($path in @((Join-Path $env:ProgramFiles 'Windows Defender'),$(if(${env:ProgramFiles(x86)}){Join-Path ${env:ProgramFiles(x86)} 'Windows Defender'}),(Join-Path $env:ProgramData 'Microsoft\Windows Defender'),(Join-Path $env:windir 'System32\drivers\WdBoot.sys'),(Join-Path $env:windir 'System32\drivers\WdFilter.sys'),(Join-Path $env:windir 'System32\drivers\WdNisDrv.sys')) | Where-Object {$_} | Select-Object -Unique) {
        $targets.Add([pscustomobject]@{Kind='Файлы';Name=[IO.Path]::GetFileName($path);Path=$path;Exists=(Test-Path -LiteralPath $path)})
    }
    foreach($path in @('HKLM:\SOFTWARE\Policies\Microsoft\Windows Defender','HKLM:\SOFTWARE\Microsoft\Windows Defender','HKLM:\SOFTWARE\Microsoft\WBEM\WMI\NetTracer\Microsoft-Windows-Windows-Defender')) {
        $targets.Add([pscustomobject]@{Kind='Реестр';Name=($path.Split('\')[-1]);Path=$path;Exists=(Test-Path -LiteralPath $path)})
    }
    return $targets.ToArray()
}
function Get-DefenderRemovalPlan {
    $state=Get-RemovalDefenderStatus
    $planId=[guid]::NewGuid().ToString('N')
    $folder=Join-Path $store 'RemovalPlans';New-Item -ItemType Directory -Path $folder -Force | Out-Null
    [pscustomobject]@{Kind='Defender';CreatedAt=[DateTime]::UtcNow.ToString('o');PlanId=$planId} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $folder ($planId+'.json')) -Encoding UTF8
    $allowed=$state.Available -and $state.Tamper -eq $false
    return [pscustomobject]@{PlanId=$planId;Status=$state;CanApply=$allowed;Targets=@(Get-DefenderRemovalTargets);Warnings=@('Удаление файлов и служб необратимо из приложения. Экспорт реестра не восстанавливает компоненты Windows.','Защищённые или занятые компоненты Windows могут отказать в удалении. Каждый отказ попадёт в отчёт.','Центр безопасности Windows, SmartScreen и службы других защитных продуктов не удаляются.');Message=$(if($allowed){'План готов. Изменения ещё не применены.'}elseif(!$state.Available){'Не удалось достоверно проверить состояние Defender. Запуск удаления недоступен.'}else{'Windows сообщает, что включена защита от подделки. Запуск удаления недоступен; приложение не обходит защиту Windows.'})}
}
function Assert-DefenderPlainTree([string]$Path) {
    # Validate every ancestor, and every nested link before any recursive deletion.
    $item=Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    $ancestor=$item
    while($ancestor) {if($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Символическая ссылка или точка соединения: удаление отменено.'};$ancestor=$(if($ancestor.PSIsContainer){$ancestor.Parent}else{$ancestor.Directory})}
    if($item.PSIsContainer) {
        $pending=New-Object 'System.Collections.Generic.Stack[string]';$pending.Push($item.FullName)
        while($pending.Count) {foreach($child in @(Get-ChildItem -LiteralPath $pending.Pop() -Force -ErrorAction Stop)) {if($child.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Внутри папки есть ссылка: удаление отменено.'};if($child.PSIsContainer){$pending.Push($child.FullName)}}}
    }
}
function Invoke-DefenderRemoval([string]$PlanId) {
    if($PlanId -notmatch '^[a-f0-9]{32}$'){throw 'Неверный идентификатор плана.'}
    $planPath=Join-Path (Join-Path $store 'RemovalPlans') ($PlanId+'.json')
    $plan=Get-Content -LiteralPath $planPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if($plan.Kind -ne 'Defender'){throw 'Неверный тип плана.'}
    $age=([DateTime]::UtcNow-[DateTime]::Parse($plan.CreatedAt).ToUniversalTime()).TotalHours
    if($age -lt 0 -or $age -gt 24){throw 'План устарел. Выполните проверку снова.'}
    $state=Get-RemovalDefenderStatus
    if(!$state.Available -or $state.Tamper -ne $false){throw 'Состояние защиты изменилось или недоступно. Удаление не запускалось.'}
    $folder=Join-Path (Get-RemovalDirectory -Machine -Create) ('Defender-'+$PlanId)
    Assert-RemovalPlainPath $folder
    if(Test-Path -LiteralPath $folder){throw 'Этот план уже использован. Выполните проверку снова.'}
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    $items=New-Object 'System.Collections.Generic.List[object]'
    $targets=@(Get-DefenderRemovalTargets)
    function Save-DefenderRegistry([string]$Path,[string]$Label) {
        if(Test-Path -LiteralPath $Path) {
            $nativePath=$Path.Replace('HKLM:\','HKEY_LOCAL_MACHINE\')
            Native (Join-Path $env:windir 'System32\reg.exe') @('export',$nativePath,(Join-Path $folder ($Label+'.reg')),'/y') | Out-Null
        }
    }
    # Export BEFORE mutation. Missing exports prevent that particular action.
    foreach($target in $targets | Where-Object Kind -eq 'Служба') {
        if(!$target.Exists){$items.Add([pscustomobject]@{Kind=$target.Kind;Target=$target.Name;State='Отсутствует';Detail='Служба не зарегистрирована.'});continue}
        try {
            Save-DefenderRegistry $target.Path $target.Name
            $service=Get-Service -Name $target.Name -ErrorAction Stop
            if($service.Status -ne 'Stopped'){Stop-Service -Name $target.Name -Force -ErrorAction Stop}
            Native (Join-Path $env:windir 'System32\sc.exe') @('config',$target.Name,'start=','disabled') | Out-Null
            Native (Join-Path $env:windir 'System32\sc.exe') @('delete',$target.Name) | Out-Null
            $items.Add([pscustomobject]@{Kind=$target.Kind;Target=$target.Name;State='Запрошено';Detail='Удаление службы запрошено. Проверка после перезагрузки обязательна.'})
        } catch {$items.Add([pscustomobject]@{Kind=$target.Kind;Target=$target.Name;State='Ошибка';Detail=$_.Exception.Message})}
    }
    foreach($target in $targets | Where-Object Kind -eq 'Файлы') {
        if(!$target.Exists){$items.Add([pscustomobject]@{Kind=$target.Kind;Target=$target.Path;State='Отсутствует';Detail='Путь не найден.'});continue}
        try {
            Assert-DefenderPlainTree $target.Path
            Remove-Item -LiteralPath $target.Path -Recurse -Force -ErrorAction Stop
            if(Test-Path -LiteralPath $target.Path){throw 'Путь остался после удаления.'}
            $items.Add([pscustomobject]@{Kind=$target.Kind;Target=$target.Path;State='Удалено';Detail='Отсутствие пути проверено.'})
        } catch {$items.Add([pscustomobject]@{Kind=$target.Kind;Target=$target.Path;State='Ошибка';Detail=$_.Exception.Message})}
    }
    foreach($target in $targets | Where-Object Kind -eq 'Реестр') {
        try {
            Save-DefenderRegistry $target.Path ('Registry-'+[array]::IndexOf($targets,$target))
            if($target.Path -eq 'HKLM:\SOFTWARE\Policies\Microsoft\Windows Defender') {
                New-Item -Path $target.Path -Force | Out-Null
                New-ItemProperty -LiteralPath $target.Path -Name DisableAntiSpyware -Value 1 -PropertyType DWord -Force | Out-Null
                $real=Join-Path $target.Path 'Real-Time Protection';New-Item -Path $real -Force | Out-Null
                foreach($name in @('DisableBehaviorMonitoring','DisableOnAccessProtection','DisableRealtimeMonitoring')) {New-ItemProperty -LiteralPath $real -Name $name -Value 1 -PropertyType DWord -Force | Out-Null}
                foreach($name in @('DisableBehaviorMonitoring','DisableOnAccessProtection','DisableRealtimeMonitoring')) {if((Get-ItemPropertyValue -LiteralPath $real -Name $name) -ne 1){throw 'Политика не сохранилась.'}}
                $resultState='Применено';$detail='Значения политик записаны и прочитаны обратно; это не подтверждает отключение антивируса.'
            } else {
                if(Test-Path -LiteralPath $target.Path){Remove-Item -LiteralPath $target.Path -Recurse -Force -ErrorAction Stop}
                if(Test-Path -LiteralPath $target.Path){throw 'Ключ остался после удаления.'}
                $resultState='Удалено';$detail='Ключ отсутствует.'
            }
            $items.Add([pscustomobject]@{Kind=$target.Kind;Target=$target.Path;State=$resultState;Detail=$detail})
        } catch {$items.Add([pscustomobject]@{Kind=$target.Kind;Target=$target.Path;State='Ошибка';Detail=$_.Exception.Message})}
    }
    $remaining=@(Get-DefenderRemovalTargets | Where-Object { $_.Exists -and $_.Path -ne 'HKLM:\SOFTWARE\Policies\Microsoft\Windows Defender' })
    $after=Get-RemovalDefenderStatus
    $complete=$remaining.Count -eq 0 -and $after.Available -and $after.Enabled -eq $false -and @($items | Where-Object State -eq 'Ошибка').Count -eq 0
    $message=if($complete){'Проверяемые компоненты удалены. Перезагрузите ПК и повторно проверьте защиту.'}else{'Удаление выполнено частично: Windows сохранила часть компонентов или отказала в доступе. Полное удаление не подтверждено.'}
    $reportPath=Join-Path $folder 'report.json'
    $report=[pscustomobject]@{Complete=$complete;RestartRequired=$true;Items=$items.ToArray();Remaining=$remaining;Status=$after;ReportPath=$reportPath;Message=$message}
    $report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $reportPath -Encoding UTF8
    $notes.Add($message);$notes.Add($reportPath)
    return $report
}

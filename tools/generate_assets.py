import json, pathlib
from PIL import Image, ImageDraw, ImageFont

root = pathlib.Path(__file__).resolve().parents[1]
rows = '''
Связь|Telegram.TelegramDesktop|Telegram|Сообщения, звонки и каналы|https://desktop.telegram.org
Связь|Discord.Discord|Discord|Голосовые каналы и игровые сообщества|https://discord.com
Связь|TeamSpeakSystems.TeamSpeakClient|TeamSpeak|Голосовая связь с низкой задержкой|https://teamspeak.com
Связь|OpenWhisperSystems.Signal|Signal|Личные сообщения и звонки|https://signal.org
Связь|Beeper.Beeper|Beeper|Объединённый клиент мессенджеров|https://www.beeper.com
Связь|Rakuten.Viber|Viber|Мессенджер и видеозвонки|https://www.viber.com
Связь|SlackTechnologies.Slack|Slack|Рабочие каналы и команды|https://slack.com
Связь|Zoom.Zoom|Zoom|Видеоконференции и встречи|https://zoom.us
Связь|Microsoft.Teams|Microsoft Teams|Командная работа и видеосвязь|https://www.microsoft.com/microsoft-teams
Связь|Element.Element|Element|Клиент защищённой сети Matrix|https://element.io
Связь|Mumble.Mumble.Client|Mumble|Голосовая связь с открытым кодом|https://www.mumble.info
Связь|Tencent.WeChat|WeChat|Сообщения и общение|https://www.wechat.com
Браузеры|Mozilla.Firefox|Firefox|Браузер с независимым движком|https://www.mozilla.org/firefox
Браузеры|Google.Chrome|Google Chrome|Браузер и синхронизация Google|https://www.google.com/chrome
Браузеры|Brave.Brave|Brave|Браузер с защитой приватности|https://brave.com
Браузеры|Vivaldi.Vivaldi|Vivaldi|Гибко настраиваемый браузер|https://vivaldi.com
Браузеры|Opera.Opera|Opera|Браузер со встроенными инструментами|https://www.opera.com
Браузеры|Opera.OperaGX|Opera GX|Игровой браузер с лимитами ресурсов|https://www.opera.com/gx
Браузеры|Microsoft.Edge|Microsoft Edge|Браузер Microsoft на Chromium|https://www.microsoft.com/edge
Браузеры|TorProject.TorBrowser|Tor Browser|Браузер для работы через сеть Tor|https://www.torproject.org
Медиа|VideoLAN.VLC|VLC|Универсальный видео- и аудиоплеер|https://www.videolan.org/vlc
Медиа|clsid2.mpc-hc|MPC-HC|Лёгкий видеоплеер с кодеками|https://github.com/clsid2/mpc-hc
Медиа|MPC-BE.MPC-BE|MPC-BE|Видеоплеер Media Player Classic BE|https://sourceforge.net/projects/mpcbe
Медиа|PeterPawlowski.foobar2000|foobar2000|Аудиоплеер с тонкой настройкой|https://www.foobar2000.org
Медиа|AIMP.AIMP|AIMP|Музыкальный плеер и медиатека|https://www.aimp.ru
Медиа|Spotify.Spotify|Spotify|Музыкальный стриминг|https://www.spotify.com
Медиа|Apple.iTunes|iTunes|Медиатека и устройства Apple|https://www.apple.com/itunes
Медиа|OBSProject.OBSStudio|OBS Studio|Запись экрана и прямые трансляции|https://obsproject.com
Медиа|Audacity.Audacity|Audacity|Запись и редактирование аудио|https://www.audacityteam.org
Медиа|HandBrake.HandBrake|HandBrake|Конвертация и сжатие видео|https://handbrake.fr
Медиа|Gyan.FFmpeg|FFmpeg|Инструменты обработки аудио и видео|https://ffmpeg.org
Медиа|CodecGuide.K-LiteCodecPack.Standard|K-Lite Codecs|Набор мультимедийных кодеков|https://codecguide.com
Медиа|Plex.Plex|Plex|Проигрыватель и личная медиатека|https://www.plex.tv
Медиа|Jellyfin.JellyfinMediaPlayer|Jellyfin Player|Клиент домашнего медиасервера|https://jellyfin.org
Разработка|Microsoft.VisualStudioCode|Visual Studio Code|Редактор кода с расширениями|https://code.visualstudio.com
Разработка|Microsoft.VisualStudio.2022.Community|Visual Studio Community|IDE для C++, C# и .NET|https://visualstudio.microsoft.com
Разработка|JetBrains.IntelliJIDEA.Community|IntelliJ IDEA Community|IDE для разработки на JVM|https://www.jetbrains.com/idea
Разработка|JetBrains.PyCharm.Community|PyCharm Community|Среда разработки на Python|https://www.jetbrains.com/pycharm
Разработка|JetBrains.WebStorm|WebStorm|IDE для JavaScript и TypeScript|https://www.jetbrains.com/webstorm
Разработка|JetBrains.Rider|Rider|IDE для .NET и игровых проектов|https://www.jetbrains.com/rider
Разработка|Notepad++.Notepad++|Notepad++|Быстрый текстовый редактор|https://notepad-plus-plus.org
Разработка|SublimeHQ.SublimeText.4|Sublime Text|Редактор текста и исходного кода|https://www.sublimetext.com
Разработка|Git.Git|Git|Система контроля версий|https://git-scm.com
Разработка|GitHub.GitHubDesktop|GitHub Desktop|Графический интерфейс Git|https://desktop.github.com
Разработка|OpenJS.NodeJS.LTS|Node.js LTS|Среда выполнения JavaScript|https://nodejs.org
Разработка|Python.Python.3.13|Python 3.13|Язык и среда выполнения Python|https://www.python.org
Разработка|Rustlang.Rustup|Rustup|Установка компилятора и средств Rust|https://rustup.rs
Разработка|GoLang.Go|Go|Компилятор и инструменты языка Go|https://go.dev
Разработка|Microsoft.DotNet.SDK.8|.NET SDK 8|Средства сборки приложений .NET|https://dotnet.microsoft.com
Разработка|Docker.DockerDesktop|Docker Desktop|Контейнеры и локальная разработка|https://www.docker.com
Разработка|Postman.Postman|Postman|Разработка и тестирование API|https://www.postman.com
Разработка|Insomnia.Insomnia|Insomnia|Клиент HTTP и GraphQL API|https://insomnia.rest
Разработка|DBBrowserForSQLite.DBBrowserForSQLite|DB Browser for SQLite|Просмотр и изменение баз SQLite|https://sqlitebrowser.org
Разработка|DBeaver.DBeaver.Community|DBeaver|Универсальный клиент баз данных|https://dbeaver.io
Разработка|Microsoft.WindowsTerminal|Windows Terminal|Терминал PowerShell, CMD и WSL|https://github.com/microsoft/terminal
Разработка|Microsoft.PowerShell|PowerShell|Современная командная оболочка|https://github.com/PowerShell/PowerShell
Разработка|WinSCP.WinSCP|WinSCP|Передача файлов SFTP и FTP|https://winscp.net
Разработка|PuTTY.PuTTY|PuTTY|Клиент SSH и последовательных портов|https://www.chiark.greenend.org.uk/~sgtatham/putty
Разработка|HeidiSQL.HeidiSQL|HeidiSQL|Клиент MySQL, MariaDB и SQL Server|https://www.heidisql.com
Дизайн|BlenderFoundation.Blender|Blender|3D-моделирование и анимация|https://www.blender.org
Дизайн|GIMP.GIMP.3|GIMP|Редактор растровых изображений|https://www.gimp.org
Дизайн|Inkscape.Inkscape|Inkscape|Редактор векторной графики|https://inkscape.org
Дизайн|KDE.Krita|Krita|Цифровая живопись и иллюстрация|https://krita.org
Дизайн|Figma.Figma|Figma|Интерфейсы и совместная работа|https://www.figma.com
Дизайн|dotPDN.PaintDotNet|Paint.NET|Редактирование изображений|https://www.getpaint.net
Дизайн|ShareX.ShareX|ShareX|Скриншоты и захват экрана|https://getsharex.com
Дизайн|Greenshot.Greenshot|Greenshot|Создание и разметка скриншотов|https://getgreenshot.org
Дизайн|NickeManarin.ScreenToGif|ScreenToGif|Запись экрана в GIF и видео|https://www.screentogif.com
Дизайн|Meltytech.Shotcut|Shotcut|Нелинейный видеоредактор|https://shotcut.org
Дизайн|KDE.Kdenlive|Kdenlive|Монтаж видео с открытым кодом|https://kdenlive.org
Офис|TheDocumentFoundation.LibreOffice|LibreOffice|Документы, таблицы и презентации|https://www.libreoffice.org
Офис|ONLYOFFICE.DesktopEditors|ONLYOFFICE|Офисный пакет для документов|https://www.onlyoffice.com
Офис|SumatraPDF.SumatraPDF|SumatraPDF|Лёгкий просмотр PDF и книг|https://www.sumatrapdfreader.org
Офис|Adobe.Acrobat.Reader.64-bit|Adobe Acrobat Reader|Просмотр и печать PDF|https://get.adobe.com/reader
Офис|Notion.Notion|Notion|Заметки и рабочие пространства|https://www.notion.so
Офис|Obsidian.Obsidian|Obsidian|Личная база знаний в Markdown|https://obsidian.md
Офис|Joplin.Joplin|Joplin|Заметки с синхронизацией|https://joplinapp.org
Офис|Appest.TickTick|TickTick|Списки задач и планирование|https://ticktick.com
Офис|Doist.Todoist|Todoist|Управление личными задачами|https://todoist.com
Офис|Anki.Anki|Anki|Обучение с помощью карточек|https://apps.ankiweb.net
Игры|Valve.Steam|Steam|Игровая библиотека и магазин|https://store.steampowered.com
Игры|EpicGames.EpicGamesLauncher|Epic Games Launcher|Игры и Unreal Engine|https://store.epicgames.com
Игры|GOG.Galaxy|GOG Galaxy|Библиотека игр GOG|https://www.gog.com/galaxy
Игры|ElectronicArts.EADesktop|EA app|Библиотека и игры Electronic Arts|https://www.ea.com/ea-app
Игры|Ubisoft.Connect|Ubisoft Connect|Библиотека игр Ubisoft|https://ubisoftconnect.com
Игры|PrismLauncher.PrismLauncher|Prism Launcher|Управление сборками Minecraft|https://prismlauncher.org
Игры|Playnite.Playnite|Playnite|Общая библиотека игровых лаунчеров|https://playnite.link
Игры|Parsec.Parsec|Parsec|Удалённый доступ для игр и работы|https://parsec.app
Игры|MoonlightGameStreamingProject.Moonlight|Moonlight|Клиент игрового стриминга|https://moonlight-stream.org
Игры|LizardByte.Sunshine|Sunshine|Сервер игрового стриминга|https://app.lizardbyte.dev/Sunshine
Утилиты|7zip.7zip|7-Zip|Архиватор 7z, ZIP и других форматов|https://www.7-zip.org
Утилиты|RARLab.WinRAR|WinRAR|Архиватор RAR и ZIP|https://www.win-rar.com
Утилиты|Microsoft.PowerToys|PowerToys|Расширенные инструменты Windows|https://github.com/microsoft/PowerToys
Утилиты|voidtools.Everything|Everything|Мгновенный поиск файлов по имени|https://www.voidtools.com
Утилиты|AntibodySoftware.WizTree|WizTree|Анализ занятого места на дисках|https://diskanalyzer.com
Утилиты|WinDirStat.WinDirStat|WinDirStat|Карта файлов и размеров на диске|https://windirstat.net
Утилиты|REALiX.HWiNFO|HWiNFO|Диагностика датчиков и оборудования|https://www.hwinfo.com
Утилиты|CPUID.CPU-Z|CPU-Z|Информация о процессоре и памяти|https://www.cpuid.com
Утилиты|TechPowerUp.GPU-Z|GPU-Z|Информация о видеокарте|https://www.techpowerup.com/gpuz
Утилиты|CrystalDewWorld.CrystalDiskInfo|CrystalDiskInfo|Состояние и SMART накопителей|https://crystalmark.info
Утилиты|CrystalDewWorld.CrystalDiskMark|CrystalDiskMark|Тест скорости дисков|https://crystalmark.info
Утилиты|AntibodySoftware.WizFile|WizFile|Быстрый поиск файлов|https://antibody-software.com
Утилиты|Rufus.Rufus|Rufus|Создание загрузочных USB-носителей|https://rufus.ie
Утилиты|Balena.Etcher|balenaEtcher|Запись образов на USB и карты|https://etcher.balena.io
Утилиты|RamenSoftware.Windhawk|Windhawk|Настройка интерфейса Windows|https://windhawk.net
Утилиты|AutoHotkey.AutoHotkey|AutoHotkey|Горячие клавиши и автоматизация|https://www.autohotkey.com
Утилиты|Microsoft.Sysinternals.ProcessExplorer|Process Explorer|Подробный просмотр процессов|https://learn.microsoft.com/sysinternals
Утилиты|Microsoft.Sysinternals.Autoruns|Autoruns|Анализ автозапуска Windows|https://learn.microsoft.com/sysinternals
Утилиты|Guru3D.Afterburner|MSI Afterburner|Мониторинг и настройка GPU|https://www.msi.com/Landing/afterburner
Сеть и облако|qBittorrent.qBittorrent|qBittorrent|Клиент загрузок BitTorrent|https://www.qbittorrent.org
Сеть и облако|Transmission.Transmission|Transmission|Лёгкий клиент BitTorrent|https://transmissionbt.com
Сеть и облако|Google.GoogleDrive|Google Drive|Синхронизация облачных файлов|https://www.google.com/drive
Сеть и облако|Dropbox.Dropbox|Dropbox|Облачное хранилище файлов|https://www.dropbox.com
Сеть и облако|Microsoft.OneDrive|OneDrive|Синхронизация файлов Microsoft|https://www.microsoft.com/onedrive
Сеть и облако|Nextcloud.NextcloudDesktop|Nextcloud|Клиент личного облака|https://nextcloud.com
Сеть и облако|Syncthing.Syncthing|Syncthing|Прямая синхронизация между ПК|https://syncthing.net
Сеть и облако|Tailscale.Tailscale|Tailscale|Частная сеть между устройствами|https://tailscale.com
Сеть и облако|WireGuard.WireGuard|WireGuard|Клиент VPN WireGuard|https://www.wireguard.com
Сеть и облако|Proton.ProtonVPN|Proton VPN|Приложение VPN|https://protonvpn.com
Сеть и облако|WiresharkFoundation.Wireshark|Wireshark|Анализ сетевых пакетов|https://www.wireshark.org
Безопасность|Bitwarden.Bitwarden|Bitwarden|Менеджер паролей|https://bitwarden.com
Безопасность|KeePassXCTeam.KeePassXC|KeePassXC|Локальная база паролей|https://keepassxc.org
Безопасность|DominikReichl.KeePass|KeePass|Менеджер паролей с плагинами|https://keepass.info
Безопасность|Cryptomator.Cryptomator|Cryptomator|Шифрование файлов для облака|https://cryptomator.org
Безопасность|IDRIX.VeraCrypt|VeraCrypt|Шифрование дисков и контейнеров|https://www.veracrypt.fr
Безопасность|Malwarebytes.Malwarebytes|Malwarebytes|Проверка на нежелательное ПО|https://www.malwarebytes.com
'''.strip().splitlines()
catalog = []
for line in rows:
    category, package_id, name, desc, site = line.split('|')
    catalog.append(dict(Id=package_id, Name=name, Category=category, Description=desc, Website=site))
fallbacks = {
 'Microsoft.VisualStudioCode': ('https://update.code.visualstudio.com/latest/win32-x64-user/stable', 'exe', ['/VERYSILENT','/NORESTART','/MERGETASKS=!runcode'], 'Microsoft Corporation'),
 'Telegram.TelegramDesktop': ('https://telegram.org/dl/desktop/win64', 'exe', ['/VERYSILENT','/NORESTART'], 'Telegram'),
 'Discord.Discord': ('https://discord.com/api/download?platform=win', 'exe', ['--silent'], 'Discord'),
 'Mozilla.Firefox': ('https://download.mozilla.org/?product=firefox-latest-ssl&os=win64&lang=ru', 'exe', ['-ms'], 'Mozilla Corporation'),
 'Google.Chrome': ('https://dl.google.com/dl/chrome/install/googlechromestandaloneenterprise64.msi', 'msi', [], 'Google'),
 'Microsoft.Teams': ('https://go.microsoft.com/fwlink/?linkid=2243204&clcid=0x409', 'exe', ['-p'], 'Microsoft Corporation'),
}
for p in catalog:
    if p['Id'] in fallbacks:
        p['Url'],p['Extension'],p['Args'],p['Signer'] = fallbacks[p['Id']]
(root/'Tweaker/catalog.json').write_text(json.dumps(catalog,ensure_ascii=False,indent=2),encoding='utf-8')
assets=root/'Tweaker/Assets';assets.mkdir(exist_ok=True)
img=Image.new('RGBA',(256,256),(16,16,16,255));d=ImageDraw.Draw(img)
d.rounded_rectangle((12,12,244,244),radius=52,fill='white')
d.line([(64,184),(64,73),(128,143),(192,73),(192,184)],fill=(16,16,16),width=19,joint='curve')
img.save(assets/'app.ico',sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
img.save(assets/'app.png')
print(f'{len(catalog)} packages, {len(set(p["Category"] for p in catalog))} categories')

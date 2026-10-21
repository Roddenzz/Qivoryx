using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

public static class Setup
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Qivoryx";
    static readonly string InstallRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Qivoryx");
    static readonly string MenuRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Qivoryx");
    static Window window;
    static TextBlock status;
    static Button submit;
#if !UNINSTALL
    static CheckBox desktop;
#endif
    static bool running;
    static bool completed;
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
#if !UNINSTALL
            if (args.Length == 2 && args[0] == "--verify") { Verify(args[1]);return; }
#endif
#if UNINSTALL
            if (!args.Contains("--remove"))
            {
                var temp = Path.Combine(Path.GetTempPath(), "Qivoryx-uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
                File.Copy(Assembly.GetExecutingAssembly().Location, temp);
                Process.Start(new ProcessStartInfo(temp, "--remove") { UseShellExecute = false, CreateNoWindow = true });
                return;
            }
#endif
            var app = new Application();window = new Window { Title = "Qivoryx Setup", Width = 590, Height = 510, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = new SolidColorBrush(Color.FromRgb(16,16,16)), Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI") };
            string[] themeKeys = {"Bg","Side","Card","Fg","Muted","Line","Hover","Accent","OnAccent","Input","ScrollThumb"};
            string[] themeColors = {"#111111","#141414","#1B1B1B","#F3F3F3","#959595","#303030","#282828","#F1F1F1","#171717","#171717","#454545"};
            for(int i=0;i<themeKeys.Length;i++) window.Resources[themeKeys[i]]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(themeColors[i]));
            using(var theme=Assembly.GetExecutingAssembly().GetManifestResourceStream("Controls")) window.Resources.MergedDictionaries.Add((ResourceDictionary)System.Windows.Markup.XamlReader.Load(theme));
            window.WindowStyle=WindowStyle.None;window.Height=550;
            System.Windows.Shell.WindowChrome.SetWindowChrome(window,new System.Windows.Shell.WindowChrome { CaptionHeight=40,ResizeBorderThickness=new Thickness(0),GlassFrameThickness=new Thickness(0),UseAeroCaptionButtons=false });
            var body = new StackPanel { Margin = new Thickness(38) };
            body.Children.Add(new TextBlock { Text = "Q  /  QIVORYX", FontWeight = FontWeights.Bold, FontSize = 15, Margin = new Thickness(0,0,0,32) });
#if UNINSTALL
            body.Children.Add(new TextBlock { Text = "Удаление приложения", FontSize = 28, FontWeight = FontWeights.SemiBold });
            status = new TextBlock { Text = "Будут удалены файлы Qivoryx и ярлыки. Журнал и резервные копии в %LOCALAPPDATA%\\Qivoryx сохранятся. Системные изменения автоматически не отменяются.", FontSize = 14, Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,20,0,20), Height = 120 };
#else
            body.Children.Add(new TextBlock { Text = "Windows под вашим контролем.", FontSize = 27, FontWeight = FontWeights.SemiBold });
            status = new TextBlock { Text = "Установщик добавит Qivoryx для текущего пользователя. Дополнительный .NET и права администратора для установки не требуются.", FontSize = 14, Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,20,0,18), Height = 80 };
#endif
            body.Children.Add(status);
            body.Children.Add(new TextBlock { Text = InstallRoot, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0,0,0,18) });
#if !UNINSTALL
            desktop = new CheckBox { Content = "Ярлык на рабочем столе", IsChecked = true, Foreground = Brushes.White, Margin = new Thickness(0,0,0,24) };body.Children.Add(desktop);
#endif
            submit = new Button { Content =
#if UNINSTALL
                "Удалить Qivoryx",
#else
                "Установить Qivoryx",
#endif
                Padding = new Thickness(20,12,20,12), Background = Brushes.White, Foreground = Brushes.Black, FontSize = 14, BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left };
            submit.Click += async (s,e) =>
            {
                if (completed) { window.Close();return; }
                if (running) return;running = true;submit.IsEnabled = false;
                try
                {
                    if (Process.GetProcessesByName("Qivoryx").Length > 0) throw new Exception("Закройте Qivoryx и повторите.");
#if UNINSTALL
                    status.Text = "Удаление файлов…";await Task.Run((Action)Uninstall);status.Text = "Qivoryx удалён. Журнал и резервные копии сохранены.";
#else
                    var makeDesktop = desktop.IsChecked == true;status.Text = "Распаковка и установка…";
                    await Task.Run((Action)Install);
                    Directory.CreateDirectory(MenuRoot);Shortcut(Path.Combine(MenuRoot,"Qivoryx.lnk"),Path.Combine(InstallRoot,"Qivoryx.exe"));
                    if(makeDesktop) Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"Qivoryx.lnk"),Path.Combine(InstallRoot,"Qivoryx.exe"));
                    status.Text = "Установка завершена. Приложение доступно в меню «Пуск».";
                    var launch = new Button { Content = "Открыть приложение", Padding = new Thickness(20,10,20,10), Margin = new Thickness(0,14,0,0), HorizontalAlignment = HorizontalAlignment.Left };
                    launch.Click += (a,b) => { Process.Start(Path.Combine(InstallRoot,"Qivoryx.exe"));window.Close(); };body.Children.Add(launch);
#endif
                    submit.Content = "Готово";
                    completed = true;submit.IsEnabled = true;
                }
                catch(Exception ex) { status.Text = ex.Message;submit.IsEnabled = true; }
                finally { running = false; }
            };
            body.Children.Add(submit);
            var shell=new Grid { Background=window.Background };shell.RowDefinitions.Add(new RowDefinition { Height=new GridLength(40) });shell.RowDefinitions.Add(new RowDefinition());
            var titlebar=new Grid();titlebar.Children.Add(new TextBlock { Text="QIVORYX / SETUP",FontSize=10,Foreground=Brushes.Gray,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(20,0,0,0) });
            var close=new Button { Content="×",Width=44,FontSize=20,Padding=new Thickness(0),Background=Brushes.Transparent,BorderThickness=new Thickness(0),HorizontalAlignment=HorizontalAlignment.Right };
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(close,true);close.Click+=(s,e)=>window.Close();titlebar.Children.Add(close);shell.Children.Add(titlebar);
            Grid.SetRow(body,1);shell.Children.Add(body);window.Content=new Border { Child=shell,BorderBrush=(Brush)window.Resources["Line"],BorderThickness=new Thickness(1) };
            if(args.Length==2 && args[0]=="--smoke")
            {
                window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-12000;window.Top=0;window.ShowInTaskbar=false;
                window.Loaded+=(s,e)=>window.Dispatcher.BeginInvoke(new Action(()=> {
                    window.UpdateLayout();var element=(FrameworkElement)window.Content;
                    var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)element.ActualWidth,(int)element.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(element);
                    var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var file=File.Create(args[1]))encoder.Save(file);window.Close();
                }),System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            window.Closing += (s,e) => { if(running)e.Cancel=true; };app.Run(window);
        }
        catch(Exception ex) { MessageBox.Show(ex.Message,"Qivoryx Setup");Environment.ExitCode=1; }
    }
    static void AssertPlainTree(string root)
    {
        if(!Directory.Exists(root))return;
        var pending=new System.Collections.Generic.Stack<string>();pending.Push(root);
        while(pending.Count>0)
        {
            var current=pending.Pop();
            if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new Exception("В папке установки найдена ссылка. Операция отменена.");
            foreach(var entry in new DirectoryInfo(current).EnumerateFileSystemInfos())
            {
                if((entry.Attributes&FileAttributes.ReparsePoint)!=0)throw new Exception("В папке установки найдена ссылка. Операция отменена.");
                if((entry.Attributes&FileAttributes.Directory)!=0)pending.Push(entry.FullName);
            }
        }
    }
#if !UNINSTALL
    static void Extract(string destination)
    {
        Directory.CreateDirectory(destination);
        using(var source=Assembly.GetExecutingAssembly().GetManifestResourceStream("Payload"))
        using(var zip=new ZipArchive(source,ZipArchiveMode.Read))
        {
            foreach(var entry in zip.Entries)
            {
                var path=Path.GetFullPath(Path.Combine(destination,entry.FullName));
                if(!path.StartsWith(Path.GetFullPath(destination)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new Exception("Небезопасный путь внутри архива.");
                if(string.IsNullOrEmpty(entry.Name)){Directory.CreateDirectory(path);continue;}
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using(var input=entry.Open())using(var output=File.Create(path))input.CopyTo(output);
            }
        }
    }
    static void Install()
    {
        AssertPlainTree(InstallRoot);
        if(Directory.Exists(InstallRoot) && Directory.EnumerateFileSystemEntries(InstallRoot).Any() && !File.Exists(Path.Combine(InstallRoot,".qivoryx-install"))) throw new Exception("Папка установки уже содержит посторонние файлы. Освободите её вручную.");
        var stage=InstallRoot+"-staging-"+Guid.NewGuid().ToString("N");
        try
        {
            Extract(stage);
            // Installed copies keep user data in AppData; portable copies use Data/.
            File.Delete(Path.Combine(stage,"portable.flag"));
            using(var source=Assembly.GetExecutingAssembly().GetManifestResourceStream("Uninstaller"))using(var output=File.Create(Path.Combine(stage,"Uninstall.exe")))source.CopyTo(output);
            File.WriteAllText(Path.Combine(stage,".qivoryx-install"),InstallRoot);
            Directory.CreateDirectory(InstallRoot);
            foreach(var file in Directory.GetFiles(stage,"*",SearchOption.AllDirectories))
            {
                var target=Path.Combine(InstallRoot,file.Substring(stage.Length+1));Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(file,target,true);
            }
            using(var key=Registry.CurrentUser.CreateSubKey(Key))
            {
                key.SetValue("DisplayName","Qivoryx — Windows Toolkit");key.SetValue("DisplayVersion","0.3.0");key.SetValue("Publisher","Qivoryx");
                key.SetValue("InstallLocation",InstallRoot);key.SetValue("DisplayIcon",Path.Combine(InstallRoot,"Qivoryx.exe"));key.SetValue("UninstallString","\""+Path.Combine(InstallRoot,"Uninstall.exe")+"\"");key.SetValue("NoModify",1);key.SetValue("NoRepair",1);
                key.SetValue("EstimatedSize",(int)(Directory.GetFiles(InstallRoot,"*",SearchOption.AllDirectories).Sum(f=>new FileInfo(f).Length)/1024),RegistryValueKind.DWord);
            }
        }
        finally { if(Directory.Exists(stage))Directory.Delete(stage,true); }
    }
    static void Verify(string output)
    {
        var target=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output)),"installer-payload-"+Guid.NewGuid().ToString("N"));Extract(target);
        foreach(var name in new[]{"Qivoryx.exe","Backend.ps1","catalog.json","README.md","Integrations/DefenderKiller/sha256.json"}) if(!File.Exists(Path.Combine(target,name)))throw new Exception("Payload missing: "+name);
        File.WriteAllText(output,"Verified payload: "+Directory.GetFiles(target,"*",SearchOption.AllDirectories).Length+" files. Extraction: "+target);
    }
    static void Shortcut(string path,string target)
    {
        var shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        var shortcut=shell.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{path});
        shortcut.GetType().InvokeMember("TargetPath",BindingFlags.SetProperty,null,shortcut,new object[]{target});
        shortcut.GetType().InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,shortcut,new object[]{Path.GetDirectoryName(target)});
        shortcut.GetType().InvokeMember("Save",BindingFlags.InvokeMethod,null,shortcut,null);
    }
#else
    static void Uninstall()
    {
        // A fixed per-user path, registry ownership and marker are all required.
        using(var key=Registry.CurrentUser.OpenSubKey(Key))
        {
            if(key==null || (string)key.GetValue("InstallLocation")!=InstallRoot)throw new Exception("Регистрация установки не найдена.");
        }
        var marker=Path.Combine(InstallRoot,".qivoryx-install");
        if(!File.Exists(marker)||File.ReadAllText(marker)!=InstallRoot)throw new Exception("Маркер установки отсутствует. Удаление отменено.");
        AssertPlainTree(InstallRoot);
        Directory.Delete(InstallRoot,true);
        foreach(var link in new[]{Path.Combine(MenuRoot,"Qivoryx.lnk"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"Qivoryx.lnk")})if(File.Exists(link))File.Delete(link);
        if(Directory.Exists(MenuRoot)&&!Directory.EnumerateFileSystemEntries(MenuRoot).Any())Directory.Delete(MenuRoot);
        Registry.CurrentUser.DeleteSubKeyTree(Key,false);
    }
#endif
}

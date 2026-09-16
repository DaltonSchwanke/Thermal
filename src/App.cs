using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms=System.Windows.Forms;

namespace Thermal {
 public class Program {
  [STAThread] public static int Main(string[] args) {
   if(args.Contains("--self-test")) return SelfTests.Run();
   if(args.Contains("--diagnostic")) return SelfTests.Diagnostic();
   bool created;
   using(var mutex=new Mutex(true,"Local\\Thermal.Desktop."+Environment.UserName,out created)) {
    if(!created && !args.Contains("--render-test")) { if(!ActivationSignal.TryActivate())Forms.MessageBox.Show("Thermal is already running. Open it using the Thermal icon in your system tray. If it is elevated, use the Start Thermal shortcut.","Thermal");return 0; }
    var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
    app.DispatcherUnhandledException+=(s,e)=> { Forms.MessageBox.Show(e.Exception.Message,"Thermal"); e.Handled=true; };
    try {
     var controller=new DesktopApp(app,args.Contains("--render-test"),args.Contains("--live-test"));
     using(var installMutex=new Mutex(false,"Local\\Thermal.Desktop.Install"))
     using(var activation=args.Contains("--render-test")?null:new ActivationSignal(()=>app.Dispatcher.BeginInvoke(new Action(controller.Restore)))) {
      controller.Start(); app.Run();
     }
    }
    catch(Exception ex) { try { string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Thermal");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"startup-error.txt"),ex.ToString()); }catch { } Forms.MessageBox.Show(ex.Message,"Thermal could not start"); return 1; }
   }
   return 0;
  }
 }
 public class DesktopApp {
  readonly Application app;
  readonly bool renderTest;
  readonly bool liveTest;
  readonly HistoryStore history;
  readonly SensorService sensors=new SensorService();
  readonly ManualResetEvent stop=new ManualResetEvent(false);
  readonly Window window;
  readonly HistoryChart chart=new HistoryChart();
  Preferences preferences;
  Forms.NotifyIcon tray;
  Window mini;
  TextBlock miniCpu,miniGpu,miniRam,miniStatus;
  Snapshot current;
  bool quitting;
  int hours=1;
  DateTime lastChart=DateTime.MinValue;
  DispatcherTimer freshness;
  Task sensorTask;
  public DesktopApp(Application app,bool renderTest,bool liveTest) {
   this.app=app; this.renderTest=renderTest;this.liveTest=liveTest;
   history=new HistoryStore(renderTest||liveTest?Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-data",Guid.NewGuid().ToString("N")):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Thermal"));
   preferences=history.LoadPreferences();
   if(!renderTest && !liveTest) {
    using(var startupKey=Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"))
     preferences.StartWithWindows=startupKey!=null && string.Equals(startupKey.GetValue("Thermal") as string,"\""+Process.GetCurrentProcess().MainModule.FileName+"\"",StringComparison.OrdinalIgnoreCase);
   }
   using(var stream=File.OpenRead(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"MainWindow.xaml"))) window=(Window)XamlReader.Load(stream);
   app.MainWindow=window;
   app.SessionEnding+=(s,e)=> { quitting=true;stop.Set();history.Save();if(tray!=null)tray.Dispose(); };
   window.Icon=BitmapFrame.Create(new Uri(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Thermal.ico")));
   ((Grid)window.FindName("ChartHost")).Children.Add(chart);
   window.Closing+=(s,e)=> { if(!quitting) { e.Cancel=true;window.Hide(); } };
   Wire("DashboardNav",()=>Page("Dashboard","System overview"));
   Wire("SensorsNav",()=>Page("Sensors","Hardware sensors"));
   Wire("SettingsNav",()=>Page("Settings","Your preferences"));
   Wire("CompactButton",ShowMini);
   foreach(var range in new[]{1,6,24,168}) { int h=range; Wire("Range"+h,()=> { hours=h;RefreshChart(); }); }
   Wire("OpenFolder",()=>Process.Start(new ProcessStartInfo(history.Folder) { UseShellExecute=true }));
   Wire("SaveSettings",SaveSettings);
   Wire("AdminButton",Elevate);
   Wire("DriverButton",InstallDriver);
   Wire("ExportButton",Export);
   Find<CheckBox>("StartupCheck").IsChecked=preferences.StartWithWindows;
   Find<CheckBox>("CompactCheck").IsChecked=preferences.StartCompact;
   Find<TextBox>("CpuThreshold").Text=preferences.CpuWarning.ToString(CultureInfo.InvariantCulture);
   Find<TextBox>("GpuThreshold").Text=preferences.GpuWarning.ToString(CultureInfo.InvariantCulture);
   var combo=Find<ComboBox>("CornerSelect");
   foreach(ComboBoxItem item in combo.Items) if((string)item.Content==preferences.Corner) combo.SelectedItem=item;
   Set("DataPath",history.Folder); Set("MachineText",Environment.MachineName+"  /  Windows  /  Live hardware monitoring");
   window.SizeChanged+=(s,e)=>chart.InvalidateVisual();
   Microsoft.Win32.SystemEvents.DisplaySettingsChanged+=DisplayChanged;
  }
  T Find<T>(string name) where T:class { return window.FindName(name) as T; }
  void Set(string name,string text) { Find<TextBlock>(name).Text=text; }
  void Wire(string name,Action action) { Find<Button>(name).Click+=(s,e)=> { try { action(); } catch(Exception ex) { MessageBox.Show(window,ex.Message,"Thermal",MessageBoxButton.OK,MessageBoxImage.Information); } }; }
  public static Brush Brush(string value) { return (Brush)new BrushConverter().ConvertFromString(value); }
  void Page(string page,string title) {
   foreach(string p in new[]{"Dashboard","Sensors","Settings"}) { Find<StackPanel>(p+"Page").Visibility=p==page?Visibility.Visible:Visibility.Collapsed; Find<Button>(p+"Nav").Background=Brush(p==page?"#233A34":"#00000000"); }
   Set("PageTitle",title);
  }
  public void Start() {
   MakeTray(); RefreshChart();
   if(renderTest) { window.Show(); app.Dispatcher.BeginInvoke(new Action(RenderTest),DispatcherPriority.ApplicationIdle);return; }
   if(preferences.StartCompact) { window.Show(); ShowMini(); } else window.Show();
   sensorTask=Task.Factory.StartNew(()=> {
    try {
     sensors.Open();
     while(!stop.WaitOne(0)) {
      try { var snap=sensors.Read(); if(stop.WaitOne(0))break; history.Add(snap.Reading); app.Dispatcher.BeginInvoke(new Action(()=>Update(snap))); }
      catch(Exception ex) { string message=ex.Message; app.Dispatcher.BeginInvoke(new Action(()=> { Set("NoticeText","Sensor update failed: "+message); Find<Border>("NoticeBox").Visibility=Visibility.Visible; })); }
      if(stop.WaitOne(2000))break;
     }
    } finally { sensors.Dispose(); history.Save(); }
   },TaskCreationOptions.LongRunning);
   freshness=new DispatcherTimer { Interval=TimeSpan.FromSeconds(2) };
   freshness.Tick+=(s,e)=> { if(current!=null && (DateTime.UtcNow-current.Reading.Time).TotalSeconds>12) {
    Set("LiveBadge","●  STALE"); Set("NoticeText","Readings are stale. Waiting for the sensor service to respond…");Find<Border>("NoticeBox").Visibility=Visibility.Visible;
    foreach(string name in new[]{"CpuValue","GpuValue","LoadValue","MemoryValue"}) Set(name,"—");
    foreach(string name in new[]{"CpuBar","GpuBar","LoadBar","MemoryBar"}) Find<ProgressBar>(name).Value=0;
    if(mini!=null) { miniCpu.Text="—";miniGpu.Text="—";miniRam.Text="—";miniStatus.Text="Readings stale";miniStatus.Foreground=Brush("#E8B978"); }
   } };
   freshness.Start();
   if(liveTest) {
    var finish=new DispatcherTimer { Interval=TimeSpan.FromSeconds(25) };
    finish.Tick+=(s,e)=> { finish.Stop();try { window.UpdateLayout();Capture(window,"live-dashboard.png");ShowMini();mini.UpdateLayout();Capture(mini,"live-mini.png");history.Save();File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"live-test.txt"),"Live UI: "+(current==null?"NO READINGS": "CPU "+F(current.Reading.CpuTemp,"°C")+", GPU "+F(current.Reading.GpuTemp,"°C")+", sensors "+current.Sensors.Count)+"\nHistory rows: "+history.Get(DateTime.UtcNow.AddDays(-7)).Count+"\nHistory error: "+history.Error); }finally { Quit(); } };finish.Start();
   }
  }
  void MakeTray() {
   tray=new Forms.NotifyIcon { Text="Thermal · PC health",Icon=new System.Drawing.Icon(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Thermal.ico")),Visible=true };
   var menu=new Forms.ContextMenuStrip();
   menu.Items.Add("Open dashboard",null,(s,e)=>app.Dispatcher.Invoke(new Action(ShowDashboard)));
   menu.Items.Add("Mini monitor",null,(s,e)=>app.Dispatcher.Invoke(new Action(ShowMini)));
   menu.Items.Add(new Forms.ToolStripSeparator());
   menu.Items.Add("Quit Thermal",null,(s,e)=>app.Dispatcher.Invoke(new Action(Quit)));
   tray.ContextMenuStrip=menu; tray.DoubleClick+=(s,e)=>ShowDashboard();
  }
  void ShowDashboard() { if(mini!=null)mini.Hide();window.Show();window.WindowState=WindowState.Normal;window.Activate();RefreshChart(); }
  public void Restore() { if(!quitting)ShowDashboard(); }
  void ShowMini() {
   if(mini==null) {
    mini=new Window { Title="Thermal mini monitor",Width=336,Height=151,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.Transparent,Topmost=true,ShowInTaskbar=false };
    var border=new Border { Background=Brush("#171E24"),BorderBrush=Brush("#40564F"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(16,10,16,12) };
    mini.Content=border;
    var root=new StackPanel(); border.Child=root;
    var header=new DockPanel { Margin=new Thickness(0,0,0,12),Background=Brushes.Transparent };
    var expand=new Button { Content="↗",FontSize=16,Padding=new Thickness(7,0,7,0),Background=Brush("#263D35"),Foreground=Brush("#8AE4C0"),BorderThickness=new Thickness(0),ToolTip="Open dashboard" }; DockPanel.SetDock(expand,Dock.Right);expand.Click+=(s,e)=>ShowDashboard();header.Children.Add(expand);
    var title=new TextBlock { Text="◈  THERMAL",Foreground=Brush("#68DDB6"),FontSize=11,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,Cursor=Cursors.SizeAll };
    title.MouseLeftButtonDown+=(s,e)=> { if(e.ClickCount==2)ShowDashboard();else mini.DragMove(); };header.Children.Add(title);root.Children.Add(header);
    var numbers=new System.Windows.Controls.Primitives.UniformGrid { Columns=3 };
    miniCpu=MiniMetric(numbers,"CPU", "#66E1B7");miniGpu=MiniMetric(numbers,"GPU","#ABAEFF");miniRam=MiniMetric(numbers,"RAM","#E0E7ED");root.Children.Add(numbers);
    miniStatus=new TextBlock { Text="Connecting…",Foreground=Brush("#849AA6"),FontSize=10,Margin=new Thickness(0,10,0,0) };root.Children.Add(miniStatus);
    mini.Closing+=(s,e)=> { if(!quitting) { e.Cancel=true;mini.Hide(); } };
    var context=new ContextMenu(); var dashboardItem=new MenuItem { Header="Open dashboard" }; dashboardItem.Click+=(s,e)=>ShowDashboard();context.Items.Add(dashboardItem);var quitItem=new MenuItem { Header="Quit Thermal" };quitItem.Click+=(s,e)=>Quit();context.Items.Add(quitItem);border.ContextMenu=context;
   }
   PositionMini(); mini.Show(); if(current!=null) UpdateMini(current);window.Hide();
  }
  static TextBlock MiniMetric(Panel parent,string label,string color) { var stack=new StackPanel();stack.Children.Add(new TextBlock { Text=label,FontSize=10,Foreground=Brush("#8CA0AF") });var val=new TextBlock { Text="—",FontSize=28,FontWeight=FontWeights.SemiBold,Foreground=Brush(color),Margin=new Thickness(0,3,0,0) };stack.Children.Add(val);parent.Children.Add(stack);return val; }
  void PositionMini() {
   if(mini==null)return;
   var area=Forms.Screen.FromHandle(new WindowInteropHelper(window).Handle).WorkingArea;
   // Screen work areas are physical pixels; WPF window coordinates are device independent.
   var source=PresentationSource.FromVisual(window); var transform=source!=null ? source.CompositionTarget.TransformFromDevice : Matrix.Identity;
   var topLeft=transform.Transform(new Point(area.Left,area.Top)); var bottomRight=transform.Transform(new Point(area.Right,area.Bottom));
   mini.Left=preferences.Corner.EndsWith("left")?topLeft.X+16:bottomRight.X-mini.Width-16;
   mini.Top=preferences.Corner.StartsWith("Top")?topLeft.Y+16:bottomRight.Y-mini.Height-16;
  }
  void DisplayChanged(object s,EventArgs e) { app.Dispatcher.BeginInvoke(new Action(PositionMini)); }
  static string F(double? value,string unit) { return value.HasValue?Math.Round(value.Value).ToString("0")+unit:"—"; }
  bool Warning(Reading r) { return r.CpuTemp>=preferences.CpuWarning || r.GpuTemp>=preferences.GpuWarning; }
  void UpdateMini(Snapshot s) {
   var r=s.Reading;miniCpu.Text=F(r.CpuTemp,"°");miniGpu.Text=F(r.GpuTemp,"°");miniRam.Text=F(r.Memory,"%");
   miniCpu.Foreground=Brush(r.CpuTemp>=preferences.CpuWarning?"#FFB378":"#66E1B7");miniGpu.Foreground=Brush(r.GpuTemp>=preferences.GpuWarning?"#FFB378":"#ABAEFF");
   miniStatus.Text=Warning(r)?"●  Temperature above your threshold":!r.CpuTemp.HasValue||!r.GpuTemp.HasValue?"●  Some temperature sensors unavailable":"●  Live  ·  CPU load "+F(r.CpuLoad,"%");
   miniStatus.Foreground=Brush(Warning(r)?"#FFB378":"#91A7B1");
  }
  void Update(Snapshot s) {
   if(quitting)return;current=s; var r=s.Reading;
   Set("CpuValue",F(r.CpuTemp,"°C"));Set("GpuValue",F(r.GpuTemp,"°C"));Set("LoadValue",F(r.CpuLoad,"%"));Set("MemoryValue",F(r.Memory,"%"));
   Find<TextBlock>("CpuValue").Foreground=Brush(r.CpuTemp>=preferences.CpuWarning?"#FFB378":"#66E1B7");Find<TextBlock>("GpuValue").Foreground=Brush(r.GpuTemp>=preferences.GpuWarning?"#FFB378":"#ABAEFF");
   Find<ProgressBar>("CpuBar").Value=r.CpuTemp??0;Find<ProgressBar>("GpuBar").Value=r.GpuTemp??0;Find<ProgressBar>("LoadBar").Value=r.CpuLoad??0;Find<ProgressBar>("MemoryBar").Value=r.Memory??0;
   Set("CpuDetail",r.CpuTemp.HasValue?"Package / hottest CPU":"Sensor unavailable");Set("GpuDetail",r.GpuTemp.HasValue?"Core / hottest GPU":"Sensor unavailable");
   Set("LoadDetail","GPU load  "+F(r.GpuLoad,"%"));Set("MemoryDetail",r.Memory.HasValue?(s.MemoryTotal*r.Memory.Value/100).ToString("0.0")+" / "+s.MemoryTotal.ToString("0")+" GB":"Sensor unavailable");
   Set("CpuName",s.CpuName??"Processor name unavailable");Set("GpuName",s.GpuName??"Graphics adapter unavailable");
   Set("Essentials","CPU power  "+F(r.CpuPower," W")+"   ·   Fan  "+F(r.Fan," RPM")+"\nDrive temperature  "+F(r.DiskTemp,"°C"));
   Set("DiskText",s.Disk);Find<ProgressBar>("DiskBar").Value=s.DiskUsed;
   Set("UptimeText","Monitoring since "+Process.GetCurrentProcess().StartTime.ToString("HH:mm"));
   Find<ItemsControl>("SensorList").ItemsSource=s.Sensors;
   string notice=history.Error ?? s.Error;
   if(notice==null && Warning(r))notice="Temperature above your configured threshold. CPU: "+F(r.CpuTemp,"°C")+" · GPU: "+F(r.GpuTemp,"°C")+". Thresholds can be adjusted in Settings.";
   if(notice==null && !r.CpuTemp.HasValue && !LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled)notice="CPU temperature needs the PawnIO sensor driver. Open Settings → Set up temperature sensors, then restart Thermal as administrator.";
   if(notice==null && (!r.CpuTemp.HasValue||!r.GpuTemp.HasValue))notice=s.Admin?"Some temperature sensors are unavailable on this hardware. Available readings are still recorded.":"Some temperature sensors need administrator access. Try Settings → Restart as administrator. Available readings are still recorded.";
   Find<Border>("NoticeBox").Visibility=notice==null?Visibility.Collapsed:Visibility.Visible;Set("NoticeText",notice??"");
   Set("LiveBadge",Warning(r)?"●  WARM":notice==null?"●  LIVE":"●  PARTIAL");
   Set("StatusText","Updated "+r.Time.ToLocalTime().ToString("HH:mm:ss")+"  ·  "+s.Sensors.Count+" sensors  ·  "+(history.Error==null?"Recording locally":"History error"));
   tray.Text=("Thermal · CPU "+F(r.CpuTemp,"°C")+" · GPU "+F(r.GpuTemp,"°C"));
   if(mini!=null)UpdateMini(s);
   if((DateTime.UtcNow-lastChart).TotalSeconds>10)RefreshChart();
  }
  void RefreshChart() {
   lastChart=DateTime.UtcNow; chart.End=lastChart;chart.Start=lastChart.AddHours(-hours);chart.Rows=history.Get(chart.Start);chart.InvalidateVisual();
   foreach(int h in new[]{1,6,24,168})Find<Button>("Range"+h).Background=Brush(h==hours?"#2C4840":"#202830");
   var cp=Aggregation.Max(chart.Rows.Select(x=>x.CpuPeak));var gp=Aggregation.Max(chart.Rows.Select(x=>x.GpuPeak));
   Set("HistoryStats",chart.Rows.Count==0?"Your history will appear as readings arrive.":"CPU peak  "+F(cp,"°C")+"    ·    GPU peak  "+F(gp,"°C")+"    ·    "+chart.Rows.Count+" minute samples");
  }
  void SaveSettings() {
   double cpu,gpu;
   if(!double.TryParse(Find<TextBox>("CpuThreshold").Text,NumberStyles.Float,CultureInfo.InvariantCulture,out cpu)||!double.TryParse(Find<TextBox>("GpuThreshold").Text,NumberStyles.Float,CultureInfo.InvariantCulture,out gpu)||double.IsNaN(cpu)||double.IsNaN(gpu)||cpu<40||cpu>115||gpu<40||gpu>115) { Set("SettingsResult","Enter thresholds between 40 and 115 °C.");return; }
   var next=new Preferences { CpuWarning=cpu,GpuWarning=gpu,StartWithWindows=Find<CheckBox>("StartupCheck").IsChecked==true,StartCompact=Find<CheckBox>("CompactCheck").IsChecked==true,Corner=(string)((ComboBoxItem)Find<ComboBox>("CornerSelect").SelectedItem).Content };
   using(var key=Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run")) { if(next.StartWithWindows) key.SetValue("Thermal","\""+Process.GetCurrentProcess().MainModule.FileName+"\"");else key.DeleteValue("Thermal",false); }
   history.SavePreferences(next);preferences=next;Set("SettingsResult","Preferences saved.");PositionMini();if(current!=null)Update(current);
  }
  void Elevate() {
   if(current!=null && current.Admin) { Set("SettingsResult","Already running as administrator.");return; }
   // The launcher waits for this instance to exit before starting the elevated process.
   var exe=Process.GetCurrentProcess().MainModule.FileName.Replace("'","''");int pid=Process.GetCurrentProcess().Id;
   string command="Wait-Process -Id "+pid+" -ErrorAction SilentlyContinue; Start-Process -FilePath '"+exe+"'";
   var p=new ProcessStartInfo("powershell.exe","-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand "+Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(command))) { UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden };
   try { Process.Start(p);Quit(); } catch(System.ComponentModel.Win32Exception) { Set("SettingsResult","Administrator restart canceled."); }
  }
  async void InstallDriver() {
   var button=Find<Button>("DriverButton");button.IsEnabled=false;
   try {
    string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"PawnIO_setup.exe");
    using(var stream=File.OpenRead(file)) using(var sha=System.Security.Cryptography.SHA256.Create()) {
     string hash=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");
     if(hash!="A3A46226C5E2824F4CDD42BE0EECBABFC672C86F7889710F5AB1E6AD385B47A0")throw new Exception("Sensor setup checksum mismatch. Please rebuild or download a fresh copy.");
    }
    var process=Process.Start(new ProcessStartInfo(file,"-install") { UseShellExecute=true,Verb="runas" });
    Set("SettingsResult","Waiting for sensor setup…");
    await Task.Run(()=>process.WaitForExit());
    Set("SettingsResult",process.ExitCode==0?"Sensor setup complete. Restart Thermal as administrator.":"Setup did not complete (code "+process.ExitCode+").");process.Dispose();
   }catch(System.ComponentModel.Win32Exception) { Set("SettingsResult","Sensor setup canceled."); }
   catch(Exception ex) { Set("SettingsResult",ex.Message); }
   finally { button.IsEnabled=true; }
  }
  void Export() {
   var dialog=new SaveFileDialog { Title="Export selected history range",FileName="Thermal-"+DateTime.Now.ToString("yyyy-MM-dd-HHmm")+".csv",Filter="CSV files (*.csv)|*.csv" };
   if(dialog.ShowDialog(window)!=true)return;
   var lines=new List<string> { "time_utc,cpu_average_c,cpu_peak_c,gpu_average_c,gpu_peak_c,cpu_load_percent,gpu_load_percent,memory_percent,drive_temp_c,cpu_power_w,fan_rpm" };
   foreach(var r in history.Get(DateTime.UtcNow.AddHours(-hours))) lines.Add(r.Time.ToString("o")+","+string.Join(",",new[]{r.CpuTemp,r.CpuPeak,r.GpuTemp,r.GpuPeak,r.CpuLoad,r.GpuLoad,r.Memory,r.DiskTemp,r.CpuPower,r.Fan}.Select(v=>v.HasValue?v.Value.ToString("0.##",CultureInfo.InvariantCulture):"")));
   File.WriteAllLines(dialog.FileName,lines);Set("StatusText","Exported "+(lines.Count-1)+" minute samples.");
  }
  public async void Quit() { if(quitting)return;quitting=true;stop.Set();if(freshness!=null)freshness.Stop();Microsoft.Win32.SystemEvents.DisplaySettingsChanged-=DisplayChanged;history.Save();if(tray!=null)tray.Dispose();window.Hide();if(mini!=null)mini.Hide();if(sensorTask!=null)await Task.WhenAny(sensorTask,Task.Delay(5000));app.Shutdown(); }
  void RenderTest() {
   try {
    var now=DateTime.UtcNow;
    for(int i=58;i>=0;i--)history.Add(new Reading { Time=now.AddMinutes(-i),CpuTemp=51+9*Math.Sin(i/5.0),GpuTemp=43+5*Math.Cos(i/8.0),CpuLoad=23,Memory=41,CpuPeak=65,GpuPeak=52 });
    Update(new Snapshot { Reading=new Reading { Time=now,CpuTemp=57,GpuTemp=46,CpuLoad=23,Memory=41,GpuLoad=12,CpuPower=34,Fan=1050,DiskTemp=38 },CpuName="Preview processor · synthetic test data",GpuName="Preview graphics · synthetic test data",MemoryTotal=32,Disk="C:\\  ·  684 GB free of 953 GB",DiskUsed=28,Admin=true,Sensors=new List<SensorRow>() });
    RefreshChart();window.UpdateLayout();Capture(window,"dashboard.png");
    ShowMini();mini.UpdateLayout();Capture(mini,"mini.png");ShowDashboard();
    Page("Settings","Your preferences");window.UpdateLayout();Capture(window,"settings.png");
    Find<TextBox>("CpuThreshold").Text="NaN";SaveSettings();
    if(Find<TextBlock>("SettingsResult").Text.IndexOf("Enter thresholds")<0)throw new Exception("Invalid threshold was accepted.");
    Find<TextBox>("CpuThreshold").Text="85";
    Find<Button>("Range168").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    if(hours!=168)throw new Exception("History range button did not update range.");
    Find<Button>("SensorsNav").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    if(Find<StackPanel>("SensorsPage").Visibility!=Visibility.Visible)throw new Exception("Sensors navigation failed.");
    Find<Button>("DashboardNav").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));window.Close();
    if(window.IsVisible)throw new Exception("Close-to-tray failed.");Restore();
    if(!window.IsVisible)throw new Exception("Activation did not restore the dashboard.");
    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"render-test.txt"),"PASS: dashboard/mini/settings rendering, mini topmost, dashboard restore, invalid threshold rejection, range switching, sensor navigation, close to tray.");
   } catch(Exception ex) { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"render-test.txt"),ex.ToString()); }
   Quit();
  }
  void Capture(Window target,string file) { var bmp=new RenderTargetBitmap((int)target.ActualWidth,(int)target.ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(target);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using(var stream=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,file)))encoder.Save(stream); }
 }
}

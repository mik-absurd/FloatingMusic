using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using Windows.Media.Control;
using Windows.Storage.Streams;

static class Entry {
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string cls,string title);
 [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
 [STAThread] static void Main(string[] args) {
  try {
   if(args.Length==1&&args[0]=="--test-lyrics"){Console.WriteLine(Player.TestLyrics());Console.WriteLine(Player.TestLyricChoices());return;}
   if(args.Length==2&&args[0]=="--probe-lyrics"){Console.WriteLine(Player.ProbeLyrics(args[1]).GetAwaiter().GetResult());return;}
   if(args.Length==1&&args[0]=="--inspect-library") {Environment.ExitCode=Task.Run(()=>LibraryBridge.Inspect()).GetAwaiter().GetResult();return;}
   if(args.Length==2&&args[0]=="--library-control") {Environment.ExitCode=Task.Run(()=>LibraryBridge.RunWorker(args[1])).GetAwaiter().GetResult();return;}
   if(args.Length==2&&args[0]=="--seek-control") {double seconds;if(!double.TryParse(args[1],NumberStyles.Float,CultureInfo.InvariantCulture,out seconds)){Environment.ExitCode=14;return;}Environment.ExitCode=Task.Run(()=>AppleSeek.RunWorker(seconds)).GetAwaiter().GetResult();return;}
   if(args.Contains("--audio-sessions")) {var info=new System.Collections.Generic.List<string>();AppleVolume.Access(null,null,line=>info.Add(line));File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"audio-sessions.txt"),info);return;}
   if(args.Contains("--read-state")) {ReadState().GetAwaiter().GetResult();return;}
   if(args.Contains("--volume-test")) {File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"volume-test.txt"),AppleVolume.Test());return;}
   if(args.Contains("--diagnose")) { Diagnose().GetAwaiter().GetResult(); return; }
   bool first; using(var mutex=new Mutex(true,"Local\\FloatingMusic_3A6A2C46",out first)) {
    if(!first) { SetForegroundWindow(FindWindow(null,"浮光 · Apple Music")); return; }
    var app=new Application(); var player=new Player(); app.Run(player.Window);
   }
  } catch(Exception ex) { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"error.log"),ex.ToString()); MessageBox.Show("浮光启动失败："+ex.Message+"\n详细信息已保存到 error.log。","浮光"); }
 }
 static async Task ReadState() {
  var m=await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
  var s=m.GetSessions().FirstOrDefault(Player.IsApple);var v=await Task.Run(()=>AppleVolume.Access());
  var lines=new System.Collections.Generic.List<string>{"UTC="+DateTime.UtcNow.ToString("o"),"Volume="+v.Level.ToString(CultureInfo.InvariantCulture),"Muted="+v.Muted,"AudioSessions="+v.Count,"ActiveAudioSessions="+v.ActiveCount,"SelectedAudioPid="+v.ProcessId};
  if(s!=null){var p=await s.TryGetMediaPropertiesAsync();var t=s.GetTimelineProperties();lines.Add("Title="+p.Title);lines.Add("Position="+t.Position.TotalSeconds.ToString(CultureInfo.InvariantCulture));lines.Add("Duration="+t.EndTime.TotalSeconds.ToString(CultureInfo.InvariantCulture));}
  File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"state-readback.txt"),lines);
 }
 static async Task Diagnose() {
  var lines=new System.Collections.Generic.List<string>();
  try {
   var manager=await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
   var sessions=manager.GetSessions().Where(Player.IsApple).ToList();
   lines.Add("Apple Music sessions: "+sessions.Count);
   foreach(var session in sessions) {
    var info=await session.TryGetMediaPropertiesAsync(); var p=session.GetPlaybackInfo(); var t=session.GetTimelineProperties();
    lines.Add("Source: "+session.SourceAppUserModelId); lines.Add("Title: "+info.Title); lines.Add("Artist: "+info.Artist);
    lines.Add("State: "+p.PlaybackStatus+"; previous="+p.Controls.IsPreviousEnabled+"; next="+p.Controls.IsNextEnabled+"; toggle="+p.Controls.IsPlayPauseToggleEnabled+"; seek="+p.Controls.IsPlaybackPositionEnabled);
    lines.Add("Timeline: "+t.Position+" / "+t.EndTime); lines.Add("Artwork: "+(info.Thumbnail!=null));
    lines.Add(await Player.DiagnoseLyrics(info.Title,info.Artist,t.EndTime.TotalSeconds-t.StartTime.TotalSeconds));
   }
   lines.Add("Time formatting: "+(Player.TimeLabel(TimeSpan.FromSeconds(125))=="2:05"?"PASS":"FAIL"));
   lines.Add("Background bounds: "+(Player.ClampOpacity(-3)==10 && Player.ClampOpacity(110)==100?"PASS":"FAIL"));
   lines.Add(Player.TestLyrics());
  } catch(Exception e) { lines.Add("ERROR: "+e); Environment.ExitCode=1; }
  File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"diagnostics.txt"),lines);
 }
}

sealed partial class Player {
 public Window Window;
 GlobalSystemMediaTransportControlsSessionManager manager;
 GlobalSystemMediaTransportControlsSession session;
 DispatcherTimer timer;
 Border shell,art;
 TextBlock song,artist,album,status,connection,elapsed,duration,opacityValue;
 Button play,previous,next,pin;
 Slider progress,opacity;
 StackPanel settingsPanel;
 bool refreshing,commandBusy,seeking,closed,updatingProgress,settingsOpen;
 string mediaKey="";
 DateTime messageUntil=DateTime.MinValue;
 string settingsPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preferences.ini");
 double bgOpacity=90;
 public static bool IsApple(GlobalSystemMediaTransportControlsSession s) { return s.SourceAppUserModelId.StartsWith("AppleInc.AppleMusicWin",StringComparison.OrdinalIgnoreCase); }
 public static double ClampOpacity(double value) { return double.IsNaN(value)||double.IsInfinity(value)?90:Math.Max(10,Math.Min(100,value)); }
 public static string TimeLabel(TimeSpan t) { return t.TotalHours>=1?t.ToString(@"h\:mm\:ss"):((int)Math.Max(0,t.TotalMinutes)).ToString()+":"+Math.Max(0,t.Seconds).ToString("00"); }
 T Find<T>(string name) where T:class { return Window.FindName(name) as T; }
 public Player() {
  using(var stream=typeof(Player).Assembly.GetManifestResourceStream("Player.xaml")) Window=(Window)XamlReader.Load(stream);
  shell=Find<Border>("Shell"); art=Find<Border>("Artwork"); song=Find<TextBlock>("Song"); artist=Find<TextBlock>("Artist"); album=Find<TextBlock>("Album"); status=Find<TextBlock>("Status"); connection=Find<TextBlock>("Connection"); elapsed=Find<TextBlock>("Elapsed"); duration=Find<TextBlock>("Duration"); opacityValue=Find<TextBlock>("OpacityValue");
  play=Find<Button>("Play"); previous=Find<Button>("Previous"); next=Find<Button>("Next"); pin=Find<Button>("PinButton"); progress=Find<Slider>("Progress"); opacity=Find<Slider>("OpacitySlider"); settingsPanel=Find<StackPanel>("SettingsPanel");
  InitLyrics(); InitVolume(); InitLibrary(); LoadSettings(); ApplyLyricsSettings(); opacity.Value=bgOpacity; ApplyOpacity(); UpdatePin();
  Find<Grid>("DragBar").MouseLeftButtonDown+=(s,e)=> { if(e.OriginalSource is TextBlock || e.OriginalSource is Grid || e.OriginalSource is System.Windows.Shapes.Ellipse) { Window.DragMove(); SaveSettings(); } };
  Find<Button>("CloseButton").Click+=(s,e)=>Window.Close();
  pin.Click+=(s,e)=>{ Window.Topmost=!Window.Topmost; UpdatePin(); SaveSettings(); };
  Find<Button>("SettingsButton").Click+=(s,e)=>{ settingsOpen=!settingsOpen; settingsPanel.Visibility=settingsOpen?Visibility.Visible:Visibility.Collapsed; ResizePanels(); };
  opacity.ValueChanged+=(s,e)=>{ bgOpacity=ClampOpacity(opacity.Value); ApplyOpacity(); };
  opacity.AddHandler(Thumb.DragCompletedEvent,new DragCompletedEventHandler((s,e)=>SaveSettings()));
  opacity.PreviewMouseLeftButtonUp+=(s,e)=>SaveSettings();
  play.Click+=async(s,e)=>await Command("toggle"); previous.Click+=async(s,e)=>await Command("previous"); next.Click+=async(s,e)=>await Command("next");
  new PointerSlider(progress,()=>seeking=true,async value=>{seeking=false;await Seek(value);});
  progress.ValueChanged+=(s,e)=> {if(!updatingProgress && seeking)elapsed.Text=TimeLabel(TimeSpan.FromSeconds(progress.Value-progress.Minimum));};
  Window.PreviewKeyDown+=async(s,e)=> { if(e.Key==Key.Space && !(Keyboard.FocusedElement is Slider) && !(Keyboard.FocusedElement is Button)) { e.Handled=true; await Command("toggle"); } if(e.Key==Key.Escape && settingsOpen) { settingsOpen=false; settingsPanel.Visibility=Visibility.Collapsed; ResizePanels(); } };
  Window.Closed+=(s,e)=> { closed=true; if(timer!=null)timer.Stop(); StopLyrics(); SaveSettings(); };
  Window.Loaded+=async(s,e)=> { KeepVisible(); await Refresh(); timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)}; timer.Tick+=async(a,b)=>await Refresh(); timer.Start(); };
 }
 void ApplyOpacity() { shell.Background=new SolidColorBrush(Color.FromArgb((byte)Math.Round(bgOpacity*2.55),22,23,33)); opacityValue.Text=Math.Round(bgOpacity)+"%"; }
 void UpdatePin() { pin.Foreground=new SolidColorBrush(Window.Topmost?Color.FromRgb(255,115,148):Color.FromRgb(150,149,168)); pin.ToolTip=Window.Topmost?"置顶：已开启":"置顶：已关闭"; }
 void Tell(string message) { status.Text=message; status.ToolTip=message; messageUntil=DateTime.UtcNow.AddSeconds(5); }
 void KeepVisible() {
  var area=System.Windows.Forms.Screen.AllScreens.Select(s=>s.WorkingArea).ToArray();
  var source=PresentationSource.FromVisual(Window); var transform=source==null?Matrix.Identity:source.CompositionTarget.TransformFromDevice;
  var rects=area.Select(r=>new Rect(transform.Transform(new Point(r.Left,r.Top)),transform.Transform(new Point(r.Right,r.Bottom)))).ToArray();
  var current=new Rect(Window.Left,Window.Top,Window.Width,Window.Height); var best=rects.FirstOrDefault(r=>r.IntersectsWith(current));
  if(best.IsEmpty || best.Width==0) best=SystemParameters.WorkArea;
  Window.Left=Math.Max(best.Left,Math.Min(Window.Left,best.Right-Window.Width)); Window.Top=Math.Max(best.Top,Math.Min(Window.Top,best.Bottom-Window.Height));
 }
 void LoadSettings() {
  Window.Left=SystemParameters.WorkArea.Right-Window.Width-32; Window.Top=SystemParameters.WorkArea.Top+48;
  try { if(!File.Exists(settingsPath))return; foreach(var line in File.ReadAllLines(settingsPath)) { var bits=line.Split('='); if(bits.Length!=2)continue; double v; if(bits[0]=="simplifiedLyrics") { simplifiedLyrics=bits[1]!="false"; continue; } if(bits[0]=="autoLyrics") { autoLyrics=bits[1]=="true"; continue; } if(bits[0]=="lyricsOpen") { lyricsOpen=bits[1]!="false"; continue; } if(bits[0]=="topmost") { Window.Topmost=bits[1]!="false"; continue; } if(!double.TryParse(bits[1],NumberStyles.Float,CultureInfo.InvariantCulture,out v)||double.IsNaN(v)||double.IsInfinity(v))continue; if(bits[0]=="opacity")bgOpacity=ClampOpacity(v); if(bits[0]=="left")Window.Left=v; if(bits[0]=="top")Window.Top=v; } }catch { }
 }
 void SaveSettings() { try { File.WriteAllLines(settingsPath,new[]{"opacity="+bgOpacity.ToString(CultureInfo.InvariantCulture),"left="+Window.Left.ToString(CultureInfo.InvariantCulture),"top="+Window.Top.ToString(CultureInfo.InvariantCulture),"topmost="+(Window.Topmost?"true":"false"),"autoLyrics="+(autoLyrics?"true":"false"),"simplifiedLyrics="+(simplifiedLyrics?"true":"false"),"lyricsOpen="+(lyricsOpen?"true":"false")}); }catch { Tell("设置未能保存"); } }
 static void ObserveFailure(Task task) { task.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted); }
 async Task<T> Timed<T>(Task<T> task) { if(await Task.WhenAny(task,Task.Delay(5000))!=task) { ObserveFailure(task); throw new TimeoutException("Apple Music 响应超时"); } return await task; }
 async Task Refresh() {
  if(refreshing||closed)return; refreshing=true;
  try {
   if(manager==null)manager=await Timed(GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask());
   if(closed)return;
   var candidates=manager.GetSessions().Where(IsApple).ToList();
   var active=candidates.FirstOrDefault(s=>s.GetPlaybackInfo().PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)??candidates.FirstOrDefault();
   if(active==null) { session=null; mediaKey=""; ResetLyrics(); SetIdle(); return; }
   session=active;
   var media=await Timed(active.TryGetMediaPropertiesAsync().AsTask()); if(closed)return;
   var key=media.Title+"\n"+media.Artist+"\n"+media.AlbumTitle;
   song.Text=string.IsNullOrWhiteSpace(media.Title)?"Apple Music":media.Title; song.ToolTip=song.Text;
   artist.Text=media.Artist; artist.ToolTip=media.Artist; album.Text=media.AlbumTitle; album.ToolTip=media.AlbumTitle;
   if(key!=mediaKey) { mediaKey=key; art.Background=null; await LoadArt(media.Thumbnail); }
   var info=active.GetPlaybackInfo(); var c=info.Controls;
   bool playing=info.PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
   play.Content=playing?"\uE769":"\uE768"; play.ToolTip=playing?"暂停（空格）":"播放（空格）";
   play.IsEnabled=!commandBusy && (c.IsPlayPauseToggleEnabled || (playing?c.IsPauseEnabled:c.IsPlayEnabled)); previous.IsEnabled=!commandBusy; next.IsEnabled=!commandBusy;
   connection.Text="  /  已连接 Apple Music";
   if(DateTime.UtcNow>messageUntil) { status.Text=playing?"正在播放":"已暂停"; status.ToolTip=status.Text; }
   var timeline=active.GetTimelineProperties();
   double start=timeline.StartTime.TotalSeconds,end=timeline.EndTime.TotalSeconds,pos=timeline.Position.TotalSeconds;
   if(playing) { var delta=(DateTimeOffset.UtcNow-timeline.LastUpdatedTime).TotalSeconds; if(delta>=0 && delta<86400)pos+=delta*(info.PlaybackRate??1); }
   pos=Math.Max(start,Math.Min(pos,end));
   TrackLyrics(key,media.Title,media.Artist,end-start,pos-start,playing,info.PlaybackRate??1);
   if(!seeking && !commandBusy) { updatingProgress=true; progress.Minimum=start; progress.Maximum=Math.Max(start+1,end); progress.Value=pos; updatingProgress=false; elapsed.Text=end>start?TimeLabel(TimeSpan.FromSeconds(pos-start)):"--:--"; }
   duration.Text=end>start?TimeLabel(TimeSpan.FromSeconds(end-start)):"--:--"; progress.IsEnabled=!commandBusy && end>start;
   progress.ToolTip="通过 Apple Music 进度控件调整；无需官方窗口置顶"; ReadVolume();ReadAccount();
  } catch(Exception ex) { session=null; manager=null; ResetLyrics(); SetIdle(); Tell(ex is UnauthorizedAccessException?"无法访问媒体会话":ex is TimeoutException?"连接超时，正在重试":"连接中断，正在重试"); }
  finally { refreshing=false; }
 }
 async Task LoadArt(IRandomAccessStreamReference thumbnail) {
  if(thumbnail==null)return;
  try { using(var input=await Timed(thumbnail.OpenReadAsync().AsTask())) using(var stream=input.AsStreamForRead()) { var image=new BitmapImage(); image.BeginInit(); image.CacheOption=BitmapCacheOption.OnLoad; image.DecodePixelWidth=180; image.StreamSource=stream; image.EndInit(); image.Freeze(); art.Background=new ImageBrush(image){Stretch=Stretch.UniformToFill}; } }catch { }
 }
 void SetIdle() { favoriteButton.IsEnabled=dislikeButton.IsEnabled=false; knownFavorite=null; accountTrack=""; favoriteButton.Content="喜爱 / 取消"; play.IsEnabled=previous.IsEnabled=next.IsEnabled=progress.IsEnabled=false; play.Content="\uE768"; art.Background=null; song.Text="让音乐浮在身边"; artist.Text="打开 Apple Music，播放一首歌曲"; album.Text="连接后自动显示歌曲与封面"; song.ToolTip=null; artist.ToolTip=null; album.ToolTip=null; connection.Text="  /  等待 Apple Music"; elapsed.Text=duration.Text="--:--"; updatingProgress=true; progress.Value=0; updatingProgress=false; if(DateTime.UtcNow>messageUntil)status.Text="等待连接"; }
 async Task Command(string command) {
  if(commandBusy||session==null)return; commandBusy=true; play.IsEnabled=previous.IsEnabled=next.IsEnabled=false;
  try { var target=session; bool ok;
   if(command=="previous")ok=await Timed(target.TrySkipPreviousAsync().AsTask());
   else if(command=="next")ok=await Timed(target.TrySkipNextAsync().AsTask());
   else { var i=target.GetPlaybackInfo(); if(i.Controls.IsPlayPauseToggleEnabled)ok=await Timed(target.TryTogglePlayPauseAsync().AsTask()); else if(i.PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)ok=await Timed(target.TryPauseAsync().AsTask()); else ok=await Timed(target.TryPlayAsync().AsTask()); }
   if(!ok)Tell("Apple Music 未执行操作");
  }catch { Tell("操作失败，请重试"); }finally {commandBusy=false;} await Refresh();
 }
 async Task Seek(double requested) {
  if(commandBusy||session==null||!progress.IsEnabled)return;commandBusy=true;progress.IsEnabled=false;
  var target=session;var originalTrack=mediaKey;var startTime=DateTime.UtcNow;
  try {
   string detail=await AppleSeek.SetAsync(requested);
   startTime=DateTime.UtcNow;bool confirmed=false;
   for(int sample=0;sample<4;sample++){
    await Task.Delay(700);if(closed||originalTrack!=mediaKey)return;
    var t=target.GetTimelineProperties();var p=target.GetPlaybackInfo();bool playing=p.PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
    double rate=p.PlaybackRate??1;double age=Math.Max(0,Math.Min(2,(DateTimeOffset.UtcNow-t.LastUpdatedTime).TotalSeconds));
    double actual=t.Position.TotalSeconds+(playing?age*rate:0);double expected=Math.Min(t.EndTime.TotalSeconds,requested+(playing?(DateTime.UtcNow-startTime).TotalSeconds*rate:0));
    confirmed=Math.Abs(actual-expected)<=3;
    if(confirmed&&sample>=1)break;
   }
   File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"seek-status.txt"),DateTime.UtcNow.ToString("o")+"\n"+detail+"\npositionConfirmed="+confirmed);
   if(!confirmed)Tell("未检测到实际跳转，请重试");else Tell("进度已调整");
  }catch(Exception ex){Tell(ex is InvalidOperationException||ex is TimeoutException?ex.Message:"进度请求失败");}finally{commandBusy=false;}
  await Refresh();
 }
}



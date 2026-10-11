using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

sealed partial class Player {
 Window lyricChoiceWindow;
 readonly Dictionary<string,List<Dictionary<string,object>>> lyricCandidates=new Dictionary<string,List<Dictionary<string,object>>>();
 static string RecordText(Dictionary<string,object> r){return HasSync(r)?LyricValue(r,"syncedLyrics"):(LyricValue(r,"plainLyrics")??"");}
 static string RecordId(Dictionary<string,object> r){object id;return r.TryGetValue("id",out id)?Convert.ToString(id,CultureInfo.InvariantCulture):"";}
 static string TextSignature(Dictionary<string,object> r){return RecordText(r).Replace("\r","").Trim();}
 static string BaseLyricTitle(string title){return Normalized(Regex.Replace(title??"",@"[（(\[].*?[）)\]]",""));}
 static IEnumerable<string> LyricQueries(string title,string artist){
  return new[]{"track_name="+Uri.EscapeDataString(title)+"&artist_name="+Uri.EscapeDataString(artist),"track_name="+Uri.EscapeDataString(ChineseVariant(title,true))+"&artist_name="+Uri.EscapeDataString(ChineseVariant(artist,true)),"track_name="+Uri.EscapeDataString(ChineseVariant(title,false))+"&artist_name="+Uri.EscapeDataString(ChineseVariant(artist,false)),"track_name="+Uri.EscapeDataString(title),"track_name="+Uri.EscapeDataString(ChineseVariant(title,true))}.Distinct();
 }
 static async Task<List<Dictionary<string,object>>> FindLyricCandidates(string title,string artist,double duration,CancellationToken token){
  var found=new Dictionary<string,Dictionary<string,object>>();var serializer=new JavaScriptSerializer();Exception failure=null;
  foreach(var query in LyricQueries(title,artist)){
   try{using(var response=await RequestLyrics("https://lrclib.net/api/search?"+query,token)){
    response.EnsureSuccessStatusCode();var records=serializer.Deserialize<List<Dictionary<string,object>>>(await response.Content.ReadAsStringAsync());
    foreach(var r in records){if(BaseLyricTitle(LyricValue(r,"trackName"))==BaseLyricTitle(title)&&!string.IsNullOrWhiteSpace(RecordText(r)))found[RecordId(r)]=r;}
   }}catch(OperationCanceledException){if(token.IsCancellationRequested)throw;failure=new TimeoutException("歌词服务响应超时，请稍后重试");}catch(HttpRequestException ex){failure=ex;}
   // Stop requesting after server failure; keep already retrieved candidates.
   if(failure!=null)break;
  }
  if(found.Count==0&&failure!=null)throw failure;
  return found.Values.ToList();
 }
 static Dictionary<string,object> SelectAutomatic(IEnumerable<Dictionary<string,object>> records,string title,string artist,double duration,string album){
  var eligible=records.Where(r=>AutoLyricMatch(r,title,artist,duration,album)).ToList();
  if(eligible.Any(HasSync))eligible=eligible.Where(HasSync).ToList();
  if(eligible.Any(r=>SameAlbum(r,album)))eligible=eligible.Where(r=>SameAlbum(r,album)).ToList();
  if(eligible.Select(TextSignature).Distinct().Count()!=1)return null;
  return eligible.OrderBy(r=>Math.Abs(LyricDuration(r)-duration)).FirstOrDefault();
 }
 static IEnumerable<Dictionary<string,object>> RankCandidates(IEnumerable<Dictionary<string,object>> records,string title,string artist,double duration,string album){
  return records.OrderByDescending(r=>AutoLyricMatch(r,title,artist,duration,album)).ThenByDescending(r=>SameArtist(LyricValue(r,"artistName"),artist)).ThenByDescending(r=>Normalized(LyricValue(r,"trackName"))==Normalized(title)).ThenByDescending(HasSync).ThenByDescending(r=>SameAlbum(r,album)).ThenBy(r=>Math.Abs(LyricDuration(r)-duration)).GroupBy(r=>Normalized(LyricValue(r,"trackName"))+"\n"+Normalized(LyricValue(r,"artistName"))+"\n"+AlbumKey(LyricValue(r,"albumName"))+"\n"+TextSignature(r)).Select(g=>g.First()).Take(40);
 }
 static string CandidateReason(Dictionary<string,object> r,string title,string artist,double duration,string album){
  var reasons=new List<string>();if(!SameArtist(LyricValue(r,"artistName"),artist))reasons.Add("歌手不同");if(Normalized(LyricValue(r,"trackName"))!=Normalized(title))reasons.Add("曲名版本不同");
  if(!string.IsNullOrEmpty(album)&&LiveVersion(title+" "+album)!=LiveVersion(LyricValue(r,"trackName")+" "+LyricValue(r,"albumName")))reasons.Add("现场 / 录音室版本不同");
  double delta=LyricDuration(r)-duration;if(duration>0&&Math.Abs(delta)>2)reasons.Add("时长差 "+delta.ToString("+0.0;-0.0",CultureInfo.InvariantCulture)+" 秒");
  if(reasons.Count==0&&!string.IsNullOrEmpty(album)&&!SameAlbum(r,album))reasons.Add("专辑不同，请核对版本");return reasons.Count==0?"歌名、歌手和时长相符":string.Join(" · ",reasons);
 }
 void UpdateCandidateButton(int count){var button=Find<Button>("ChooseLyrics");button.Content=count>1?"选":"搜";button.ToolTip=count>1?"有 "+count+" 个歌词版本，点击选择":"搜索并选择歌词版本";}
 async void OpenLyricChoices(){
  if(string.IsNullOrEmpty(lyricKey)){Tell("请先播放歌曲");return;}
  if(lyricChoiceWindow!=null){lyricChoiceWindow.Activate();return;}
  string key=lyricKey,title=trackTitle,artistName=trackArtist,albumName=trackAlbum;double durationSeconds=trackDuration;
  Window dialog;using(var stream=typeof(Player).Assembly.GetManifestResourceStream("Queue.xaml"))dialog=(Window)System.Windows.Markup.XamlReader.Load(stream);
  lyricChoiceWindow=dialog;dialog.Title="浮光 · 选择歌词";dialog.Width=430;dialog.Height=540;dialog.Owner=Window;dialog.WindowStartupLocation=WindowStartupLocation.CenterOwner;dialog.Resources[typeof(Button)]=Window.Resources[typeof(Button)];
  dialog.SetBinding(Window.TopmostProperty,new System.Windows.Data.Binding("Topmost"){Source=Window});((Border)dialog.FindName("QueueShell")).SetBinding(Border.BackgroundProperty,new System.Windows.Data.Binding("Background"){Source=shell});
  ((TextBlock)dialog.FindName("QueueKind")).Text="  /  歌词版本";var caption=(TextBlock)dialog.FindName("QueueTitle");caption.Text=title;caption.TextTrimming=TextTrimming.CharacterEllipsis;caption.ToolTip=title+" · "+artistName;
  ((TextBlock)dialog.FindName("QueueFooter")).Text="仅本次播放生效 · 歌词不保存到本地";
  var hint=(TextBlock)dialog.FindName("QueueHint");var rows=(StackPanel)dialog.FindName("QueueRows");var reload=(Button)dialog.FindName("QueueReload");reload.ToolTip="重新搜索歌词候选";System.Windows.Automation.AutomationProperties.SetName(reload,"重新搜索歌词候选");
  ((Button)dialog.FindName("QueueClose")).Click+=(s,e)=>dialog.Close();((Grid)dialog.FindName("QueueDragBar")).MouseLeftButtonDown+=(s,e)=>{if(e.OriginalSource is TextBlock||e.OriginalSource is Grid||e.OriginalSource is System.Windows.Shapes.Ellipse)dialog.DragMove();};dialog.PreviewKeyDown+=(s,e)=>{if(e.Key==System.Windows.Input.Key.Escape){e.Handled=true;dialog.Close();}};
  var cancellation=new CancellationTokenSource();bool disposed=false,busy=false;
  Func<bool,Task> load=async force=>{if(disposed||busy)return;busy=true;reload.IsEnabled=false;rows.Children.Clear();hint.Text="正在搜索歌词版本…";
   try{
    List<Dictionary<string,object>> candidates;
    if(force||!lyricCandidates.TryGetValue(key,out candidates)){if(!autoLyrics){hint.Text="联网查询已关闭，请在设置中开启";return;}candidates=await FindLyricCandidates(title,artistName,durationSeconds,cancellation.Token);if(lyricCandidates.Count>=60)lyricCandidates.Clear();lyricCandidates[key]=candidates;}
    if(disposed)return;var ranked=RankCandidates(candidates,title,artistName,durationSeconds,albumName).ToList();if(key==lyricKey)UpdateCandidateButton(ranked.Count);
    hint.Text=ranked.Count==0?"暂无候选，可导入本地 LRC":"找到 "+ranked.Count+" 个版本 · 当前时长 "+TimeLabel(TimeSpan.FromSeconds(durationSeconds));
    foreach(var record in ranked){
     bool sync=HasSync(record);var entry=new StackPanel{Margin=new Thickness(10,10,10,10)};
     entry.Children.Add(new TextBlock{Text=(LyricValue(record,"trackName")??title)+"  ·  "+(sync?"同步歌词":"纯文本"),FontSize=13,Foreground=sync?new SolidColorBrush(Color.FromRgb(255,135,164)):Brushes.White,TextWrapping=TextWrapping.Wrap});
     entry.Children.Add(new TextBlock{Text=(LyricValue(record,"artistName")??"")+" · "+(LyricValue(record,"albumName")??"未知专辑"),FontSize=11,Foreground=new SolidColorBrush(Color.FromRgb(192,189,204)),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,0)});
     entry.Children.Add(new TextBlock{Text=TimeLabel(TimeSpan.FromSeconds(Math.Max(0,LyricDuration(record))))+" · "+CandidateReason(record,title,artistName,durationSeconds,albumName),FontSize=10,Foreground=new SolidColorBrush(Color.FromRgb(150,149,168)),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,0)});
     var choose=new Button{Style=(Style)dialog.Resources["QueueSongButton"],Content=entry,Margin=new Thickness(0,0,0,3),ToolTip="使用此版本，仅本次播放生效"};System.Windows.Automation.AutomationProperties.SetName(choose,"选择歌词 "+LyricValue(record,"trackName")+" "+LyricValue(record,"albumName")+(sync?" 同步":" 纯文本"));
     choose.Click+=(s,e)=>{if(key!=lyricKey||closed){hint.Text="歌曲已切换，请关闭并重新打开候选";return;}try{
      string text=RecordText(record);if(lyricCancel!=null)lyricCancel.Cancel();var document=LyricDocument.Parse(text);document.Source="LRCLIB · 已选版本";SetDocument(document);Tell("已切换歌词版本，仅本次播放生效");dialog.Close();
     }catch{hint.Text="保存失败，尚未更换歌词；请重试";}};rows.Children.Add(choose);
    }
   }catch(OperationCanceledException){if(!disposed)hint.Text="搜索超时，请刷新重试";}catch{if(!disposed)hint.Text="歌词服务暂时不可用，请稍后刷新";}finally{busy=false;if(!disposed)reload.IsEnabled=true;}
  };
  reload.Click+=async(s,e)=>await load(true);dialog.Closed+=(s,e)=>{disposed=true;cancellation.Cancel();lyricChoiceWindow=null;};dialog.Show();await load(false);
 }
 public static string TestLyricChoices(){
  var a=new Dictionary<string,object>{{"id",1},{"trackName","测试"},{"artistName","歌手"},{"albumName","专辑"},{"duration",200.0},{"syncedLyrics","[00:01]甲\n[00:02]乙"}};var b=new Dictionary<string,object>(a);b["id"]=2;b["syncedLyrics"]="[00:05]甲\n[00:09]乙";
  if(SelectAutomatic(new[]{a,b},"测试","歌手",200,"专辑")!=null)throw new Exception("有歧义的歌词被自动选中");b["syncedLyrics"]=a["syncedLyrics"];if(SelectAutomatic(new[]{a,b},"测试","歌手",200,"专辑")==null)throw new Exception("相同歌词的重复记录未合并");
  if(SameArtist("甲 & 乙","甲")||!SameArtist("陶喆-David Tao","陶喆")||!SameArtist("David Tao（陶喆）","陶喆"))throw new Exception("歌手别名或合作艺人匹配失败");
  return "PASS: ambiguous candidates, duplicate records, bilingual aliases, collaborator protection";
 }
}

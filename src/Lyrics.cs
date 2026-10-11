using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Web.Script.Serialization;
using System.Security.Cryptography;

public sealed class LyricLine { public double Time; public string Text; }
public sealed class LyricDocument {
 public List<LyricLine> Lines=new List<LyricLine>(); public string Plain=""; public string Source="";
 public static LyricDocument Parse(string text) {
  var result=new LyricDocument(); var stamp=new Regex(@"\[(\d{1,4}):([0-5]\d)(?:[.:](\d{1,3}))?\]");
  double offset=0; var off=Regex.Match(text,@"\[offset:([+-]?\d{1,8})\]",RegexOptions.IgnoreCase); if(off.Success)offset=int.Parse(off.Groups[1].Value,CultureInfo.InvariantCulture)/1000.0;
  foreach(var raw in text.Replace("\r","").Split('\n')) {
   var matches=stamp.Matches(raw); if(matches.Count==0)continue;
   var content=stamp.Replace(raw,"").Trim();
   foreach(Match m in matches) { double time=int.Parse(m.Groups[1].Value)*60+int.Parse(m.Groups[2].Value); if(m.Groups[3].Success)time+=double.Parse("0."+m.Groups[3].Value,CultureInfo.InvariantCulture); result.Lines.Add(new LyricLine{Time=time-offset,Text=content}); }
  }
  result.Lines=result.Lines.OrderBy(l=>l.Time).GroupBy(l=>l.Time).Select(g=>new LyricLine{Time=g.Key,Text=string.Join("\n",g.Select(l=>l.Text))}).ToList();
  if(result.Lines.Count==0)result.Plain=Regex.Replace(text,@"\[(?:ti|ar|al|by|offset|re|ve):[^\]]*\]","",RegexOptions.IgnoreCase).Trim();
  return result;
 }
 public int IndexAt(double seconds) { int lo=0,hi=Lines.Count-1,found=-1; while(lo<=hi) { int mid=lo+(hi-lo)/2; if(Lines[mid].Time<=seconds){found=mid;lo=mid+1;}else hi=mid-1; } return found; }
}

sealed partial class Player {
 public static string TestLyrics() {
  var doc=LyricDocument.Parse("[offset:500]\n[00:01.00][00:03.500]测试甲\n[00:02.25]测试乙\n[00:02.25]译文\n[00:04] ");
  if(doc.Lines.Count!=4 || doc.Lines[0].Time!=0.5 || doc.Lines[2].Time!=3 || doc.IndexAt(0)!=-1 || doc.IndexAt(1.75)!=1 || doc.IndexAt(9)!=3 || !doc.Lines[1].Text.Contains("译文"))throw new Exception("LRC 时间解析测试失败");
  if(LyricDocument.Parse("[ti:测试]\n这是原创测试文本").Plain!="这是原创测试文本")throw new Exception("纯文本解析测试失败");
  if(ChineseVariant("繁體歌詞與時間",false)!="繁体歌词与时间")throw new Exception("简体转换失败");
  var record=new Dictionary<string,object>{{"trackName","流沙"},{"artistName","陶喆-David Tao"},{"albumName","同名专辑"},{"duration",243.0},{"syncedLyrics","[00:01]测试甲\n[00:03]测试乙"}};
  if(!LyricMatches(record,"流沙","陶喆",242.8,"陶喆")||!HasSync(record)||LyricMatches(record,"流沙 (Reimagined)","陶喆",243,"陶喆")||LyricMatches(record,"流沙","其他歌手",243,"陶喆")||LyricMatches(record,"流沙","陶喆",280,"陶喆"))throw new Exception("版本匹配测试失败");
  record["albumName"]="Soul Power Live";if(LyricMatches(record,"流沙","陶喆",243,"陶喆"))throw new Exception("现场版本排除失败");
  record["syncedLyrics"]="只有文本";if(HasSync(record))throw new Exception("无效时间轴被误判为同步歌词");
  return "PASS: LRC parsing, simplified display, verified artist alias, recording/version checks, timestamp validation";
 }
 public static async Task<string> ProbeLyrics(string payload){
  var input=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));string title=(string)input["Title"],artist=(string)input["Artist"],album=input.ContainsKey("Album")?(string)input["Album"]:"";double duration=Convert.ToDouble(input["Duration"]);
  var records=await FindLyricCandidates(title,artist,duration,CancellationToken.None);var selected=SelectAutomatic(records,title,artist,duration,album);
  return new JavaScriptSerializer().Serialize(new{Matched=selected!=null,Id=selected==null?"":RecordId(selected),Lines=selected==null?0:LyricDocument.Parse(RecordText(selected)).Lines.Count,Candidates=RankCandidates(records,title,artist,duration,album).Select(r=>new{Id=RecordId(r),Album=LyricValue(r,"albumName"),Duration=LyricDuration(r),Lines=LyricDocument.Parse(RecordText(r)).Lines.Count,Automatic=AutoLyricMatch(r,title,artist,duration,album)}).ToArray()});
 }
 public static async Task<string> DiagnoseLyrics(string title,string artist,double duration) {
  var data=await GetLyricRecord(title,artist.Split(new[]{" — "},StringSplitOptions.None)[0].Trim(),duration,CancellationToken.None);
  if(data==null)return "Lyrics: no matching recording";var text=data["syncedLyrics"] as string;return "Lyrics: matched; synchronized lines="+(string.IsNullOrEmpty(text)?0:LyricDocument.Parse(text).Lines.Count);
 }
 static string ChineseVariant(string s,bool traditional) {try{return Microsoft.VisualBasic.Strings.StrConv(s??"",traditional?Microsoft.VisualBasic.VbStrConv.TraditionalChinese:Microsoft.VisualBasic.VbStrConv.SimplifiedChinese,2052);}catch{return s??"";}}
 static string Normalized(string s) {return Regex.Replace(ChineseVariant(s,false).Normalize(NormalizationForm.FormKC),@"[\s\p{P}]","").ToLowerInvariant();}
 static string LyricValue(Dictionary<string,object> r,string key){object value;return r.TryGetValue(key,out value)?value as string:null;}
 static bool HasSync(Dictionary<string,object> r){string text=LyricValue(r,"syncedLyrics");return !string.IsNullOrWhiteSpace(text)&&LyricDocument.Parse(text).Lines.Count>=2;}
 static IEnumerable<string> ArtistKeys(string name){
  var result=new HashSet<string>{Normalized(name)};
  var bilingual=Regex.Match(name??"",@"^\s*(?<zh>[\p{IsCJKUnifiedIdeographs}\s·]+)\s*[-–—(（/]\s*(?<en>[A-Za-z][A-Za-z\s.'-]+)[)）]?\s*$");
  if(!bilingual.Success)bilingual=Regex.Match(name??"",@"^\s*(?<en>[A-Za-z][A-Za-z\s.'-]+)\s*[-–—(（/]\s*(?<zh>[\p{IsCJKUnifiedIdeographs}\s·]+)[)）]?\s*$");
  if(bilingual.Success){result.Add(Normalized(bilingual.Groups["zh"].Value));result.Add(Normalized(bilingual.Groups["en"].Value));}
  // Bilingual names are split only across scripts, never between two collaborators.
  return result;
 }
 static bool SameArtist(string a,string b){return ArtistKeys(a).Intersect(ArtistKeys(b)).Any();}
 static string AlbumKey(string name){return Normalized(Regex.Replace(name??"",@"^Optional\(""(.*)""\)$","$1"));}
 static bool SameAlbum(Dictionary<string,object> r,string album){return !string.IsNullOrWhiteSpace(album)&&AlbumKey(LyricValue(r,"albumName"))==AlbumKey(album);}
 static bool LiveVersion(string text){return Regex.IsMatch(ChineseVariant(text,false),@"\blive\b|现场|演唱会",RegexOptions.IgnoreCase);}
 static bool LyricMatches(Dictionary<string,object> r,string title,string artist,double duration,string album=""){object value;return Normalized(LyricValue(r,"trackName"))==Normalized(title)&&SameArtist(LyricValue(r,"artistName"),artist)&&(string.IsNullOrEmpty(album)||LiveVersion(title+" "+album)==LiveVersion(LyricValue(r,"trackName")+" "+LyricValue(r,"albumName")))&&(duration<1||(r.TryGetValue("duration",out value)&&Math.Abs(Convert.ToDouble(value,CultureInfo.InvariantCulture)-duration)<=2));}
 static double LyricDuration(Dictionary<string,object> r){object value;double duration;return r.TryGetValue("duration",out value)&&double.TryParse(Convert.ToString(value,CultureInfo.InvariantCulture),NumberStyles.Float,CultureInfo.InvariantCulture,out duration)?duration:0;}
 static bool AutoLyricMatch(Dictionary<string,object> r,string title,string artist,double duration,string album){return LyricMatches(r,title,artist,duration,album)&&(SameAlbum(r,album)||(duration>0&&Math.Abs(LyricDuration(r)-duration)<=0.75));}
 static async Task<HttpResponseMessage> RequestLyrics(string url,CancellationToken token){for(int n=0;;n++){var response=await lyricHttp.GetAsync(url,token);if(n>=1||((int)response.StatusCode!=503&&(int)response.StatusCode!=429))return response;response.Dispose();await Task.Delay(1500,token);}}
 static async Task<Dictionary<string,object>> GetLyricRecord(string title,string artist,double duration,CancellationToken token,string album="") {
  var records=await FindLyricCandidates(title,artist,duration,token);return SelectAutomatic(records,title,artist,duration,album);
 }
 bool lyricsOpen=true,autoLyrics=true,applyingLyricSettings,simplifiedLyrics=true;Button lyricScriptButton;
 Grid lyricPanel; TextBlock lyricSource,lyricBefore,lyricCurrent,lyricAfter,lyricOffset,plainText;
 StackPanel syncedLines; ScrollViewer plainScroll; CheckBox autoBox;
 string lyricKey="",trackTitle="",trackArtist="",trackAlbum=""; double trackDuration,lyricPosition,lyricDelay,lyricRate=1; bool lyricPlaying;
 DateTime lyricAnchor; DispatcherTimer lyricTimer; CancellationTokenSource lyricCancel;
 LyricDocument lyricDocument; int displayedLine=int.MinValue;
 readonly Dictionary<string,LyricDocument> lyricCache=new Dictionary<string,LyricDocument>();
 static readonly HttpClient lyricHttp=CreateLyricClient();
 static HttpClient CreateLyricClient() { var client=new HttpClient{Timeout=TimeSpan.FromSeconds(15)}; client.DefaultRequestHeaders.UserAgent.ParseAdd("FloatingMusic/1.0.1"); return client; }
 void InitLyrics() {
  lyricPanel=Find<Grid>("LyricsPanel"); lyricSource=Find<TextBlock>("LyricSource"); lyricBefore=Find<TextBlock>("LyricBefore"); lyricCurrent=Find<TextBlock>("LyricCurrent"); lyricAfter=Find<TextBlock>("LyricAfter"); lyricOffset=Find<TextBlock>("LyricOffset"); plainText=Find<TextBlock>("PlainLyricsText"); syncedLines=Find<StackPanel>("SyncedLines"); plainScroll=Find<ScrollViewer>("PlainLyricsScroll"); autoBox=Find<CheckBox>("AutoLyrics");
  Find<Button>("LyricsButton").Click+=(s,e)=>{lyricsOpen=!lyricsOpen; ApplyLyricsSettings(); KeepVisible(); SaveSettings();};
  Find<Button>("RefreshLyrics").Click+=(s,e)=> { if(!autoLyrics){SetLyricMessage("联网查询已关闭","在设置中开启，或导入本地 LRC");return;} FetchLyrics(true); };
  Find<Button>("ImportLyrics").Click+=(s,e)=>ImportLrc();
  Find<Button>("ChooseLyrics").Click+=(s,e)=>OpenLyricChoices();
  lyricScriptButton=Find<Button>("LyricScript");lyricScriptButton.Click+=(s,e)=>{simplifiedLyrics=!simplifiedLyrics;ApplyLyricsScript();SaveSettings();};
  Find<Button>("LyricsEarlier").Click+=(s,e)=>AdjustLyrics(-0.5);
  Find<Button>("LyricsLater").Click+=(s,e)=>AdjustLyrics(0.5);
  autoBox.Click+=(s,e)=>{ if(applyingLyricSettings)return; autoLyrics=autoBox.IsChecked==true; SaveSettings(); if(autoLyrics)FetchLyrics(false);else if(lyricCancel!=null) { lyricCancel.Cancel(); if(lyricDocument==null)SetLyricMessage("联网查询已关闭","可导入本地 LRC 歌词"); } };
  lyricTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(250)}; lyricTimer.Tick+=(s,e)=>RenderLyrics(); lyricTimer.Start();
 }
 void ApplyLyricsSettings() { applyingLyricSettings=true; autoBox.IsChecked=autoLyrics; lyricPanel.Visibility=lyricsOpen?Visibility.Visible:Visibility.Collapsed; ResizePanels(false);ApplyLyricsScript(); applyingLyricSettings=false; }
 string DisplayLyric(string text){return simplifiedLyrics?ChineseVariant(text,false):text;}
 void ApplyLyricsScript(){lyricScriptButton.Content=simplifiedLyrics?"简":"原";lyricScriptButton.ToolTip=simplifiedLyrics?"歌词显示为简体；点击查看原文":"歌词显示为原文；点击转为简体";displayedLine=int.MinValue;if(lyricDocument!=null)plainText.Text=DisplayLyric(lyricDocument.Plain);RenderLyrics();}
 void ResizePanels(bool reposition=true) { Window.Height=312+(lyricsOpen?210:0)+(settingsOpen?136:0); if(reposition && Window.IsLoaded)KeepVisible(); }
 void StopLyrics() { lyricTimer.Stop(); if(lyricCancel!=null)lyricCancel.Cancel(); }
 void ResetLyrics() { if(lyricKey.Length==0)return; lyricKey=""; lyricDocument=null; lyricPlaying=false; if(lyricCancel!=null)lyricCancel.Cancel(); SetLyricMessage("同步歌词","播放歌曲后自动查找歌词"); }
 void TrackLyrics(string key,string title,string performer,double seconds,double position,bool playing,double rate) {
  lyricPosition=position; lyricAnchor=DateTime.UtcNow; lyricPlaying=playing; lyricRate=rate;
  if(key!=lyricKey) { lyricKey=key; trackTitle=title;var parts=performer.Split(new[]{" — "},StringSplitOptions.None);trackArtist=parts[0].Trim();trackAlbum=parts.Length>1?string.Join(" — ",parts.Skip(1)):""; trackDuration=seconds; lyricDelay=0; lyricOffset.Text="0.0s"; UpdateCandidateButton(0); FetchLyrics(false); }
  RenderLyrics();
 }
 void AdjustLyrics(double amount) { lyricDelay=Math.Max(-30,Math.Min(30,lyricDelay+amount)); lyricOffset.Text=lyricDelay.ToString("+0.0;-0.0;0.0",CultureInfo.InvariantCulture)+"s"; displayedLine=int.MinValue; RenderLyrics(); }
 void SetLyricMessage(string label,string message) { lyricSource.Text=label; lyricSource.ToolTip=label; lyricBefore.Text=lyricAfter.Text=""; lyricCurrent.Text=message; syncedLines.Visibility=Visibility.Visible; plainScroll.Visibility=Visibility.Collapsed; }
 string LocalLyricPath(string key) { using(var hash=SHA256.Create()) { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"lyrics",BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-","")+".lrc"); } }
 async void FetchLyrics(bool force) {
  if(string.IsNullOrEmpty(lyricKey))return;
  if(lyricCancel!=null){lyricCancel.Cancel();lyricCancel.Dispose();} lyricCancel=new CancellationTokenSource(); var token=lyricCancel.Token; string requestKey=lyricKey;
  lyricDocument=null; displayedLine=int.MinValue;
  try {
   LyricDocument cached;
   if(!force && lyricCache.TryGetValue(requestKey,out cached)) { SetDocument(cached);return; }
   string local=LocalLyricPath(requestKey);
   if(!force && File.Exists(local)) { var doc=LyricDocument.Parse(ReadLyricsFile(local));doc.Source="本地 LRC"; SetDocument(doc);return; }
   if(!autoLyrics) { SetLyricMessage("联网查询已关闭","点击导入按钮，选择本地 LRC"); return; }
   SetLyricMessage("LRCLIB · 正在查找","正在寻找这首歌的歌词…");
   var records=await FindLyricCandidates(trackTitle,trackArtist,trackDuration,token);
   if(requestKey!=lyricKey||closed||token.IsCancellationRequested)return;
   if(lyricCandidates.Count>=60)lyricCandidates.Clear();lyricCandidates[requestKey]=records;var candidateCount=RankCandidates(records,trackTitle,trackArtist,trackDuration,trackAlbum).Count();UpdateCandidateButton(candidateCount);
   var data=SelectAutomatic(records,trackTitle,trackArtist,trackDuration,trackAlbum);
   {
    if(requestKey!=lyricKey || closed || token.IsCancellationRequested)return;
    if(data==null) { SetLyricMessage(candidateCount>0?"LRCLIB · 有 "+candidateCount+" 个候选":"LRCLIB · 未找到可靠匹配",candidateCount>0?"点击“选”核对歌词版本":"点击“搜”查找，或导入本地歌词");return; }
    if(data.ContainsKey("instrumental") && data["instrumental"] is bool && (bool)data["instrumental"]) { SetLyricMessage("LRCLIB · 纯音乐","此曲暂无人声歌词");return; }
    string lrc=data.ContainsKey("syncedLyrics")?data["syncedLyrics"] as string:null;
    string plain=data.ContainsKey("plainLyrics")?data["plainLyrics"] as string:null;
    if(string.IsNullOrWhiteSpace(lrc) && string.IsNullOrWhiteSpace(plain)) {SetLyricMessage("LRCLIB · 暂无歌词","可为这首歌导入本地 LRC");return;}
    var document=LyricDocument.Parse(!string.IsNullOrWhiteSpace(lrc)?lrc:plain);document.Source="LRCLIB";
    if(lyricCache.Count>=100)lyricCache.Clear();lyricCache[requestKey]=document;SetDocument(document);
   }
  } catch(OperationCanceledException) { if(!token.IsCancellationRequested && requestKey==lyricKey && !closed)SetLyricMessage("LRCLIB · 请求超时","点击刷新重试，或导入本地 LRC"); }
    catch(Exception) { if(requestKey==lyricKey && !closed && !token.IsCancellationRequested)SetLyricMessage("LRCLIB · 获取失败","检查网络后刷新，或导入本地 LRC"); }
 }
 static string ReadLyricsFile(string path) { if(new FileInfo(path).Length>1024*1024)throw new InvalidDataException("歌词文件不能超过 1 MB"); try { return File.ReadAllText(path,new UTF8Encoding(false,true)); }catch(DecoderFallbackException) { return File.ReadAllText(path,Encoding.GetEncoding(54936)); } }
 void ImportLrc() {
  if(string.IsNullOrEmpty(lyricKey)) {Tell("请先播放一首歌曲");return;}
  var dialog=new Microsoft.Win32.OpenFileDialog{Title="为当前歌曲导入歌词",Filter="歌词文件 (*.lrc;*.txt)|*.lrc;*.txt"};
  string importKey=lyricKey; if(dialog.ShowDialog(Window)!=true)return;
  if(importKey!=lyricKey){Tell("歌曲已切换，请重新导入");return;}
  try { string content=ReadLyricsFile(dialog.FileName); var doc=LyricDocument.Parse(content); if(doc.Lines.Count==0 && string.IsNullOrWhiteSpace(doc.Plain)){Tell("文件中没有可显示的歌词");return;}
   if(lyricCancel!=null)lyricCancel.Cancel(); doc.Source="本地 LRC"; lyricCache[lyricKey]=doc; SetDocument(doc);
   var path=LocalLyricPath(lyricKey);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,content,Encoding.UTF8);
  }catch(Exception ex){Tell("歌词导入失败："+ex.Message);}
 }
 void SetDocument(LyricDocument document) { lyricDocument=document; displayedLine=int.MinValue; lyricSource.Text=document.Source+(document.Lines.Count>0?" · 逐行同步":" · 纯文本（无时间轴）"); lyricSource.ToolTip=lyricSource.Text; plainScroll.Visibility=document.Lines.Count>0?Visibility.Collapsed:Visibility.Visible; syncedLines.Visibility=document.Lines.Count>0?Visibility.Visible:Visibility.Collapsed; plainText.Text=DisplayLyric(document.Plain); plainScroll.ScrollToTop(); RenderLyrics(); }
 void RenderLyrics() {
  if(lyricDocument==null || lyricDocument.Lines.Count==0 || !lyricsOpen)return;
  double position=lyricPosition+(lyricPlaying?Math.Max(0,(DateTime.UtcNow-lyricAnchor).TotalSeconds)*lyricRate:0)-lyricDelay;
  int index=lyricDocument.IndexAt(position); if(index==displayedLine)return; displayedLine=index;
  lyricBefore.Text=index>0?DisplayLyric(lyricDocument.Lines[index-1].Text):"";
  lyricCurrent.Text=index<0?"♫":string.IsNullOrWhiteSpace(lyricDocument.Lines[index].Text)?"♫":DisplayLyric(lyricDocument.Lines[index].Text);
  lyricAfter.Text=index+1<lyricDocument.Lines.Count?DisplayLyric(lyricDocument.Lines[index+1].Text):"";
 }
}


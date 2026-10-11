using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Automation;
using System.Text;
using System.Threading;
using Windows.Media.Control;

// All interactions are scoped to the installed AppleMusic process.
static class LibraryBridge {
 static readonly SemaphoreSlim gate=new SemaphoreSlim(1,1);
 public sealed class Request {public string Action;public string TrackKey;public string[] QueuePrefix;}
 public sealed class QueueTrack {public string Title;public string Artist;public string Duration;public string Section;public string ListId;public string Key;}
 static string QueueKey(QueueTrack track){return track.ListId+"\n"+track.Title+"\n"+track.Artist+"\n"+track.Duration;}
 static Result PlayQueue(Request request,Result result){
  CheckTrack(request.TrackKey);var prefix=request.QueuePrefix;
  if(prefix==null||prefix.Length==0||prefix.Length>result.Queue.Count||!result.Queue.Take(prefix.Length).Select(t=>t.Key).SequenceEqual(prefix))throw new InvalidOperationException("待播列表已改变，请刷新后重新选择");
  var manager=GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().GetAwaiter().GetResult();var session=manager.GetSessions().FirstOrDefault(Player.IsApple);if(session==null)throw new InvalidOperationException("Apple Music 未连接");
  string previous=request.TrackKey;
  for(int i=0;i<prefix.Length;i++){
   CheckTrack(previous);var expected=result.Queue[i];var skip=session.TrySkipNextAsync().AsTask();if(!skip.Wait(4000)||!skip.Result)throw new InvalidOperationException("Apple Music 未接受切歌，请刷新列表");
   bool confirmed=false;
   for(int n=0;n<20;n++){Thread.Sleep(150);string current=CurrentTrack();var parts=current.Split('\n');if(current==previous)continue;
    if(parts.Length>=2&&parts[0]==expected.Title&&parts[1]==expected.Artist){previous=current;confirmed=true;break;}
    if(parts.Length>=2&&!string.IsNullOrWhiteSpace(parts[0]))throw new InvalidOperationException("实际播放顺序已改变，已停止切歌，请刷新列表");
   }
   if(!confirmed)throw new InvalidOperationException("未确认切歌结果，已停止操作，请刷新列表");
  }
  result.TrackKey=previous;result.Message="正在播放："+result.Queue[prefix.Length-1].Title;return result;
 }
 public sealed class Result {public bool Ok;public string Message;public string TrackKey;public bool? Favorite;public bool CanFavorite;public List<QueueTrack> Queue=new List<QueueTrack>();}
 static string Serialize(object value){return System.Text.RegularExpressions.Regex.Replace(new JavaScriptSerializer().Serialize(value),"[^\\u0000-\\u007f]",m=>"\\u"+((int)m.Value[0]).ToString("x4"));}
 static AutomationElement ById(AutomationElement root,string id){return root.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,id));}
 static AutomationElement MainRoot(){return Roots().FirstOrDefault(r=>ById(r,"LCD")!=null);}
 static IEnumerable<AutomationElement> Elements(AutomationElement root,ControlType type){return root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,type)).Cast<AutomationElement>();}
 static bool Named(AutomationElement element,params string[] names){return names.Any(n=>string.Equals(element.Current.Name,n,StringComparison.OrdinalIgnoreCase));}
 static void Invoke(AutomationElement element){object p;if(element==null||!element.Current.IsEnabled||!element.TryGetCurrentPattern(InvokePattern.Pattern,out p))throw new InvalidOperationException("当前操作不可用");((InvokePattern)p).Invoke();}
 static AutomationElement FavoriteButton(AutomationElement lcd){return Elements(lcd,ControlType.Button).FirstOrDefault(b=>Named(b,"喜爱","喜愛","Favorite","取消喜爱","撤销喜爱","取消喜愛","撤銷喜愛","Undo Favorite","Unfavorite","Remove from Favorites"));}
 static bool IsFavorite(AutomationElement b){return Named(b,"已喜爱","已喜愛","取消喜爱","撤销喜爱","取消喜愛","撤銷喜愛","Undo Favorite","Unfavorite","Remove from Favorites","Favorited");}
 static List<AutomationElement> SongMenu(){return Roots().SelectMany(r=>Elements(r,ControlType.MenuItem)).ToList();}
 static AutomationElement MenuFavorite(){return SongMenu().FirstOrDefault(b=>Named(b,"喜爱","喜愛","Favorite")||IsFavorite(b));}
 static void Toggle(AutomationElement button){object raw;if(button.TryGetCurrentPattern(TogglePattern.Pattern,out raw))((TogglePattern)raw).Toggle();else Invoke(button);}
 static List<AutomationElement> QueueLists(AutomationElement root){return root.FindAll(TreeScope.Descendants,Condition.TrueCondition).Cast<AutomationElement>().Where(e=>e.Current.AutomationId.StartsWith("PlayQueueListView",StringComparison.Ordinal)).ToList();}
 static Result ReadQueue(AutomationElement root,Result result,Request request){
  var button=ById(root,"PlayQueueToggleButton");if(button==null)throw new InvalidOperationException("未找到官方待播列表入口");
  bool opened=false;object pattern;var toggle=button.TryGetCurrentPattern(TogglePattern.Pattern,out pattern)?(TogglePattern)pattern:null;
  try{
   if(toggle!=null?toggle.Current.ToggleState==ToggleState.Off:QueueLists(root).Count==0){Toggle(button);opened=true;Thread.Sleep(700);}
   var lists=QueueLists(root);for(int i=0;i<8&&lists.Count==0;i++){Thread.Sleep(150);lists=QueueLists(root);}
   if(lists.Count==0)throw new InvalidOperationException("官方客户端未提供待播列表控件，请稍后刷新");
   foreach(var list in lists){
    if(list.Current.AutomationId.IndexOf("History",StringComparison.OrdinalIgnoreCase)>=0)continue;
    foreach(AutomationElement row in list.FindAll(TreeScope.Children,Condition.TrueCondition)){
     var texts=Elements(row,ControlType.Text).Select(t=>t.Current.Name).Where(t=>!string.IsNullOrWhiteSpace(t)).ToList();if(texts.Count==0)continue;
     string duration=System.Text.RegularExpressions.Regex.IsMatch(texts.Last(),@"^\d+:\d{2}(:\d{2})?$")?texts.Last():"";
     var track=new QueueTrack{Title=texts[0],Artist=texts.Count>(duration.Length>0?2:1)?texts[1]:"",Duration=duration,ListId=list.Current.AutomationId,Section=list.Current.AutomationId.IndexOf("Autoplay",StringComparison.OrdinalIgnoreCase)>=0?"自动播放":"接下来"};track.Key=QueueKey(track);result.Queue.Add(track);
    }
   }
   CheckTrack(result.TrackKey);
   if(request.Action=="play-queue")return PlayQueue(request,result);
   result.Message=result.Queue.Count==0?"当前没有读取到待播歌曲":"已读取 "+result.Queue.Count+" 首待播歌曲（官方控件当前提供的内容）";return result;
  }finally{if(opened){try{if(toggle==null||toggle.Current.ToggleState==ToggleState.On)Toggle(button);}catch{}}}
 }
 static string CurrentTrack(){var m=GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().GetAwaiter().GetResult();var s=m.GetSessions().FirstOrDefault(Player.IsApple);if(s==null)return "";var p=s.TryGetMediaPropertiesAsync().AsTask().GetAwaiter().GetResult();return p.Title+"\n"+p.Artist+"\n"+p.AlbumTitle;}
 static void CheckTrack(string expected){if(string.IsNullOrEmpty(expected)||CurrentTrack()!=expected)throw new InvalidOperationException("歌曲已切换，请重新点击");}
 public static Result Execute(Request request) {
  var root=MainRoot();if(root==null)throw new InvalidOperationException("未找到 Apple Music 主窗口");var lcd=ById(root,"LCD");
  var result=new Result{Ok=true,TrackKey=CurrentTrack()};
  if(request.Action=="snapshot") {result.CanFavorite=FavoriteButton(lcd)!=null;return result;}
  if(request.Action=="queue"||request.Action=="play-queue")return ReadQueue(root,result,request);
  if(request.Action=="favorite"||request.Action=="dislike"||request.Action=="inspect-menu") {
   if(request.Action!="inspect-menu")CheckTrack(request.TrackKey);var action=ById(lcd,"ActionButton");Invoke(action);
   bool menuOpen=true;
   try{for(int n=0;n<10;n++){Thread.Sleep(100);var menu=request.Action=="favorite"?MenuFavorite():SongMenu().FirstOrDefault(e=>Named(e,"减少推荐","減少推薦","Suggest Less","Suggest Less Like This"));
    if(menu==null)continue;
    if(request.Action=="inspect-menu"){result.Message=string.Join(" / ",SongMenu().Select(e=>e.Current.Name));return result;}
    CheckTrack(request.TrackKey);bool before=IsFavorite(menu);Invoke(menu);menuOpen=false;
    if(request.Action=="dislike"){result.Message="已向 Apple Music 提交减少推荐";return result;}
    Thread.Sleep(400);CheckTrack(request.TrackKey);Invoke(action);menuOpen=true;
    for(int j=0;j<6;j++){Thread.Sleep(150);var updated=MenuFavorite();if(updated!=null&&IsFavorite(updated)!=before){CheckTrack(request.TrackKey);result.CanFavorite=true;result.Favorite=IsFavorite(updated);result.Message=result.Favorite==true?"已设为喜爱":"已取消喜爱";return result;}}
    throw new InvalidOperationException("已提交喜爱操作，但未确认状态变化");
   }throw new InvalidOperationException("当前歌曲的操作菜单不可用，请稍后重试");}
   finally{if(menuOpen){try{Invoke(action);}catch{}}}
  }
  throw new InvalidOperationException("未知操作");
 }
 public static int RunWorker(string payload) {try{var request=new JavaScriptSerializer().Deserialize<Request>(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));Console.WriteLine(Serialize(Execute(request)));return 0;}catch(Exception ex){Console.WriteLine(Serialize(new Result{Message=ex is InvalidOperationException?ex.Message:"官方控件暂时不可用，请重试"}));return 1;}}
 public static Task<Result> CallAsync(Request request) {return Task.Run(()=>{gate.Wait();try{var payload=Convert.ToBase64String(Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(request)));var start=new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,"--library-control "+payload){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};using(var worker=Process.Start(start)){var output=worker.StandardOutput.ReadToEndAsync();if(!worker.WaitForExit(request.Action=="play-queue"?120000:8000)){worker.Kill();worker.WaitForExit();throw new TimeoutException("官方控件响应超时");}var response=new JavaScriptSerializer().Deserialize<Result>(output.GetAwaiter().GetResult());if(response==null||!response.Ok)throw new InvalidOperationException(response==null?"未收到操作结果":response.Message);return response;}}finally{gate.Release();}});}
 public sealed class Item { public string Id;public string Name;public string Type;public string[] Patterns;public string Parent;public string Help;public string Status;public string Toggle; }
 public static List<AutomationElement> Roots() {
  var roots=new List<AutomationElement>();
  foreach(var p in Process.GetProcessesByName("AppleMusic"))using(p){foreach(AutomationElement e in AutomationElement.RootElement.FindAll(TreeScope.Children,new PropertyCondition(AutomationElement.ProcessIdProperty,p.Id)))roots.Add(e);}
  return roots;
 }
 public static int Inspect() {
  try {
   var items=new List<Item>();
   foreach(var root in Roots())foreach(AutomationElement e in root.FindAll(TreeScope.Descendants,new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.MenuItem),new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.ListItem)))) {
    var c=e.Current;var p=TreeWalker.ControlViewWalker.GetParent(e);items.Add(new Item{Id=c.AutomationId,Name=c.Name,Type=c.ControlType.ProgrammaticName,Patterns=e.GetSupportedPatterns().Select(pattern=>pattern.ProgrammaticName).ToArray(),Parent=p==null?"":p.Current.AutomationId+" / "+p.Current.Name,Help=c.HelpText,Status=c.ItemStatus,Toggle=Convert.ToString(e.GetCurrentPropertyValue(TogglePattern.ToggleStateProperty,true))});
   }
   Console.WriteLine(System.Text.RegularExpressions.Regex.Replace(new JavaScriptSerializer().Serialize(items),"[^\\u0000-\\u007f]",m=>"\\u"+((int)m.Value[0]).ToString("x4")));return 0;
  }catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}
 }
}

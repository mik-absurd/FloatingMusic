using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

sealed partial class Player {
 Button favoriteButton,dislikeButton;bool accountReading,accountBusy;string accountTrack="";DateTime accountReadAt=DateTime.MinValue;Window queueWindow;bool? knownFavorite;
 void InitLibrary() {
  favoriteButton=Find<Button>("FavoriteCurrent");dislikeButton=Find<Button>("DislikeCurrent");
  favoriteButton.Click+=async(s,e)=>await Preference("favorite");dislikeButton.Click+=async(s,e)=>await Preference("dislike");Find<Button>("BrowseQueue").Click+=(s,e)=>OpenQueue();
 }
 async void ReadAccount() {
  if(accountTrack!=mediaKey){knownFavorite=null;favoriteButton.IsEnabled=dislikeButton.IsEnabled=false;favoriteButton.Content="喜爱 / 取消";favoriteButton.Foreground=Brushes.LightGray;}
  if(closed||session==null||accountReading||accountBusy||(accountTrack==mediaKey&&DateTime.UtcNow<accountReadAt))return;
  accountReading=true;var key=mediaKey;accountReadAt=DateTime.UtcNow.AddSeconds(8);
  try {var result=await LibraryBridge.CallAsync(new LibraryBridge.Request{Action="snapshot"});if(!closed&&key==mediaKey&&result.TrackKey==key&&!accountBusy){accountTrack=key;ShowFavorite(result);dislikeButton.IsEnabled=true;}}
  catch {if(key==mediaKey){accountTrack=key;favoriteButton.ToolTip="当前操作不可用，稍后重试";}}
  finally {accountReading=false;}
 }
 void ShowFavorite(LibraryBridge.Result result){if(result.Favorite.HasValue)knownFavorite=result.Favorite;favoriteButton.IsEnabled=result.CanFavorite;favoriteButton.Content=knownFavorite.HasValue?(knownFavorite.Value?"★ 已喜爱":"☆ 喜爱"):"喜爱 / 取消";favoriteButton.Foreground=new SolidColorBrush(knownFavorite==true?Color.FromRgb(255,115,148):Color.FromRgb(212,213,223));favoriteButton.ToolTip="点击时读取官方状态并切换喜爱；不会自动更改偏好";}
 async Task Preference(string action) {
  if(commandBusy||accountBusy||session==null)return;accountBusy=true;commandBusy=true;favoriteButton.IsEnabled=dislikeButton.IsEnabled=false;string key=mediaKey;
  try {var result=await LibraryBridge.CallAsync(new LibraryBridge.Request{Action=action,TrackKey=key});if(key==mediaKey&&!closed){Tell(result.Message);if(action=="favorite")ShowFavorite(result);}}
  catch(Exception ex){Tell(ex.Message);}
  finally{accountBusy=false;commandBusy=false;accountReadAt=DateTime.MinValue;}await Refresh();
 }
 async void OpenQueue() {
  if(queueWindow!=null){queueWindow.Activate();return;}
  Window dialog;using(var stream=typeof(Player).Assembly.GetManifestResourceStream("Queue.xaml"))dialog=(Window)System.Windows.Markup.XamlReader.Load(stream);
  dialog.Resources[typeof(Button)]=Window.Resources[typeof(Button)];dialog.Owner=Window;dialog.WindowStartupLocation=WindowStartupLocation.CenterOwner;queueWindow=dialog;
  dialog.SetBinding(Window.TopmostProperty,new System.Windows.Data.Binding("Topmost"){Source=Window});
  ((Border)dialog.FindName("QueueShell")).SetBinding(Border.BackgroundProperty,new System.Windows.Data.Binding("Background"){Source=shell});
  var hint=(TextBlock)dialog.FindName("QueueHint");var reload=(Button)dialog.FindName("QueueReload");var rows=(StackPanel)dialog.FindName("QueueRows");
  ((Button)dialog.FindName("QueueClose")).Click+=(s,e)=>dialog.Close();
  ((Grid)dialog.FindName("QueueDragBar")).MouseLeftButtonDown+=(s,e)=>{if(e.OriginalSource is TextBlock||e.OriginalSource is Grid||e.OriginalSource is System.Windows.Shapes.Ellipse)dialog.DragMove();};
  dialog.PreviewKeyDown+=(s,e)=>{if(e.Key==System.Windows.Input.Key.Escape){e.Handled=true;dialog.Close();}};
  bool busy=false,disposed=false;
  Func<Task> load=null;load=async()=>{if(busy||disposed)return;busy=true;reload.IsEnabled=false;hint.Text="正在读取…";rows.Children.Clear();try{
   var result=await LibraryBridge.CallAsync(new LibraryBridge.Request{Action="queue"});if(disposed)return;int index=0;string section="";
   foreach(var song in result.Queue){
    if(song.Section!=section){section=song.Section;if(section!="接下来")rows.Children.Add(new TextBlock{Text=section,Foreground=new SolidColorBrush(Color.FromRgb(255,115,148)),FontSize=10,Margin=new Thickness(8,12,0,6)});}
    index++;var entry=new Grid{Margin=new Thickness(8,10,8,10)};entry.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(27)});entry.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});entry.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(40)});
    entry.Children.Add(new TextBlock{Text=index.ToString("00"),FontSize=10,Foreground=new SolidColorBrush(Color.FromRgb(140,139,159)),VerticalAlignment=VerticalAlignment.Center});
    var details=new StackPanel{Margin=new Thickness(0,0,8,0)};Grid.SetColumn(details,1);entry.Children.Add(details);
    details.Children.Add(new TextBlock{Text=song.Title,FontSize=13,TextTrimming=TextTrimming.CharacterEllipsis,ToolTip=song.Title,Foreground=new SolidColorBrush(index==1?Color.FromRgb(255,135,164):Color.FromRgb(244,244,248))});
    details.Children.Add(new TextBlock{Text=song.Artist,FontSize=10,Foreground=new SolidColorBrush(Color.FromRgb(160,156,176)),TextTrimming=TextTrimming.CharacterEllipsis,ToolTip=song.Artist,Margin=new Thickness(0,5,0,0)});
    var length=new TextBlock{Text=song.Duration,FontSize=10,Foreground=new SolidColorBrush(Color.FromRgb(150,149,168)),VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Right};Grid.SetColumn(length,2);entry.Children.Add(length);
    var choose=new Button{Style=(Style)dialog.Resources["QueueSongButton"],Content=entry,Background=index==1?new SolidColorBrush(Color.FromArgb(16,255,115,148)):Brushes.Transparent,Margin=new Thickness(0,0,0,2),ToolTip="播放《"+song.Title+"》；较靠后的歌曲需要逐首跳转"};
    System.Windows.Automation.AutomationProperties.SetName(choose,"播放 "+song.Title);int selectedCount=index;
    choose.Click+=async(s,e)=>{
     if(busy||commandBusy||accountBusy||disposed)return;busy=true;commandBusy=accountBusy=true;reload.IsEnabled=rows.IsEnabled=false;hint.Text="正在跳到《"+song.Title+"》…";string outcome="";
     try{var played=await LibraryBridge.CallAsync(new LibraryBridge.Request{Action="play-queue",TrackKey=result.TrackKey,QueuePrefix=result.Queue.Take(selectedCount).Select(t=>t.Key).ToArray()});outcome=played.Message;}
     catch(Exception ex){outcome=ex.Message;}
     finally{busy=false;commandBusy=accountBusy=false;rows.IsEnabled=reload.IsEnabled=true;accountReadAt=DateTime.MinValue;}
     Tell(outcome);await Refresh();if(!disposed){await load();hint.Text=outcome;hint.ToolTip=outcome;}
    };rows.Children.Add(choose);
   }
   hint.Text=result.Queue.Count==0?"暂无待播歌曲":"已加载 "+result.Queue.Count+" 首歌曲";hint.ToolTip=result.Message;
  }catch(Exception ex){if(!disposed){hint.Text="暂时无法读取，点击右上角重试";hint.ToolTip=ex.Message;rows.Children.Add(new TextBlock{Text=ex.Message,FontSize=12,Foreground=Brushes.LightGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(8,20,8,0)});}}finally{busy=false;if(!disposed)reload.IsEnabled=true;}};
  reload.Click+=async(s,e)=>await load();dialog.Closed+=(s,e)=>{disposed=true;queueWindow=null;};dialog.Show();await load();
 }
}

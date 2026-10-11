using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Automation;

// Calls the official slider's accessibility pattern. Never activates, restores,
// repositions or pins the Apple Music window, and never sends mouse/keyboard input.
static class AppleSeek {
 [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr window,int index);
 public static int RunWorker(double seconds) {
  try {
   var foreground=GetForegroundWindow();bool foundWindow=false;
   foreach(var process in Process.GetProcessesByName("AppleMusic"))using(process) {
    var roots=AutomationElement.RootElement.FindAll(TreeScope.Children,new PropertyCondition(AutomationElement.ProcessIdProperty,process.Id));
    foreach(AutomationElement root in roots) {
     foundWindow=true;
     var slider=root.FindFirst(TreeScope.Descendants,new AndCondition(new PropertyCondition(AutomationElement.AutomationIdProperty,"LCDScrubber"),new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Slider)));
     if(slider==null)continue;object raw;
     if(!slider.Current.IsEnabled||!slider.TryGetCurrentPattern(RangeValuePattern.Pattern,out raw))continue;
     var pattern=(RangeValuePattern)raw;if(pattern.Current.IsReadOnly)continue;
     if(double.IsNaN(seconds)||double.IsInfinity(seconds)||seconds<pattern.Current.Minimum||seconds>pattern.Current.Maximum)return 14;
     var window=new IntPtr(root.Current.NativeWindowHandle);bool minimized=IsIconic(window);bool topmost=(GetWindowLongPtr(window,-20).ToInt64()&8)!=0;
     pattern.SetValue(seconds);
     Console.WriteLine("control=LCDScrubber; target="+seconds.ToString(CultureInfo.InvariantCulture)+"; minimizedBefore="+minimized+"; minimizedAfter="+IsIconic(window)+"; topmostBefore="+topmost+"; topmostAfter="+((GetWindowLongPtr(window,-20).ToInt64()&8)!=0)+"; foregroundUnchanged="+(foreground==GetForegroundWindow()));
     return 0;
    }
   }
   return foundWindow?12:11;
  }catch(Exception ex){Console.WriteLine(ex.GetType().Name);return 13;}
 }
 public static Task<string> SetAsync(double seconds) {
  return Task.Run(()=> {
   var info=new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,"--seek-control "+seconds.ToString("R",CultureInfo.InvariantCulture)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
   using(var worker=Process.Start(info)) {
    if(!worker.WaitForExit(5000)){worker.Kill();worker.WaitForExit();throw new TimeoutException("进度控件响应超时");}
    var detail=worker.StandardOutput.ReadToEnd().Trim();
    if(worker.ExitCode==11)throw new InvalidOperationException("请保持 Apple Music 主窗口运行");
    if(worker.ExitCode==12)throw new InvalidOperationException("进度控件不可用；请还原 Apple Music 主窗口后重试，无需置顶");
    if(worker.ExitCode!=0)throw new InvalidOperationException("官方进度控件未接受请求");
    return detail;
   }
  });
 }
}

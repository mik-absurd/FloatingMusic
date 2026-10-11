using System;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Controls;

// Windows Core Audio ABI: mmdeviceapi.h and audiopolicy.h.
static class AppleVolume {
 [ComImport,Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class EnumeratorClass { }
 [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface Enumerator {[PreserveSig]int EnumAudioEndpoints(int flow,uint state,out Devices devices);}
 [ComImport,Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface Devices {[PreserveSig]int GetCount(out uint count);[PreserveSig]int Item(uint index,out Device device);}
 [ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface Device {[PreserveSig]int Activate(ref Guid iid,uint context,IntPtr parameters,[MarshalAs(UnmanagedType.IUnknown)]out object result);}
 [ComImport,Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface Manager {
  [PreserveSig]int GetAudioSessionControl(ref Guid id,uint flags,out IntPtr control);
  [PreserveSig]int GetSimpleAudioVolume(ref Guid id,uint flags,out IntPtr volume);
  [PreserveSig]int GetSessionEnumerator(out Sessions sessions);
 }
 [ComImport,Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface Sessions {[PreserveSig]int GetCount(out int count);[PreserveSig]int GetSession(int index,[MarshalAs(UnmanagedType.IUnknown)]out object session);}
 [ComImport,Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface Control {
  [PreserveSig]int GetState(out int state);
  [PreserveSig]int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)]out string name);
  [PreserveSig]int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)]string name,ref Guid context);
  [PreserveSig]int GetIconPath([MarshalAs(UnmanagedType.LPWStr)]out string path);
  [PreserveSig]int SetIconPath([MarshalAs(UnmanagedType.LPWStr)]string path,ref Guid context);
  [PreserveSig]int GetGroupingParam(out Guid grouping);
  [PreserveSig]int SetGroupingParam(ref Guid grouping,ref Guid context);
  [PreserveSig]int RegisterAudioSessionNotification(IntPtr client);
  [PreserveSig]int UnregisterAudioSessionNotification(IntPtr client);
  [PreserveSig]int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)]out string id);
  [PreserveSig]int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)]out string id);
  [PreserveSig]int GetProcessId(out uint id);
 }
 [ComImport,Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface Volume {
  [PreserveSig]int SetMasterVolume(float volume,ref Guid context);[PreserveSig]int GetMasterVolume(out float volume);
  [PreserveSig]int SetMute([MarshalAs(UnmanagedType.Bool)]bool muted,ref Guid context);[PreserveSig]int GetMute([MarshalAs(UnmanagedType.Bool)]out bool muted);
 }
 public sealed class State {public float Level;public bool Muted;public int Count;public int ActiveCount;public uint ProcessId;}
 static void Release(object value){if(value!=null && Marshal.IsComObject(value))Marshal.ReleaseComObject(value);}
 static void Check(int hr){Marshal.ThrowExceptionForHR(hr);}
 public static State Access(float? level=null,bool? mute=null,Action<string> observe=null) {
  var ids=new HashSet<uint>();var folders=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  foreach(var p in Process.GetProcessesByName("AppleMusic"))using(p){try{ids.Add((uint)p.Id);folders.Add(Path.GetDirectoryName(p.MainModule.FileName));}catch{}}
  foreach(var p in Process.GetProcessesByName("AMPLibraryAgent"))using(p){try{if(folders.Contains(Path.GetDirectoryName(p.MainModule.FileName)))ids.Add((uint)p.Id);}catch{}}
  var result=new State();if(ids.Count==0)return result;object root=new EnumeratorClass();Devices devices=null;
  try {Check(((Enumerator)root).EnumAudioEndpoints(0,1,out devices));uint count;Check(devices.GetCount(out count));
   for(uint n=0;n<count;n++){Device device=null;object manager=null;Sessions sessions=null;
    try{Check(devices.Item(n,out device));var iid=typeof(Manager).GUID;Check(device.Activate(ref iid,23,IntPtr.Zero,out manager));Check(((Manager)manager).GetSessionEnumerator(out sessions));int total;Check(sessions.GetCount(out total));
     for(int j=0;j<total;j++){object control=null;try{Check(sessions.GetSession(j,out control));uint pid;Check(((Control)control).GetProcessId(out pid));
      if(observe!=null){int state;((Control)control).GetState(out state);if(state==1||ids.Contains(pid)){string processName="system";try{using(var p=Process.GetProcessById((int)pid))processName=p.ProcessName;}catch{}float scalar;((Volume)control).GetMasterVolume(out scalar);observe("device="+n+" process="+processName+" pid="+pid+" state="+state+" volume="+scalar);}}
      if(!ids.Contains(pid))continue;var v=(Volume)control;var context=Guid.Empty;
      if(level.HasValue)Check(v.SetMasterVolume(Math.Max(0,Math.Min(1,level.Value)),ref context));if(mute.HasValue)Check(v.SetMute(mute.Value,ref context));float current;bool muted;int audioState;Check(((Control)control).GetState(out audioState));Check(v.GetMasterVolume(out current));Check(v.GetMute(out muted));if(result.Count==0||(audioState==1&&result.ActiveCount==0)){result.Level=current;result.Muted=muted;result.ProcessId=pid;}if(audioState==1)result.ActiveCount++;result.Count++;
     }finally{Release(control);}}
    }finally{Release(sessions);Release(manager);Release(device);}
   }
  }finally{Release(devices);Release(root);}return result;
 }
 public static string Test(){var before=Access();if(before.Count==0)return "Volume: no Apple Music audio session";float target=Math.Max(0,before.Level-0.03f);try{var applied=Access(target);if(Math.Abs(applied.Level-target)>0.005)throw new Exception("音量设置未生效");return "Apple Music volume read/write: PASS; sessions="+before.Count;}finally{Access(before.Level);}}
}

sealed partial class Player {
 Slider volumeSlider;Button muteButton;TextBlock volumeLabel;bool updatingVolume,changingVolume,readingVolume,volumeDragging;long volumeRevision;float? desiredVolume;AppleVolume.State volumeState;
 void InitVolume(){volumeSlider=Find<Slider>("VolumeSlider");muteButton=Find<Button>("MuteButton");volumeLabel=Find<TextBlock>("VolumeLabel");
  new PointerSlider(volumeSlider,()=>volumeDragging=true,value=>{volumeDragging=false;desiredVolume=(float)(value/100);volumeRevision++;SetVolume();});
  volumeSlider.ValueChanged+=(s,e)=>{if(!updatingVolume){volumeRevision++;desiredVolume=(float)(volumeSlider.Value/100);volumeLabel.Text=Math.Round(volumeSlider.Value)+"%";SetVolume();}};
  muteButton.Click+=async(s,e)=>{if(changingVolume||volumeState==null)return;changingVolume=true;try{volumeState=await Task.Run(()=>AppleVolume.Access(null,!volumeState.Muted));ShowVolume();}catch{Tell("音量调整失败");}finally{changingVolume=false;}};
 }
 async void SetVolume(){if(changingVolume)return;changingVolume=true;try{while(desiredVolume.HasValue){float target=desiredVolume.Value;desiredVolume=null;volumeState=await Task.Run(()=>AppleVolume.Access(target));}ShowVolume();}catch{Tell("音量调整失败");}finally{changingVolume=false;}}
 async void ReadVolume(){if(readingVolume||changingVolume||volumeDragging||closed)return;readingVolume=true;long revision=volumeRevision;try{var state=await Task.Run(()=>AppleVolume.Access());if(!changingVolume&&!volumeDragging&&revision==volumeRevision){volumeState=state;ShowVolume();}}catch{volumeSlider.IsEnabled=muteButton.IsEnabled=false;}finally{readingVolume=false;}}
 void ShowVolume(){if(closed||volumeDragging)return;bool available=volumeState!=null&&volumeState.Count>0;volumeSlider.IsEnabled=muteButton.IsEnabled=available;if(!available){volumeLabel.Text="--";return;}updatingVolume=true;volumeSlider.Value=volumeState.Level*100;updatingVolume=false;volumeLabel.Text=volumeState.Muted?"静音":Math.Round(volumeSlider.Value)+"%";muteButton.Content=volumeState.Muted?"\uE74F":"\uE767";muteButton.ToolTip=volumeState.Muted?"取消 Apple Music 静音":"将 Apple Music 静音";}
}


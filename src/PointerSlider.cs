using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

// Capture the whole rail, rather than relying on a three-pixel RepeatButton hit area.
// Commits the chosen value before polling can replace it with an older backend value.
sealed class PointerSlider {
 readonly Slider slider;readonly Action start;readonly Action<double> commit;bool active;
 public PointerSlider(Slider target,Action onStart,Action<double> onCommit){
  slider=target;start=onStart;commit=onCommit;slider.IsMoveToPointEnabled=false;
  slider.AddHandler(Mouse.PreviewMouseDownEvent,new MouseButtonEventHandler(Down),true);
  slider.AddHandler(Mouse.PreviewMouseMoveEvent,new MouseEventHandler(Move),true);
  slider.AddHandler(Mouse.PreviewMouseUpEvent,new MouseButtonEventHandler(Up),true);
  slider.LostMouseCapture+=(s,e)=>{if(active)Finish();};
  slider.PreviewKeyDown+=HandleKey;
 }
 void Down(object sender,MouseButtonEventArgs e){if(e.ChangedButton!=MouseButton.Left||!slider.IsEnabled)return;e.Handled=true;slider.Focus();active=true;start();slider.CaptureMouse();SetAt(e.GetPosition(slider).X);}
 void Move(object sender,MouseEventArgs e){if(!active)return;e.Handled=true;if(e.LeftButton!=MouseButtonState.Pressed){Finish();return;}SetAt(e.GetPosition(slider).X);}
 void Up(object sender,MouseButtonEventArgs e){if(!active||e.ChangedButton!=MouseButton.Left)return;e.Handled=true;SetAt(e.GetPosition(slider).X);Finish();}
 void Finish(){if(!active)return;double value=slider.Value;active=false;if(slider.IsMouseCaptured)slider.ReleaseMouseCapture();commit(value);}
 void SetAt(double x){double thumb=12;slider.ApplyTemplate();var track=slider.Template.FindName("PART_Track",slider) as Track;if(track!=null&&track.Thumb!=null&&track.Thumb.ActualWidth>0)thumb=track.Thumb.ActualWidth;double ratio=Math.Max(0,Math.Min(1,(x-thumb/2)/Math.Max(1,slider.ActualWidth-thumb)));slider.Value=slider.Minimum+ratio*(slider.Maximum-slider.Minimum);}
 void HandleKey(object sender,KeyEventArgs e){double next=slider.Value;switch(e.Key){case Key.Left:case Key.Down:next-=slider.SmallChange;break;case Key.Right:case Key.Up:next+=slider.SmallChange;break;case Key.Home:next=slider.Minimum;break;case Key.End:next=slider.Maximum;break;case Key.PageDown:next-=slider.LargeChange;break;case Key.PageUp:next+=slider.LargeChange;break;default:return;}e.Handled=true;start();slider.Value=Math.Max(slider.Minimum,Math.Min(slider.Maximum,next));commit(slider.Value);}
}


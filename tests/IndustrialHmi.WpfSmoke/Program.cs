using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IndustrialHmi.App.Controls;
using IndustrialHmi.Core.Models;
using IndustrialHmi.Core.Services;
using IndustrialHmi.Core.ViewModels;
namespace IndustrialHmi.WpfSmoke;
internal static class Program {
    [STAThread] private static int Main() {
        var errors=new BindingErrors();PresentationTraceSources.DataBindingSource.Listeners.Add(errors);PresentationTraceSources.DataBindingSource.Switch.Level=SourceLevels.Error;
        var app=new Application();app.Resources.MergedDictionaries.Add(new ResourceDictionary {Source=new Uri("pack://application:,,,/IndustrialHmi.App;component/Themes/Dark.xaml")});
        var sim=new SimulatedPlcClient();var vm=new MainViewModel(new MachineController(sim),new HmiSettings(),sim);
        var window=new IndustrialHmi.App.MainWindow(vm);var result=1;
        // Hard timeout also catches startup/closing hangs.
        var timeout=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromSeconds(40)};
        timeout.Tick+=(_,_)=>{Console.Error.WriteLine("WPF smoke timed out");app.Shutdown(1);};timeout.Start();
        window.Loaded+=async(_,_)=>{
            try {
                await WaitUntilAsync(()=>vm.IsConnected);
                var output=Path.Combine(Environment.CurrentDirectory,"artifacts","screenshots");Directory.CreateDirectory(output);
                foreach(var page in vm.Pages) {
                    vm.SelectedPage=page;await Task.Delay(150);window.UpdateLayout();
                    Require(errors.Messages.Count==0,"Binding error: "+string.Join(";",errors.Messages));
                    var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);
                    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(output,page+".png"));encoder.Save(stream);
                }
                vm.SelectedPage="手动调试";await Task.Delay(150);window.UpdateLayout();
                var jog=Descendants(window).OfType<HoldJogButton>().First(b=>b.Positive);
                jog.Focus();
                jog.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(jog),0,Key.Space){RoutedEvent=Keyboard.PreviewKeyDownEvent});
                await WaitUntilAsync(()=>vm.Velocity>0);
                jog.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(jog),0,Key.Space){RoutedEvent=Keyboard.PreviewKeyUpEvent});
                await WaitUntilAsync(()=>vm.Velocity==0);
                await vm.ExtendCommand.ExecuteAsync(null);Require(vm.CylinderWork,"Cylinder feedback not updated");
                sim.SetInput(Tag.DoorClosed,false);await vm.TickAsync();Require(!vm.ManualAllowed && vm.AlarmCount>0,"Interlock failed");
                Require(errors.Messages.Count==0,string.Join(";",errors.Messages));
                Console.WriteLine("PASS: 5 pages, XAML bindings, screenshots, hold/release jog, cylinder feedback, door interlock.");result=0;
            }catch(Exception e){Console.Error.WriteLine(e);}
            finally{timeout.Stop();window.Close();}
        };
        app.Run(window);return result;
    }
    private static async Task WaitUntilAsync(Func<bool> condition) {for(var i=0;i<100;i++){if(condition())return;await Task.Delay(50);}throw new TimeoutException("Expected UI condition not reached.");}
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root){for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var item in Descendants(child))yield return item;}}
    private sealed class BindingErrors : TraceListener {
        public List<string> Messages {get;}=[];
        public override void Write(string? message){if(!string.IsNullOrEmpty(message))Messages.Add(message);}
        public override void WriteLine(string? message)=>Write(message);
    }
}

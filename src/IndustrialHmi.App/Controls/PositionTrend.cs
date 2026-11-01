using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;
namespace IndustrialHmi.App.Controls;
public sealed class PositionTrend : FrameworkElement {
    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(nameof(Samples), typeof(ObservableCollection<double>), typeof(PositionTrend), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, Changed));
    public ObservableCollection<double>? Samples { get => (ObservableCollection<double>?)GetValue(SamplesProperty); set => SetValue(SamplesProperty,value); }
    public PositionTrend() {
        Loaded += (_, _) => {
            if (Samples is not null) { Samples.CollectionChanged -= Redraw; Samples.CollectionChanged += Redraw; }
            InvalidateVisual();
        };
        Unloaded += (_, _) => { if (Samples is not null) Samples.CollectionChanged -= Redraw; };
    }
    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        var plot=(PositionTrend)d;
        if(e.OldValue is INotifyCollectionChanged old) old.CollectionChanged -= plot.Redraw;
        if(plot.IsLoaded && e.NewValue is INotifyCollectionChanged current) current.CollectionChanged += plot.Redraw;
    }
    private void Redraw(object? s, NotifyCollectionChangedEventArgs e) => InvalidateVisual();
    protected override void OnRender(DrawingContext dc) {
        base.OnRender(dc); var w=ActualWidth; var h=ActualHeight;
        var grid=new Pen(new SolidColorBrush(Color.FromRgb(38,52,70)),1);
        for(var i=0;i<5;i++) dc.DrawLine(grid,new Point(0,h*i/4),new Point(w,h*i/4));
        for(var i=0;i<9;i++) dc.DrawLine(grid,new Point(w*i/8,0),new Point(w*i/8,h));
        if(Samples is not {Count:>1} samples) return;
        var min=samples.Min()-1; var max=samples.Max()+1;
        var geometry=new StreamGeometry();
        using(var ctx=geometry.Open()) {
            for(var i=0;i<samples.Count;i++) {
                var point=new Point(w*i/Math.Max(119,samples.Count-1),h-8-(samples[i]-min)/(max-min)*(h-16));
                if(i==0)ctx.BeginFigure(point,false,false); else ctx.LineTo(point,true,false);
            }
        }
        dc.DrawGeometry(null,new Pen(new SolidColorBrush(Color.FromRgb(66,214,176)),2),geometry);
    }
}

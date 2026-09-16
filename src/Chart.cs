using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Input;
namespace Thermal {
    public class HistoryChart : FrameworkElement {
        public List<Reading> Rows=new List<Reading>();
        public DateTime Start, End;
        public double CpuWarning=85, GpuWarning=85;
        public HistoryChart() { MouseMove+=Hover; MouseLeave+=(s,e)=> { ToolTip=null; }; }
        static Brush B(string s) { return (Brush)new BrushConverter().ConvertFromString(s); }
        static void Text(DrawingContext dc,string text,double x,double y,Brush brush,double size) { dc.DrawText(new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,brush,1.0),new Point(x,y)); }
        protected override void OnRender(DrawingContext dc) {
            base.OnRender(dc); double w=ActualWidth-48,h=ActualHeight-35; if(w<1||h<1)return;
            dc.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,ActualWidth,ActualHeight));
            double max=Math.Max(100,Math.Ceiling((Aggregation.Max(Rows.SelectMany(r=>new[]{r.CpuTemp,r.GpuTemp})) ?? 0)/20)*20);
            for(int i=0;i<=4;i++) { double y=8+h*i/4; dc.DrawLine(new Pen(B("#29333C"),1),new Point(35,y),new Point(w+35,y)); Text(dc,(max*(1-i/4.0)).ToString("0"),0,y-7,B("#748696"),10); }
            for(int i=0;i<=4;i++) { var t=Start.AddTicks((End-Start).Ticks*i/4).ToLocalTime(); string fmt=(End-Start).TotalHours>24 ? "ddd HH:mm" : "HH:mm"; Text(dc,t.ToString(fmt),35+w*i/4-(i==4?40:0),h+17,B("#748696"),10); }
            DrawSeries(dc,r=>r.CpuTemp,B("#66E1B7"),w,h,max); DrawSeries(dc,r=>r.GpuTemp,B("#ABAEFF"),w,h,max);
            if(!Rows.Any(r=>r.CpuTemp.HasValue||r.GpuTemp.HasValue)) { Text(dc,"Waiting for temperature readings",Math.Max(50,w/2-95),h/2-6,B("#A1B0BC"),14); Text(dc,"History starts when a temperature sensor becomes available.",Math.Max(50,w/2-163),h/2+20,B("#738696"),11); }
        }
        void DrawSeries(DrawingContext dc,Func<Reading,double?> selector,Brush color,double w,double h,double max) {
            Point? previous=null; DateTime last=DateTime.MinValue;
            // Break the line when the app was not recording; never bridge sleep or shutdown gaps.
            foreach(var r in Rows) {
                var v=selector(r); if(!v.HasValue) { previous=null;continue; }
                double x=35+(r.Time-Start).TotalSeconds/Math.Max(1,(End-Start).TotalSeconds)*w;
                var p=new Point(Math.Max(35,Math.Min(w+35,x)),8+h*(1-Math.Min(max,Math.Max(0,v.Value))/max));
                if(previous.HasValue && (r.Time-last).TotalSeconds<=125) dc.DrawLine(new Pen(color,1.8),previous.Value,p);
                else dc.DrawEllipse(color,null,p,2.5,2.5);
                previous=p; last=r.Time;
            }
        }
        void Hover(object sender,MouseEventArgs e) {
            if(Rows.Count==0)return; double f=Math.Max(0,Math.Min(1,(e.GetPosition(this).X-35)/Math.Max(1,ActualWidth-48)));
            var time=Start.AddTicks((long)((End-Start).Ticks*f)); var r=Rows.OrderBy(x=>Math.Abs((x.Time-time).TotalSeconds)).First();
            if(Math.Abs((r.Time-time).TotalSeconds)>Math.Max(120,(End-Start).TotalSeconds/80)) { ToolTip="No readings at this time"; return; }
            ToolTip=r.Time.ToLocalTime().ToString("ddd, MMM d · HH:mm")+"\nCPU average "+F(r.CpuTemp)+"  ·  peak "+F(r.CpuPeak)+"\nGPU average "+F(r.GpuTemp)+"  ·  peak "+F(r.GpuPeak);
        }
        static string F(double? n) { return n.HasValue?n.Value.ToString("0.#")+" °C":"unavailable"; }
    }
}

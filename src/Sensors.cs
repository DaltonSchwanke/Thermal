using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using LibreHardwareMonitor.Hardware;
namespace Thermal {
    public class SensorService : IDisposable {
        readonly Computer computer=new Computer { IsCpuEnabled=true, IsGpuEnabled=true, IsMemoryEnabled=true, IsMotherboardEnabled=true, IsStorageEnabled=true, IsControllerEnabled=true };
        PerformanceCounter cpu;
        string initError;
        bool opened;
        [StructLayout(LayoutKind.Sequential)] class MemoryStatus { public uint Length=(uint)Marshal.SizeOf(typeof(MemoryStatus)); public uint Load; public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, Extended; }
        [DllImport("kernel32.dll",SetLastError=true)] static extern bool GlobalMemoryStatusEx([In,Out] MemoryStatus status);
        public void Open() {
            try { cpu=new PerformanceCounter("Processor","% Processor Time","_Total"); cpu.NextValue(); } catch { }
            try { computer.Open(); opened=true; } catch(Exception ex) { initError=ex.Message; }
        }
        static double? Valid(float? v) { return v.HasValue && !float.IsNaN(v.Value) && !float.IsInfinity(v.Value) ? (double?)v.Value : null; }
        void Visit(IHardware hw,List<SensorRow> rows,List<string> errors) {
            try { hw.Update(); foreach(var s in hw.Sensors) { var v=Valid(s.Value); if(s.SensorType==SensorType.Temperature) v=Temperature(v); if(hw.HardwareType==HardwareType.Cpu && s.SensorType==SensorType.Power && v==0) v=null; if(v.HasValue) rows.Add(new SensorRow { Hardware=hw.Name, Name=s.Name, Kind=hw.HardwareType+"/"+s.SensorType, Value=v, Display=v.Value.ToString("0.#")+Unit(s.SensorType) }); } }
            catch(Exception ex) { errors.Add(hw.Name+": "+ex.Message); }
            foreach(var child in hw.SubHardware) Visit(child,rows,errors);
        }
        public static double? Temperature(double? v) { return v.HasValue && v>0 && v<=150 && !double.IsNaN(v.Value) ? v : null; }
        static string Unit(SensorType t) { switch(t) { case SensorType.Temperature:return " °C"; case SensorType.Load:case SensorType.Control:return " %"; case SensorType.Fan:return " RPM"; case SensorType.Power:return " W"; case SensorType.Clock:return " MHz"; case SensorType.Voltage:return " V"; case SensorType.Data:return " GB"; case SensorType.SmallData:return " MB"; case SensorType.Throughput:return " B/s"; default:return ""; } }
        public static bool IsCurrentTemperature(string name) { return !new[]{"warning","critical","limit","threshold","tjmax","distance"}.Any(x=>name.IndexOf(x,StringComparison.OrdinalIgnoreCase)>=0); }
        static double? Max(List<SensorRow> rows,string hardware,string kind,string name) { return Aggregation.Max(rows.Where(s=>s.Kind.StartsWith(hardware,StringComparison.OrdinalIgnoreCase) && s.Kind.EndsWith("/"+kind) && (kind!="Temperature" || IsCurrentTemperature(s.Name)) && (name==null || s.Name.IndexOf(name,StringComparison.OrdinalIgnoreCase)>=0)).Select(s=>s.Value)); }
        public Snapshot Read() {
            var rows=new List<SensorRow>(); var errors=new List<string>();
            if(opened) foreach(var hw in computer.Hardware) Visit(hw,rows,errors);
            var r=new Reading { Time=DateTime.UtcNow };
            r.CpuTemp=Max(rows,"Cpu","Temperature","Package") ?? Max(rows,"Cpu","Temperature",null);
            r.GpuTemp=Max(rows,"Gpu","Temperature","Core") ?? Max(rows,"Gpu","Temperature",null);
            r.CpuLoad=Max(rows,"Cpu","Load","Total");
            if(!r.CpuLoad.HasValue && cpu!=null) { try { r.CpuLoad=Math.Max(0,Math.Min(100,cpu.NextValue())); } catch { } }
            r.GpuLoad=Max(rows,"Gpu","Load","Core"); r.DiskTemp=Max(rows,"Storage","Temperature",null);
            r.CpuPower=Max(rows,"Cpu","Power","Package"); r.Fan=Aggregation.Max(rows.Where(s=>s.Kind.EndsWith("/Fan")).Select(s=>s.Value)); r.CpuPeak=r.CpuTemp; r.GpuPeak=r.GpuTemp;
            var mem=new MemoryStatus(); bool ok=GlobalMemoryStatusEx(mem);
            if(ok) r.Memory=100.0*(mem.TotalPhysical-mem.AvailablePhysical)/mem.TotalPhysical;
            var snapshot=new Snapshot { Reading=r, Sensors=rows, CpuName=opened ? computer.Hardware.Where(h=>h.HardwareType==HardwareType.Cpu).Select(h=>h.Name).FirstOrDefault() : null, GpuName=opened ? string.Join(" + ",computer.Hardware.Where(h=>h.HardwareType.ToString().StartsWith("Gpu")).Select(h=>h.Name)) : null, MemoryTotal=ok ? mem.TotalPhysical/1073741824.0 : 0, Admin=new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator), Error=initError ?? (errors.Count>0 ? string.Join("; ",errors.Take(2)) : null) };
            try { var d=new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)); snapshot.Disk=d.Name+"  ·  "+(d.AvailableFreeSpace/1073741824.0).ToString("0")+" GB free of "+(d.TotalSize/1073741824.0).ToString("0")+" GB"; snapshot.DiskUsed=100.0*(d.TotalSize-d.AvailableFreeSpace)/d.TotalSize; } catch { snapshot.Disk="Storage capacity unavailable"; }
            return snapshot;
        }
        public void Dispose() { if(cpu!=null) cpu.Dispose(); try { computer.Close(); } catch { } }
    }
}

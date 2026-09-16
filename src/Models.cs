using System;
using System.Collections.Generic;
using System.Linq;
namespace Thermal {
    public class Reading {
        public DateTime Time { get; set; }
        public double? CpuTemp { get; set; }
        public double? GpuTemp { get; set; }
        public double? CpuLoad { get; set; }
        public double? GpuLoad { get; set; }
        public double? Memory { get; set; }
        public double? DiskTemp { get; set; }
        public double? CpuPower { get; set; }
        public double? Fan { get; set; }
        public double? CpuPeak { get; set; }
        public double? GpuPeak { get; set; }
    }
    public class SensorRow {
        public string Hardware { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        public double? Value { get; set; }
        public string Display { get; set; }
    }
    public class Snapshot {
        public Reading Reading { get; set; }
        public List<SensorRow> Sensors { get; set; }
        public string CpuName { get; set; }
        public string GpuName { get; set; }
        public string Error { get; set; }
        public string Disk { get; set; }
        public double DiskUsed { get; set; }
        public double MemoryTotal { get; set; }
        public bool Admin { get; set; }
    }
    public class Preferences {
        public bool StartWithWindows { get; set; }
        public bool StartCompact { get; set; }
        public double CpuWarning { get; set; }
        public double GpuWarning { get; set; }
        public string Corner { get; set; }
        public Preferences() { CpuWarning=85; GpuWarning=85; Corner="Bottom right"; }
    }
    public static class Aggregation {
        public static double? Mean(IEnumerable<double?> values) { var v=values.Where(x=>x.HasValue).Select(x=>x.Value).ToArray(); return v.Length==0 ? (double?)null : v.Average(); }
        public static double? Max(IEnumerable<double?> values) { var v=values.Where(x=>x.HasValue).Select(x=>x.Value).ToArray(); return v.Length==0 ? (double?)null : v.Max(); }
        public static Reading Summarize(IEnumerable<Reading> input) {
            var r=input.ToArray();
            return new Reading { Time=r[0].Time, CpuTemp=Mean(r.Select(x=>x.CpuTemp)), GpuTemp=Mean(r.Select(x=>x.GpuTemp)), CpuLoad=Mean(r.Select(x=>x.CpuLoad)), GpuLoad=Mean(r.Select(x=>x.GpuLoad)), Memory=Mean(r.Select(x=>x.Memory)), DiskTemp=Mean(r.Select(x=>x.DiskTemp)), CpuPower=Mean(r.Select(x=>x.CpuPower)), Fan=Mean(r.Select(x=>x.Fan)), CpuPeak=Max(r.Select(x=>x.CpuPeak ?? x.CpuTemp)), GpuPeak=Max(r.Select(x=>x.GpuPeak ?? x.GpuTemp)) };
        }
    }
}

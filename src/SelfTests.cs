using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Web.Script.Serialization;
namespace Thermal {
 public static class SelfTests {
  static void Assert(bool condition,string message) { if(!condition)throw new Exception(message); }
  public static int Run() {
   string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test-data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
   try {
    var now=DateTime.UtcNow;
    var input=new List<Reading> { new Reading { Time=now.AddDays(-8),CpuTemp=99 }, new Reading { Time=now.AddDays(-6),CpuTemp=44 }, new Reading { Time=now.AddDays(1),CpuTemp=77 } };
    File.WriteAllText(Path.Combine(folder,"history.json"),new JavaScriptSerializer().Serialize(input));
    var store=new HistoryStore(folder);Assert(store.Get(now.AddDays(-10)).Count==1,"Retention removes expired/future samples at startup");
    var bucket=new DateTime(now.Year,now.Month,now.Day,now.Hour,now.Minute,0,DateTimeKind.Utc).AddMinutes(-2);
    store.Add(new Reading { Time=bucket,CpuTemp=40,GpuTemp=null });store.Add(new Reading { Time=bucket.AddSeconds(2),CpuTemp=80,GpuTemp=50 });store.Add(new Reading { Time=bucket.AddMinutes(1),CpuTemp=45 });store.Save();
    var rows=store.Get(now.AddMinutes(-5));Assert(rows.Count==2,"One aggregate per minute");Assert(rows[0].CpuTemp==60,"Minute average");Assert(rows[0].CpuPeak==80,"Peak preserved");Assert(rows[0].GpuTemp==50,"Missing sensors not treated as zero");
    var reloaded=new HistoryStore(folder);Assert(reloaded.Get(now.AddDays(-7)).Count==3,"Atomic persistence round trip");Assert(reloaded.Error==null,"No persistence error");
    reloaded.SavePreferences(new Preferences { CpuWarning=91,Corner="Bottom left" });Assert(reloaded.LoadPreferences().CpuWarning==91,"Preferences round trip");
    Assert(Aggregation.Mean(new double?[]{null,null})==null,"Missing series stays missing");
    Assert(SensorService.Temperature(0)==null && SensorService.Temperature(-1)==null && SensorService.Temperature(double.NaN)==null && SensorService.Temperature(151)==null && SensorService.Temperature(62)==62,"Invalid sensor temperatures rejected");
    Assert(!SensorService.IsCurrentTemperature("Warning Temperature") && !SensorService.IsCurrentTemperature("Critical Temperature") && SensorService.IsCurrentTemperature("Composite Temperature"),"SSD limits excluded from current temperature");
    string activationName="Local\\Thermal.ActivationTest."+Guid.NewGuid().ToString("N");
    using(var activated=new System.Threading.ManualResetEvent(false))
    using(var listener=new ActivationSignal(()=>activated.Set(),activationName)) {
      Assert(ActivationSignal.TryActivate(activationName),"Relaunch signal delivered");
      Assert(activated.WaitOne(2000),"Running app receives activation");
    }
    Assert(!ActivationSignal.TryActivate(activationName),"Activation handle is released on shutdown");
    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test.txt"),"PASS: retention, future clock data, minute aggregation, peak preservation, missing values, persistence, preferences, invalid temperatures, SSD limit filtering, relaunch signaling and cleanup.");return 0;
   }catch(Exception ex) { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test.txt"),ex.ToString());return 1; }
  }
  public static int Diagnostic() {
   try { using(var service=new SensorService()) { service.Open();System.Threading.Thread.Sleep(2200);var snap=service.Read();File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"diagnostic.json"),new JavaScriptSerializer().Serialize(snap));return 0; } }
   catch(Exception ex) { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"diagnostic.json"),ex.ToString());return 1; }
  }
 }
}

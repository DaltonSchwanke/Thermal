using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Web.Script.Serialization;
namespace Thermal {
    public class HistoryStore {
        public const int RetentionDays=7;
        public readonly string Folder;
        readonly JavaScriptSerializer json=new JavaScriptSerializer { MaxJsonLength=16000000 };
        readonly List<Reading> rows=new List<Reading>();
        readonly List<Reading> pending=new List<Reading>();
        readonly object gate=new object();
        DateTime lastSave=DateTime.MinValue;
        public string Error { get; private set; }
        public HistoryStore(string folder) {
            Folder=folder;
            try {
                Directory.CreateDirectory(folder);
                var path=Path.Combine(folder,"history.json");
                if(File.Exists(path)) rows.AddRange(json.Deserialize<List<Reading>>(File.ReadAllText(path)) ?? new List<Reading>());
                Prune(DateTime.UtcNow); Save();
            } catch(Exception ex) { Error="History could not be loaded: "+ex.Message; }
        }
        void Prune(DateTime now) { rows.RemoveAll(x=>x.Time<now.AddDays(-RetentionDays) || x.Time>now.AddMinutes(2)); }
        public void Add(Reading r) {
            lock(gate) {
                Prune(r.Time);
                if(pending.Count>0 && pending[0].Time.Ticks/TimeSpan.TicksPerMinute!=r.Time.Ticks/TimeSpan.TicksPerMinute) { rows.Add(Aggregation.Summarize(pending)); pending.Clear(); }
                pending.Add(r);
                if((r.Time-lastSave).TotalSeconds>=60) Save();
            }
        }
        public List<Reading> Get(DateTime since) { lock(gate) { Prune(DateTime.UtcNow); var result=rows.Where(x=>x.Time>=since).ToList(); if(pending.Count>0 && pending[0].Time>=since) result.Add(Aggregation.Summarize(pending)); return result.OrderBy(x=>x.Time).ToList(); } }
        public void Save() {
            lock(gate) {
                try {
                    Prune(DateTime.UtcNow);
                    var output=rows.ToList(); if(pending.Count>0) output.Add(Aggregation.Summarize(pending));
                    var path=Path.Combine(Folder,"history.json"); var temp=path+".tmp";
                    File.WriteAllText(temp,json.Serialize(output));
                    if(File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path);
                    lastSave=DateTime.UtcNow; Error=null;
                } catch(Exception ex) { Error="History is not being saved: "+ex.Message; }
            }
        }
        public Preferences LoadPreferences() { try { return json.Deserialize<Preferences>(File.ReadAllText(Path.Combine(Folder,"settings.json"))) ?? new Preferences(); } catch { return new Preferences(); } }
        public void SavePreferences(Preferences prefs) { lock(gate) { File.WriteAllText(Path.Combine(Folder,"settings.json"),json.Serialize(prefs)); } }
    }
}

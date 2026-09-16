using System;
using System.Threading;

namespace Thermal {
    public sealed class ActivationSignal : IDisposable {
        readonly EventWaitHandle signal;
        readonly RegisteredWaitHandle registration;
        public static string Name { get { return "Local\\Thermal.Activate."+Environment.UserName; } }
        public ActivationSignal(Action activate) : this(activate,Name) { }
        internal ActivationSignal(Action activate,string name) {
            signal=new EventWaitHandle(false,EventResetMode.AutoReset,name);
            registration=ThreadPool.RegisterWaitForSingleObject(signal,(state,timedOut)=>activate(),null,Timeout.Infinite,false);
        }
        public static bool TryActivate() { return TryActivate(Name); }
        internal static bool TryActivate(string name) {
            try { using(var existing=EventWaitHandle.OpenExisting(name)) { existing.Set();return true; } }
            catch(WaitHandleCannotBeOpenedException) { return false; }
            catch(UnauthorizedAccessException) { return false; }
        }
        public void Dispose() { registration.Unregister(null);signal.Dispose(); }
    }
}

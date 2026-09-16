using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

// Small elevated entry point for shortcuts and the portable package.
// Thermal.exe remains available for non-administrator, limited-sensor operation.
internal static class Launcher {
    [STAThread] static int Main() {
        string folder=AppDomain.CurrentDomain.BaseDirectory;
        try {
            try {
                using(var signal=EventWaitHandle.OpenExisting("Local\\Thermal.Activate."+Environment.UserName)) {
                    signal.Set(); return 0;
                }
            } catch(WaitHandleCannotBeOpenedException) { }
            bool installed=false;
            using(var key=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO")) {
                Version version;
                installed=key!=null && Version.TryParse(key.GetValue("DisplayVersion") as string,out version) && version>=new Version(2,0);
            }
            if(!installed && MessageBox.Show("Thermal needs the signed PawnIO driver to read CPU and motherboard temperatures.\n\nInstall the bundled sensor driver now? Choose No to continue with limited readings.","Set up Thermal",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes) {
                string setup=Path.Combine(folder,"PawnIO_setup.exe");
                using(var stream=File.OpenRead(setup)) using(var sha=SHA256.Create()) {
                    string hash=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");
                    if(hash!="A3A46226C5E2824F4CDD42BE0EECBABFC672C86F7889710F5AB1E6AD385B47A0")throw new Exception("The sensor installer could not be verified. Please download a fresh Thermal package.");
                }
                using(var installer=Process.Start(new ProcessStartInfo(setup,"-install") { UseShellExecute=true,WorkingDirectory=folder })) {
                    installer.WaitForExit();
                    if(installer.ExitCode!=0)MessageBox.Show("Sensor setup did not finish. Thermal will still open; you can retry sensor setup in Settings.","Thermal");
                }
            }
            Process.Start(new ProcessStartInfo(Path.Combine(folder,"Thermal.exe")) { UseShellExecute=true,WorkingDirectory=folder });
            return 0;
        } catch(Exception ex) { MessageBox.Show("Thermal could not start.\n\n"+ex.Message,"Thermal",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1; }
    }
}

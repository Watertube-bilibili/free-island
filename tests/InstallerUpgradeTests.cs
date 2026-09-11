using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using FreeIsland.Installation;

internal static class InstallerUpgradeTests
{
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS: " + name); }
    private static byte[] Bytes(string s) { return Encoding.UTF8.GetBytes(s); }
    private static Dictionary<string, byte[]> Payload() {
        return new Dictionary<string, byte[]> { { "FreeIsland.exe", Bytes("new executable") }, { "FreeIsland.exe.config", Bytes("new config") }, { "FreeIsland.install", Bytes(Common.MarkerContents) } };
    }
    private static void Seed(string root) {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root,"FreeIsland.exe"),"old executable");
        File.WriteAllText(Path.Combine(root,"FreeIsland.exe.config"),"old config");
        File.WriteAllText(Path.Combine(root,"FreeIsland.install"),Common.MarkerContents);
        File.WriteAllText(Path.Combine(root,"user-settings.keep"),"saved scene and reminders");
    }
    private static int Main(string[] args) {
        if(args.Length==2 && args[0]=="--child") {
            using (EventWaitHandle exit = new EventWaitHandle(false,EventResetMode.AutoReset,args[1]+".Exit"))
            using (EventWaitHandle ready = EventWaitHandle.OpenExisting(args[1]+".Ready")) {
                ready.Set(); if(!exit.WaitOne(5000)) return 3; Thread.Sleep(350); return 0;
            }
        }
        string root=Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),"installer-upgrade-"+Guid.NewGuid().ToString("N"));
        try {
            Common.TestInstallPath=root; Common.TestInstancePrefix=@"Local\FreeIsland.InstallerTest."+Guid.NewGuid().ToString("N");
            Common.ReplacementTimeoutMilliseconds=350;
            Seed(root);
            File.SetAttributes(Path.Combine(root,"FreeIsland.exe"),FileAttributes.ReadOnly);
            Common.ReplacePayload(Payload());
            Check(File.ReadAllText(Path.Combine(root,"FreeIsland.exe"))=="new executable","read-only existing executable upgraded");
            Check(File.ReadAllText(Path.Combine(root,"FreeIsland.exe.config"))=="new config","existing config replaced");
            Check(File.ReadAllText(Path.Combine(root,"user-settings.keep"))=="saved scene and reminders","unlisted user data preserved");
            Check(Directory.GetDirectories(root,".upgrade-*").Length==0,"successful transaction removes temporary files");
            Seed(root);
            FileStream locked=null;
            Common.BeforeReplaceForTest=delegate(string path) { if(path.EndsWith(".config")) locked=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read); };
            bool failed=false;
            try { Common.ReplacePayload(Payload()); } catch(IOException) { failed=true; }
            finally { if(locked!=null)locked.Dispose(); Common.BeforeReplaceForTest=null; }
            Check(failed,"persistent lock produces upgrade error");
            Check(File.ReadAllText(Path.Combine(root,"FreeIsland.exe"))=="old executable" && File.ReadAllText(Path.Combine(root,"FreeIsland.exe.config"))=="old config","failure after first replacement rolls back old installation");
            Check(Common.IsInstalled(),"installation marker survives failed upgrade");
            Common.ReplacementTimeoutMilliseconds=2000;
            Thread unlocker=null;
            Common.BeforeReplaceForTest=delegate(string path) {
                if(!path.EndsWith(".config"))return;
                FileStream temporaryLock=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
                unlocker=new Thread(delegate() { Thread.Sleep(250); temporaryLock.Dispose(); }); unlocker.Start();
            };
            Common.ReplacePayload(Payload()); if(unlocker!=null)unlocker.Join(); Common.BeforeReplaceForTest=null;
            Check(File.ReadAllText(Path.Combine(root,"FreeIsland.exe.config"))=="new config","transient file lock retries successfully");
            string executable=Path.Combine(root,"FreeIsland.exe");
            File.Copy(Assembly.GetExecutingAssembly().Location,executable,true);
            File.WriteAllText(executable+".config","<configuration><startup><supportedRuntime version=\"v4.0\" /></startup></configuration>");
            using(EventWaitHandle ready=new EventWaitHandle(false,EventResetMode.AutoReset,Common.TestInstancePrefix+".Ready"))
            using(Process child=Process.Start(new ProcessStartInfo(executable,"--child "+Common.TestInstancePrefix) {UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=root})) {
                Check(ready.WaitOne(4000),"isolated running application created exit event");
                using(Common.HoldAppInstance()) {
                    Common.StopInstalledApp();
                    Check(child.HasExited,"upgrade waits for actual process exit, not only an event or mutex");
                    Common.ReplacePayload(Payload());
                }
            }
            Check(File.ReadAllText(executable)=="new executable","previously running executable replaced");
            Check(Common.StartupTargetsInstall("\""+executable+"\" --silent"),"quoted startup path with spaces is recognized");
            Check(!Common.StartupTargetsInstall("\""+executable+".other\" --silent"),"unrelated startup target remains unowned");
            Console.WriteLine("No installation registry, real startup settings, user app data, or shutdown actions were modified.");
            Console.WriteLine("Isolated artifacts: "+root); return 0;
        } catch(Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}

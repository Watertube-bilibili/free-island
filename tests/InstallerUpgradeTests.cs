using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
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
    private static void AttributePermissionRegression(string root) {
        string file=Path.Combine(root,"FreeIsland.exe");
        FileSecurity original=File.GetAccessControl(file,AccessControlSections.Access);
        SecurityIdentifier user=WindowsIdentity.GetCurrent().User;
        FileSystemAccessRule deny=new FileSystemAccessRule(user,FileSystemRights.WriteAttributes,AccessControlType.Deny);
        FileSecurity restricted=File.GetAccessControl(file,AccessControlSections.Access);restricted.AddAccessRule(deny);File.SetAccessControl(file,restricted);
        try {
            bool legacyDenied=false;
            try { File.SetAttributes(file,File.GetAttributes(file)&~FileAttributes.ReadOnly); } catch(UnauthorizedAccessException) { legacyDenied=true; }
            Check(legacyDenied,"reproduced 1.0.1 failure: normal file denies unnecessary WRITE_ATTRIBUTES");
            // This directory contains only the fixture. No application is running.
            Common.ReplacePayload(Payload());
            Check(File.ReadAllText(file)=="new executable","upgrade succeeds without WRITE_ATTRIBUTES on normal file");
            Check(Directory.GetDirectories(root,".upgrade-*").Length==0,"attribute restriction leaves no false recovery directory");
            restricted=File.GetAccessControl(file,AccessControlSections.Access);restricted.AddAccessRule(deny);File.SetAccessControl(file,restricted);
            Common.DeletePayloadFile(file);
            Check(!File.Exists(file),"uninstall helper succeeds without WRITE_ATTRIBUTES on normal file");
        } finally {
            if(File.Exists(file))File.SetAccessControl(file,original);
            // Replacement preserves the target DACL on a backup. Restore any retained
            // isolated fixture files too, so failed tests leave no restricted artifacts.
            foreach(string directory in Directory.GetDirectories(root,".upgrade-*"))
                foreach(string retained in Directory.GetFiles(directory))File.SetAccessControl(retained,original);
        }
    }
    private static void DeleteRegression(string root) {
        string file=Path.Combine(root,"delete-readonly.fixture");
        File.WriteAllText(file,"retained until deletion succeeds");
        File.SetAttributes(file,File.GetAttributes(file)|FileAttributes.ReadOnly);
        string failure=null;
        using(FileStream locked=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read)) {
            try { Common.DeletePayloadFile(file); } catch(IOException error) { failure=error.Message; }
            Check(failure!=null && failure.Contains("占用"),"uninstall sharing failure reports a file lock");
            Check((File.GetAttributes(file)&FileAttributes.ReadOnly)!=0,"failed uninstall restores only its changed read-only attribute");
        }
        Common.DeletePayloadFile(file);
        Check(!File.Exists(file),"uninstall deletes read-only file after lock is released");
        Check(Common.AccessFailureMessage(new UnauthorizedAccessException()).Contains("错误 5"),"access denial is distinguished from file sharing");
        Check(Common.AccessFailureMessage(new System.ComponentModel.Win32Exception(32)).Contains("占用"),"native Windows sharing error keeps its actual error code");
    }
    private static void UntouchedFailureRegression(string root) {
        string failure=null;
        Common.BeforeReplaceForTest=delegate(string path) { throw new UnauthorizedAccessException("isolated pre-write rejection"); };
        try { Common.ReplacePayload(Payload()); } catch(IOException error) { failure=error.Message; }
        finally { Common.BeforeReplaceForTest=null; }
        Check(failure!=null && failure.Contains("尚未替换") && !failure.Contains("恢复操作未完成") && !failure.Contains("备份保留"),"pre-write rejection does not invent rollback failure or recovery backups");
        Check(File.ReadAllText(Path.Combine(root,"FreeIsland.exe"))=="old executable" && Directory.GetDirectories(root,".upgrade-*").Length==0,"pre-write rejection preserves original files and removes new staging files");
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
            AttributePermissionRegression(root);
            DeleteRegression(root);
            Seed(root);
            UntouchedFailureRegression(root);
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
            string upgradeFailure=null;
            try { Common.ReplacePayload(Payload()); } catch(IOException error) { failed=true; upgradeFailure=error.Message; }
            finally { if(locked!=null)locked.Dispose(); Common.BeforeReplaceForTest=null; }
            Check(failed,"persistent lock produces upgrade error");
            Check(upgradeFailure.Contains("占用") && !upgradeFailure.Contains("浮岛仍在运行"),"upgrade sharing failure does not falsely identify the app as running");
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

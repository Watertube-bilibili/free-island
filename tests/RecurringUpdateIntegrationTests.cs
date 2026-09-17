using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using FreeIsland;

// Exercises persisted recurring plans across the updater's injected exit/restart.
// No network, actual installer, registry write, or OS shutdown is used.
internal static class RecurringUpdateIntegrationTests
{
    private static int checks;
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("INERT FIXTURE - NOT AN EXECUTABLE");
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); checks++; Console.WriteLine("PASS " + message); }
    private sealed class Transport : IUpdateTransport
    {
        public byte[] Fetch(Uri uri, int maximum, CancellationToken token)
        {
            string hash; using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Payload)).Replace("-", "").ToLowerInvariant();
            return Encoding.UTF8.GetBytes("{\"tag_name\":\"v1.0.8\",\"draft\":false,\"prerelease\":false,\"assets\":[{\"name\":\"FreeIsland-Setup-1.0.8.exe\",\"state\":\"uploaded\",\"size\":" + Payload.Length + ",\"digest\":\"sha256:" + hash + "\",\"browser_download_url\":\"https://github.com/Watertube-bilibili/free-island/releases/download/v1.0.8/FreeIsland-Setup-1.0.8.exe\"}]}");
        }
        public void Download(Uri uri, string path, long maximum, Action<long> progress, CancellationToken token)
        { token.ThrowIfCancellationRequested(); File.WriteAllBytes(path, Payload); progress(Payload.Length); }
    }
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1) throw new ArgumentException("Provide a workspace fixture directory.");
            string root = Path.Combine(Path.GetFullPath(args[0]), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            DateTime now = new DateTime(2030, 1, 7, 8, 0, 0, DateTimeKind.Local).ToUniversalTime();
            string state = Path.Combine(root, "state"); int launches = 0, exits = 0, shutdowns = 0;
            DateTime due;
            using (var engine = new CoreEngine(state, false, delegate { return now; }, delegate { shutdowns++; }))
            {
                engine.SetRecurringShutdown(17, 30, 31);
                due = engine.ShutdownAt.Value;
                Check(engine.ShutdownRecurringEnabled && due.DayOfWeek == DayOfWeek.Monday, "Workday plan active before update");
                using (var service = new UpdateService(engine, false, delegate { return false; }, delegate { return @"D:\Fixture Installation"; },
                    delegate(string file, string arguments) { Check(File.ReadAllBytes(file).SequenceEqual(Payload) && arguments.StartsWith("--auto-update "), "Verified inert bytes handed to injected automatic installer"); launches++; return true; },
                    delegate { exits++; engine.Dispose(); }, new Transport(), Path.Combine(root, "cache"), new Version(1, 0, 7, 0), delegate { return now; }, delegate { return false; }, delegate { return true; }))
                {
                    service.CheckAsync(true).GetAwaiter().GetResult(); Check(service.HasDownload, "Fixture update ready");
                    Check(!service.HasActiveWork && !engine.ShutdownBlocksAutoUpdate, "Distant persisted recurring plan allows update");
                    service.Poll(); Check(launches == 1 && exits == 1, "Automatic update exits only after injected launch succeeds");
                }
            }
            now = now.AddMinutes(1);
            using (var engine = new CoreEngine(state, false, delegate { return now; }, delegate { shutdowns++; }))
            {
                Check(engine.ShutdownRecurringEnabled && engine.ShutdownRepeatDays == 31 && engine.ShutdownRepeatHour == 17 && engine.ShutdownRepeatMinute == 30, "All weekday and time preferences survive update restart");
                Check(engine.ShutdownAt == due, "Restart preserves the upcoming occurrence");
                now = due.ToUniversalTime().AddMinutes(-5);
                Check(engine.ShutdownBlocksAutoUpdate, "Exactly five minutes before shutdown blocks update");
                using (var service = new UpdateService(engine, false, delegate { return false; }, delegate { return @"D:\Fixture Installation"; }, delegate { launches++; return true; }, delegate { exits++; }, new Transport(), Path.Combine(root, "near-cache"), new Version(1, 0, 7, 0), delegate { return now; }, delegate { return false; }, delegate { return true; }))
                {
                    service.CheckAsync(true).GetAwaiter().GetResult(); service.Poll(); service.InstallManually();
                    Check(service.HasActiveWork && launches == 1 && exits == 1, "Near recurring deadline blocks automatic and manual update launch");
                }
                now = due.ToUniversalTime().AddMinutes(1); engine.Tick();
                Check(shutdowns == 0 && engine.ShutdownAt > due, "Missed occurrence rolls forward without issuing shutdown");
                engine.CancelShutdown(); Check(!engine.ShutdownRecurringEnabled && !engine.ShutdownAt.HasValue, "Cancel disables all future repeated shutdowns");
                engine.ScheduleShutdown(now.ToLocalTime().AddDays(2));
                Check(engine.ShutdownBlocksAutoUpdate, "One-shot reservation still blocks update even when distant");
            }
            using (var engine = new CoreEngine(state, false, delegate { return now; }, delegate { shutdowns++; }))
            { Check(!engine.ShutdownRecurringEnabled && !engine.ShutdownAt.HasValue, "One-shot plans remain unarmed after restart"); }
            Check(shutdowns == 0, "No OS or injected shutdown action was executed");
            Console.WriteLine("PASS: " + checks + " recurring/update integration checks. Only fixture files and injected transport/process callbacks were used.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FreeIsland;

internal static class UpdateTests
{
    private static int checks;
    private static string root;
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("TEST DATA ONLY - THIS IS NOT AN EXECUTABLE");
    private static string Hash(byte[] value) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(value)).Replace("-", "").ToLowerInvariant(); }
    private static void Check(bool value, string text) { if (!value) throw new Exception(text); checks++; }
    private static void Reject(Action action, string text) { bool rejected = false; try { action(); } catch { rejected = true; } Check(rejected, text); }
    private static GitHubRelease Release(string version = "1.0.8", bool digest = true)
    {
        string setup = "FreeIsland-Setup-" + version + ".exe", sums = "SHA256SUMS-" + version + ".txt", basis = UpdateRules.Repository + "/releases/download/v" + version + "/";
        return new GitHubRelease { Tag = "v" + version, Draft = false, Prerelease = false, Assets = new System.Collections.Generic.List<GitHubAsset> {
            new GitHubAsset { Name = setup, Url = basis + setup, State = "uploaded", Size = Payload.Length, Digest = digest ? "sha256:" + Hash(Payload) : null },
            new GitHubAsset { Name = sums, Url = basis + sums, State = "uploaded", Size = 100 }
        } };
    }
    private static byte[] Json(GitHubRelease release) { using (var memory = new MemoryStream()) { new DataContractJsonSerializer(typeof(GitHubRelease)).WriteObject(memory, release); return memory.ToArray(); } }
    private sealed class FakeTransport : IUpdateTransport
    {
        public GitHubRelease Release = UpdateTests.Release(); public byte[] Bytes = Payload; public int Fetches, Downloads; public bool Block;
        public byte[] Fetch(Uri uri, int maximum, CancellationToken token)
        {
            Fetches++; token.ThrowIfCancellationRequested();
            if (uri.AbsoluteUri == UpdateRules.LatestApi) return Json(Release);
            return Encoding.UTF8.GetBytes(Hash(Payload) + "  " + Release.Assets[0].Name + "\r\n");
        }
        public void Download(Uri uri, string path, long maximum, Action<long> progress, CancellationToken token)
        {
            Downloads++;
            if (Block) { token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested(); }
            File.WriteAllBytes(path, Bytes); progress(Bytes.Length);
        }
    }
    private sealed class Fixture : IDisposable
    {
        public CoreEngine Engine; public FakeTransport Transport = new FakeTransport(); public UpdateService Service; public string Cache;
        public DateTime Time = new DateTime(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc); public bool Visible = true, Portable, Elevated, LaunchWorks = true; public int Launches, Exits; public string Arguments;
        public Fixture(bool disabled = false)
        {
            string path = Path.Combine(root, Guid.NewGuid().ToString("N")); Cache = Path.Combine(path, "updates"); Engine = new CoreEngine(Path.Combine(path, "state"), true, delegate { return Time; }, delegate { throw new Exception("No real shutdown"); });
            Service = new UpdateService(Engine, disabled, delegate { return Visible; }, delegate { return Portable ? null : @"D:\Fixture Installation"; }, delegate(string file, string arguments) { Check(File.ReadAllBytes(file).SequenceEqual(Payload), "Only verified fixture payload handed to injected launcher"); Launches++; Arguments = arguments; return LaunchWorks; }, delegate { Exits++; }, Transport, Cache, new Version(1, 0, 7, 0), delegate { return Time; }, delegate { return Elevated; }, delegate { return true; });
        }
        public void Download() { Service.CheckAsync(true).GetAwaiter().GetResult(); Check(Service.HasDownload, "Update downloaded and verified"); }
        public void Dispose() { Service.Dispose(); Engine.Dispose(); }
    }
    private static void Parsing()
    {
        foreach (string invalid in new[] { "1.0", "1.0.8-beta", "v01.0.8", "v1.0.8.0", "v1.0.8\n", " v1.0.8", "v9999999999.0.0", "1.0.-1" }) { Version v; Check(!UpdateRules.TryVersion(invalid, out v), "Reject unstable/malformed version " + invalid); }
        Check(UpdateRules.ParseRelease(Json(Release("1.0.7")), new Version(1, 0, 7, 0)) == null, "Never reinstall same version");
        Check(UpdateRules.ParseRelease(Json(Release("1.0.6")), new Version(1, 0, 7, 0)) == null, "Never downgrade");
        var release = Release(); release.Prerelease = true; Check(UpdateRules.ParseRelease(Json(release), new Version(1, 0, 7)) == null, "Skip prerelease"); release.Prerelease = false; release.Draft = true; Check(UpdateRules.ParseRelease(Json(release), new Version(1, 0, 7)) == null, "Skip draft");
        release = Release(); release.Assets[0].Name = "FreeIsland-Win7-Setup-1.0.8.exe"; Reject(delegate { UpdateRules.ParseRelease(Json(release), new Version(1, 0, 7)); }, "Never choose wrong edition");
        foreach (string address in new[] { "http://github.com/Watertube-bilibili/free-island/releases/download/v1.0.8/FreeIsland-Setup-1.0.8.exe", "https://evil.example/FreeIsland-Setup-1.0.8.exe", UpdateRules.Repository + "/releases/download/v1.0.9/FreeIsland-Setup-1.0.8.exe", UpdateRules.Repository + "/releases/download/v1.0.8/FreeIsland-Setup-1.0.8.exe?x=1" }) { release = Release(); release.Assets[0].Url = address; Reject(delegate { UpdateRules.ParseRelease(Json(release), new Version(1, 0, 7)); }, "Reject wrong-origin or mismatched-version asset"); }
        release = Release(); release.Assets.Add(release.Assets[0]); Reject(delegate { UpdateRules.ParseRelease(Json(release), new Version(1, 0, 7)); }, "Reject ambiguous asset");
        release = Release(); release.Assets[0].Size = UpdateRules.MaximumInstallerBytes + 1; Reject(delegate { UpdateRules.ParseRelease(Json(release), new Version(1, 0, 7)); }, "Bound installer size");
        release = Release(); release.Assets[0].Digest = "sha1:" + Hash(Payload); Reject(delegate { UpdateRules.ParseRelease(Json(release), new Version(1, 0, 7)); }, "Reject weaker digest");
        release = Release("1.0.8", false); var parsed = UpdateRules.ParseRelease(Json(release), new Version(1, 0, 7)); Check(parsed.ChecksumUrl != null && parsed.Hash == null, "Allow same-release checksum fallback");
        release.Assets.RemoveAt(1); Reject(delegate { UpdateRules.ParseRelease(Json(release), new Version(1, 0, 7)); }, "No unchecked updates");
        string sum = Hash(Payload) + "  FreeIsland-Setup-1.0.8.exe\n"; Check(UpdateRules.ManifestHash(Encoding.UTF8.GetBytes(sum), "FreeIsland-Setup-1.0.8.exe") == Hash(Payload), "Exact checksum entry");
        Reject(delegate { UpdateRules.ManifestHash(Encoding.UTF8.GetBytes(sum + sum), "FreeIsland-Setup-1.0.8.exe"); }, "Reject duplicate checksum");
        Reject(delegate { UpdateRules.ManifestHash(Encoding.UTF8.GetBytes(sum), "FreeIsland-Setup-1.0.9.exe"); }, "Reject missing checksum");
        Check(UpdateRules.AllowedRedirect(new Uri("https://release-assets.githubusercontent.com/asset?signature=test")), "GitHub signed asset CDN allowed");
        foreach (string uri in new[] { "http://release-assets.githubusercontent.com/a", "https://evil.release-assets.githubusercontent.com/a", "https://github.com.evil.example/a", "https://user@objects.githubusercontent.com/a" }) Check(!UpdateRules.AllowedRedirect(new Uri(uri)), "Reject unsafe redirect");
        Reject(delegate { UpdateRules.ParseRelease(new byte[UpdateRules.MaximumMetadataBytes + 1], new Version(1, 0, 7)); }, "Bound metadata");
    }
    private static void Behavior()
    {
        using (var f = new Fixture())
        {
            f.Download(); f.Service.Poll(); Check(f.Launches == 0, "Visible control center blocks automatic install"); f.Visible = false;
            f.Engine.ToggleStopwatch(); f.Engine.ToggleStopwatch(); f.Service.Poll(); Check(f.Launches == 0, "Paused stopwatch blocks automatic install"); f.Engine.ResetStopwatch();
            f.Engine.StartCountdown(TimeSpan.FromMinutes(2)); f.Engine.PauseResumeCountdown(); f.Service.Poll(); Check(f.Launches == 0, "Paused countdown blocks automatic install"); f.Engine.CancelCountdown();
            f.Engine.ScheduleShutdown(f.Time.ToLocalTime().AddHours(1)); f.Service.Poll(); Check(f.Launches == 0, "Distant shutdown reservation blocks automatic install"); f.Engine.CancelShutdown();
            f.Service.Poll(); Check(f.Launches == 1 && f.Exits == 1 && f.Arguments == "--auto-update \"D:\\Fixture Installation\"", "Idle install uses exact directory and exits only after launch"); f.Service.Poll(); Check(f.Launches == 1, "No repeated installer launches");
        }
        using (var f = new Fixture()) { f.Portable = true; f.Visible = false; f.Download(); f.Service.Poll(); Check(f.Launches == 0 && f.Service.HasDownload, "Portable never auto-installs"); f.Service.InstallManually(); Check(f.Launches == 1 && f.Exits == 0 && f.Arguments == "", "Portable explicit action opens installer wizard"); }
        using (var f = new Fixture()) { f.Elevated = true; f.Visible = false; f.Download(); f.Service.Poll(); Check(f.Launches == 0, "Elevated app never auto-installs"); f.Service.InstallManually(); Check(f.Launches == 1 && f.Arguments == "" && f.Exits == 0, "Elevated explicit action opens manual installer"); }
        using (var f = new Fixture()) { f.Engine.Settings.AutoUpdate = false; f.Visible = false; f.Download(); f.Service.Poll(); Check(f.Launches == 0, "Automatic preference respected after manual download"); f.Service.InstallManually(); Check(f.Launches == 1 && f.Exits == 1, "Manual install remains available when automatic is off"); }
        using (var f = new Fixture()) { f.Download(); f.Visible = false; DateTime distant = f.Time.ToLocalTime().AddHours(2); f.Engine.SetRecurringShutdown(distant.Hour, distant.Minute, 127); Check(!f.Service.HasActiveWork && f.Engine.ShutdownRecurringEnabled, "Distant recurring shutdown does not block all updates forever"); f.Service.Poll(); Check(f.Launches == 1 && f.Exits == 1, "Idle update allowed with future recurring plan preserved"); }
        using (var f = new Fixture()) { f.Download(); f.Visible = false; DateTime near = f.Time.ToLocalTime().AddMinutes(4); f.Engine.SetRecurringShutdown(near.Hour, near.Minute, 127); f.Service.Poll(); Check(f.Service.HasActiveWork && f.Launches == 0, "Recurring shutdown within five minutes blocks update"); }
        using (var f = new Fixture()) { f.LaunchWorks = false; f.Visible = false; f.Download(); f.Service.Poll(); f.Service.Poll(); Check(f.Launches == 1 && f.Exits == 0 && f.Service.HasDownload, "Launch failure keeps app and download, stops auto retry loop"); }
        using (var f = new Fixture()) { f.Download(); string file = Directory.GetFiles(f.Cache, "*.exe", SearchOption.AllDirectories).Single(); File.WriteAllBytes(file, new byte[Payload.Length]); f.Visible = false; f.Service.Poll(); Check(f.Launches == 0 && f.Exits == 0 && !f.Service.HasDownload, "Revalidate payload before process launch"); }
        using (var f = new Fixture()) { f.Transport.Bytes = new byte[Payload.Length]; f.Service.CheckAsync(true).GetAwaiter().GetResult(); Check(!f.Service.HasDownload && f.Launches == 0 && Directory.GetFiles(f.Cache, "*.part", SearchOption.AllDirectories).Length == 0, "Hash mismatch removes partial data and refuses install"); }
        using (var f = new Fixture()) { f.Transport.Release = Release("1.0.8", false); f.Download(); Check(f.Transport.Fetches == 2, "Checksum fallback fetched from same release"); }
        using (var f = new Fixture()) { f.Download(); f.Service.CheckAsync(true).GetAwaiter().GetResult(); Check(f.Transport.Downloads == 1 && f.Service.HasDownload, "Verified same-version installer reused without repeated download"); f.Transport.Release = Release("1.0.7"); f.Service.CheckAsync(true).GetAwaiter().GetResult(); f.Visible = false; f.Service.Poll(); Check(!f.Service.HasDownload && f.Launches == 0, "Retracted release clears pending install"); }
        using (var f = new Fixture()) { f.Transport.Block = true; Task pending = f.Service.CheckAsync(true); Check(SpinWait.SpinUntil(delegate { return f.Transport.Downloads > 0; }, 3000), "Download reached cancellable transport"); f.Service.Cancel(); pending.GetAwaiter().GetResult(); Check(!f.Service.IsBusy && !f.Service.HasDownload && f.Launches == 0, "Cancellation completes without launching"); }
        using (var f = new Fixture(true)) { f.Time = f.Time.AddDays(1); f.Service.Poll(); f.Service.CheckAsync(true).GetAwaiter().GetResult(); f.Service.InstallManually(); Check(f.Transport.Fetches == 0 && f.Launches == 0, "Safe mode disables network/process for manual and automatic actions"); }
        using (var f = new Fixture())
        {
            f.Service.Poll(); Check(f.Transport.Fetches == 0, "No startup network before 30-second delay"); f.Time = f.Time.AddSeconds(31); f.Service.Poll(); Check(SpinWait.SpinUntil(delegate { return !f.Service.IsBusy; }, 3000), "Scheduled update finishes"); Check(f.Transport.Fetches == 1, "First delayed check"); f.Time = f.Time.AddHours(5); f.Service.Poll(); Check(f.Transport.Fetches == 1, "No overly frequent recheck"); f.Time = f.Time.AddHours(2); f.Service.Poll(); Check(SpinWait.SpinUntil(delegate { return !f.Service.IsBusy; }, 3000) && f.Transport.Fetches == 2, "Six-hour schedule checks again");
        }
        using (var f = new Fixture()) { f.Service.ReadInstallResultLines(new[] { "failed", "1.0.8.0", "2030-01-01" }); f.Visible = false; f.Time = f.Time.AddSeconds(31); f.Service.Poll(); Check(SpinWait.SpinUntil(delegate { return !f.Service.IsBusy; }, 3000), "Failed-version check finishes"); Check(f.Transport.Downloads == 0 && f.Launches == 0, "Previous installer failure blocks automatic repeat"); f.Download(); f.Service.Poll(); Check(f.Launches == 0, "Manual redownload does not remove failure cooldown"); f.Service.InstallManually(); Check(f.Launches == 1, "Failed version can be retried explicitly"); Check(f.Arguments == "" && f.Exits == 0, "Failed-version manual retry opens full installer wizard and keeps app alive for admin-retry flow"); }
        string owned = Path.Combine(root, "owned-install"); Directory.CreateDirectory(owned); File.WriteAllBytes(Path.Combine(owned, "FreeIsland.exe"), Payload); Check(!UpdateRules.IsPlainOwnedInstallation(owned), "Unmarked portable directory not treated as owned installation"); File.WriteAllText(Path.Combine(owned, "FreeIsland.install"), "FreeIsland per-user installation v1"); Check(UpdateRules.IsPlainOwnedInstallation(owned), "Plain marker-backed installation detected without registry changes");
    }
    private static int Main(string[] args)
    {
        root = args.Length > 0 ? args[0] : Path.Combine(Environment.CurrentDirectory, "artifacts", "update-tests"); Directory.CreateDirectory(root);
        try { Parsing(); Behavior(); File.WriteAllText(Path.Combine(root, "result.txt"), "PASS " + checks + " updater checks; injected network and launcher only, no live network, installs, shutdown or startup changes."); return 0; }
        catch (Exception error) { File.WriteAllText(Path.Combine(root, "result.txt"), error.ToString()); return 1; }
    }
}

using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Threading;
using EqlTracker.Core;
using Microsoft.Win32;

namespace EqlTracker.App;

internal enum ApplicationUpdateState { Idle, Checking, Available, Downloading, Ready, Applying, Error }
internal sealed class ApplicationUpdates : IDisposable
{
    private readonly string root, cache, current;
    private readonly bool networking;
    private readonly Dispatcher dispatcher;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMinutes(15) };
    private readonly HttpClient http;
    private readonly FileSystemWatcher? preferenceWatcher;
    private CancellationTokenSource? work;
    private bool automaticWork;
    private UpdateManifest? manifest;
    private Release? release;
    private string job = "", payload = "", installer = "";
    internal UpdatePreferences Preferences { get; }
    internal ApplicationUpdateState State { get; private set; }
    internal string Status { get; private set; } = "Automatic updates are off. You can check manually.";
    internal string AvailableVersion => manifest?.Version ?? "";
    internal string ReleaseUrl => manifest == null ? "https://github.com/" + UpdateProtocol.Repository + "/releases" : "https://github.com/" + UpdateProtocol.Repository + "/releases/tag/" + manifest.Tag;
    internal event Action? Changed;
    internal void ReviewState(ApplicationUpdateState state)
    {
        if(networking)throw new InvalidOperationException("Review states require diagnostic/offline mode.");
        manifest=new(1,"EQL Catalyst",UpdateProtocol.Repository,"1.4.1-beta.1","v1.4.1-beta.1",new string('a',40),"win-x64","1.4.0-beta.1",[]);
        Set(state,state==ApplicationUpdateState.Ready?"Beta 1.4.1 is verified and ready. Update and restart when convenient.":"Automatic updates are off. You can check manually.");
    }
    internal ApplicationUpdates(string root, string version, bool networking,HttpMessageHandler? transport=null)
    {
        http=new(transport??new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromMinutes(15)};
        this.root = root; current = version; this.networking = networking; cache = Path.Combine(root, "updates"); dispatcher = Application.Current.Dispatcher;
        Preferences = UpdatePreferences.Read(root); Status=Preferences.Automatic?"Weekly update checks are enabled. You choose when to install and restart.":Status; http.DefaultRequestHeaders.UserAgent.ParseAdd("EQL-Catalyst/" + version.Split('+')[0]);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        timer.Tick += (_, _) => { if (Preferences.Due(DateTimeOffset.UtcNow)) _ = Check(true); };
        if (networking)
        {
            Directory.CreateDirectory(root);preferenceWatcher=new(root,"update-preferences.json"){NotifyFilter=NotifyFilters.FileName|NotifyFilters.LastWrite};
            void RefreshPreferences(object sender,FileSystemEventArgs e){if(!dispatcher.HasShutdownStarted)dispatcher.BeginInvoke(()=>{var saved=UpdatePreferences.Read(root);Preferences.Automatic=saved.Automatic;Preferences.LastChecked=saved.LastChecked;Preferences.LastAttempt=saved.LastAttempt;Preferences.HighestVersion=saved.HighestVersion;if(!saved.Automatic&&automaticWork)work?.Cancel();Changed?.Invoke();});}
            preferenceWatcher.Created+=RefreshPreferences;preferenceWatcher.Changed+=RefreshPreferences;preferenceWatcher.Renamed+=RefreshPreferences;preferenceWatcher.EnableRaisingEvents=true;
            timer.Start(); _ = RestoreReady();
        }
    }
    private void Set(ApplicationUpdateState state, string message)
    { dispatcher.Invoke(() => { State = state; Status = message; Changed?.Invoke(); }); }
    internal void Automatic(bool value)
    {
        Preferences.Automatic = value;
        try{Preferences.Save(root,true);}catch(IOException){Set(ApplicationUpdateState.Error,"Another instance is saving update settings. Try the option again.");return;}
        if (!value && automaticWork) work?.Cancel();
        if(State==ApplicationUpdateState.Idle)Status=value?"Weekly update checks are enabled. You choose when to install and restart.":"Automatic updates are off. You can check manually.";
        Changed?.Invoke(); if (value && Preferences.Due(DateTimeOffset.UtcNow)) _ = Check(true);
    }
    internal void Cancel() => work?.Cancel();
    private async Task RestoreReady()
    {
        try
        {
            string ready = Path.Combine(cache, "ready.json");
            if (File.Exists(ready))
            {
                string? folder = JsonSerializer.Deserialize<string>(await File.ReadAllTextAsync(ready));
                if (folder == null || !Path.GetFullPath(folder).StartsWith(Path.GetFullPath(cache) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid cached update location.");
                var found = UpdateProtocol.Verify(await File.ReadAllBytesAsync(Path.Combine(folder, "update-manifest.json")), Convert.FromBase64String(await File.ReadAllTextAsync(Path.Combine(folder, "update-manifest.sig"))));
                if (UpdateProtocol.Newer(found.Version, current))
                {
                    manifest = found; job = folder;
                    using var p = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(job, "prepared.json")));
                    payload = UpdateFiles.Child(job, p.RootElement.GetProperty("Payload").GetString()!);
                    installer = UpdateFiles.Child(job, manifest.Packages.Single(a => a.Kind == "Installer").Name);
                    await Task.Run(() => VerifyReady()); Set(ApplicationUpdateState.Ready, "Beta " + found.Version + " is ready to install.");
                }
            }
        }
        catch (Exception e) when (Expected(e)) { Set(ApplicationUpdateState.Error, "The previous download needs to be checked again."); }
        if (Preferences.Due(DateTimeOffset.UtcNow)) await Check(true);
    }
    internal async Task Check(bool automatic = false)
    {
        if (!networking) { Set(ApplicationUpdateState.Idle, "Update networking is disabled in offline and diagnostic modes."); return; }
        if (work != null || State == ApplicationUpdateState.Applying) return;
        automaticWork = automatic; work = new(); var cancel = work.Token;
        try
        {
            Directory.CreateDirectory(cache);
            using var checkLease=new FileStream(Path.Combine(cache,"check.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            var saved=UpdatePreferences.Read(root);
            if(automatic&&!saved.Due(DateTimeOffset.UtcNow)){Preferences.Automatic=saved.Automatic;Preferences.LastChecked=saved.LastChecked;Preferences.LastAttempt=saved.LastAttempt;return;}
            Set(ApplicationUpdateState.Checking, "Checking GitHub Releases…"); Preferences.LastAttempt = DateTimeOffset.UtcNow; Preferences.Save(root);
            byte[] data = await Get(new Uri("https://api.github.com/repos/" + UpdateProtocol.Repository + "/releases?per_page=100"), 2 * 1024 * 1024, cancel);
            var releases = JsonSerializer.Deserialize<List<Release>>(data) ?? [];
            Release? found = releases.Where(r => !r.Draft && ValidVersion(r.Tag) && UpdateProtocol.Newer(r.Tag, current)).OrderByDescending(r => UpdateVersion.Parse(r.Tag)).FirstOrDefault();
            if (found == null) { Preferences.LastChecked = DateTimeOffset.UtcNow; Preferences.Save(root); Set(manifest != null && payload.Length > 0 ? ApplicationUpdateState.Ready : ApplicationUpdateState.Idle, "You have the newest compatible release."); return; }
            var metadata = found.Assets.SingleOrDefault(a => a.Name == "update-manifest.json") ?? throw new InvalidDataException("This release has no verified update metadata. Open its release page for manual installation.");
            var signature = found.Assets.SingleOrDefault(a => a.Name == "update-manifest.sig") ?? throw new InvalidDataException("The release signature is missing.");
            byte[] bytes = await Get(AssetUri(metadata, found.Tag), 65536, cancel), signed = await Get(AssetUri(signature, found.Tag), 1024, cancel);
            var verified = UpdateProtocol.Verify(bytes, Convert.FromBase64String(System.Text.Encoding.UTF8.GetString(signed).Trim()));
            if (verified.Tag != found.Tag || UpdateProtocol.Newer(verified.MinimumUpdaterVersion, current) || Preferences.HighestVersion.Length > 0 && UpdateProtocol.Newer(Preferences.HighestVersion, verified.Version)) throw new InvalidDataException("This update needs a newer updater or is older than previously verified metadata.");
            foreach (var package in verified.Packages) { var asset = found.Assets.SingleOrDefault(a => a.Name == package.Name); if (asset == null || asset.Size != package.Bytes) throw new InvalidDataException("Release assets do not match the signed metadata."); }
            manifest = verified; release = found; Preferences.HighestVersion = verified.Version; Preferences.LastChecked = DateTimeOffset.UtcNow; Preferences.Save(root);
            job = Path.Combine(cache, verified.Version + "-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(job);
            await File.WriteAllBytesAsync(Path.Combine(job, "update-manifest.json"), bytes, cancel); await File.WriteAllBytesAsync(Path.Combine(job, "update-manifest.sig"), signed, cancel);
            Set(ApplicationUpdateState.Available, "Beta " + verified.Version + " is available.");
        }
        catch (OperationCanceledException) { Set(ApplicationUpdateState.Idle, "Update check cancelled."); }
        catch (Exception e) when (Expected(e)) { Set(ApplicationUpdateState.Error, Friendly(e)); }
        finally { work.Dispose(); work = null; }
        if (automatic && Preferences.Automatic && State == ApplicationUpdateState.Available) await Download();
    }
    internal async Task Download()
    {
        if (work != null || manifest == null || release == null) return;
        work = new(); var cancel = work.Token;
        try
        {
            Directory.CreateDirectory(cache);
            using var lease = new FileStream(Path.Combine(cache, "download.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if(await ReusePrepared())return;
            Set(ApplicationUpdateState.Downloading, "Downloading the verified update…");
            foreach (var package in manifest.Packages)
            {
                string file = UpdateFiles.Child(job, package.Name), temporary = file + ".part";
                using var response = await Request(AssetUri(release.Assets.Single(a => a.Name == package.Name), manifest.Tag), cancel);
                if (response.Content.Headers.ContentLength is long size && size != package.Bytes) throw new InvalidDataException("Unexpected download size.");
                await using var input = await response.Content.ReadAsStreamAsync(cancel); await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    byte[] buffer = new byte[81920]; long received = 0; var clock = Stopwatch.StartNew();
                    while (true) { int count = await input.ReadAsync(buffer, cancel); if (count == 0) break; received += count; if (received > package.Bytes) throw new InvalidDataException("The download exceeds its signed size."); await output.WriteAsync(buffer.AsMemory(0, count), cancel); if (clock.ElapsedMilliseconds > 300) { Set(ApplicationUpdateState.Downloading, $"Downloading {package.Kind.ToLowerInvariant()}: {received * 100 / package.Bytes}%"); clock.Restart(); } }
                }
                await Task.Run(()=>UpdateProtocol.VerifyFile(temporary, package),cancel); File.Move(temporary, file, true);
            }
            string zip = Path.Combine(job, manifest.Packages.Single(p => p.Kind == "Portable").Name);
            payload = await Task.Run(() => UpdateInstallation.Extract(zip, Path.Combine(job, "extracted-"+Guid.NewGuid().ToString("N"))), cancel);
            using(var metadata=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(payload,"release-manifest.json"),cancel)))
                if(metadata.RootElement.GetProperty("Version").GetString()!=manifest.Version)throw new InvalidDataException("The payload version differs from the signed release metadata.");
            cancel.ThrowIfCancellationRequested();
            installer = Path.Combine(job, manifest.Packages.Single(p => p.Kind == "Installer").Name);
            UpdateFiles.WriteJson(Path.Combine(job, "prepared.json"), new { Payload = Path.GetRelativePath(job, payload).Replace('\\', '/') });
            UpdateFiles.WriteJson(Path.Combine(cache, "ready.json"), job); Set(ApplicationUpdateState.Ready, "Beta " + manifest.Version + " is ready. Update and restart when convenient.");
        }
        catch (OperationCanceledException) { Set(ApplicationUpdateState.Available, "Download cancelled. You can try again."); }
        catch (Exception e) when (Expected(e)) { Set(ApplicationUpdateState.Error, Friendly(e)); }
        finally { work.Dispose(); work = null; automaticWork = false; }
    }
    private void VerifyReady()
    {
        manifest = UpdateProtocol.Verify(File.ReadAllBytes(Path.Combine(job, "update-manifest.json")), Convert.FromBase64String(File.ReadAllText(Path.Combine(job, "update-manifest.sig")).Trim()));
        if (!UpdateProtocol.Newer(manifest.Version, current)) throw new InvalidDataException("This package is not newer than the running app.");
        foreach (var p in manifest.Packages) UpdateProtocol.VerifyFile(Path.Combine(job, p.Name), p);
        UpdateInstallation.VerifyExtracted(Path.Combine(job, manifest.Packages.Single(p => p.Kind == "Portable").Name), payload);
    }
    private async Task<bool> ReusePrepared()
    {
        try
        {
            string ready=Path.Combine(cache,"ready.json");if(!File.Exists(ready))return false;
            string folder=JsonSerializer.Deserialize<string>(await File.ReadAllTextAsync(ready))??"";
            if(!Path.GetFullPath(folder).StartsWith(Path.GetFullPath(cache)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))return false;
            var candidate=UpdateProtocol.Verify(await File.ReadAllBytesAsync(Path.Combine(folder,"update-manifest.json")),Convert.FromBase64String(await File.ReadAllTextAsync(Path.Combine(folder,"update-manifest.sig"))));
            if(candidate.Version!=manifest?.Version)return false;
            using var prepared=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(folder,"prepared.json")));
            string sharedPayload=UpdateFiles.Child(folder,prepared.RootElement.GetProperty("Payload").GetString()!);
            await Task.Run(()=>{foreach(var p in candidate.Packages)UpdateProtocol.VerifyFile(Path.Combine(folder,p.Name),p);UpdateInstallation.VerifyExtracted(Path.Combine(folder,candidate.Packages.Single(p=>p.Kind=="Portable").Name),sharedPayload);});
            job=folder;payload=sharedPayload;installer=Path.Combine(folder,candidate.Packages.Single(p=>p.Kind=="Installer").Name);manifest=candidate;
            Set(ApplicationUpdateState.Ready,"The update prepared by another character instance is ready to install.");return true;
        }
        catch(Exception e)when(Expected(e)){return false;}
    }
    internal async Task Apply()
    {
        if (State != ApplicationUpdateState.Ready) return;
        try
        {
            Set(ApplicationUpdateState.Applying, "Verifying update files before closing the app…"); await Task.Run(VerifyReady);
            var app = (App)Application.Current; var profile = app.Controller.Profile;
            string scope = InstalledScope(AppContext.BaseDirectory);
            var intent = new UpdateIntent(Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar), root, payload, installer, scope, profile.Character, profile.Server, profile.GameFolder, manifest!.Version, job);
            string intentFile = Path.Combine(job, "apply.json"); UpdateFiles.WriteJson(intentFile, intent);
            Process.Start(new ProcessStartInfo(Path.Combine(payload, "EQLUpdater.exe")) { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "--apply", intentFile } });
            Set(ApplicationUpdateState.Ready, "Update helper opened. Finish any unsaved-work prompts to continue.");
        }
        catch (Exception e) when (Expected(e)) { Set(ApplicationUpdateState.Error, Friendly(e)); }
    }
    private static string InstalledScope(string directory)
    {
        const string key = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{A1839104-954F-4E08-9D9E-580C5D944289}_is1";
        foreach (var (hive, scope) in new[] { (RegistryHive.CurrentUser, "CurrentUser"), (RegistryHive.LocalMachine, "AllUsers") })
        { using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64); using var registration = baseKey.OpenSubKey(key); if (registration?.GetValue("InstallLocation") is string location && Path.GetFullPath(location).TrimEnd('\\').Equals(Path.GetFullPath(directory).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return scope; }
        return "Portable";
    }
    private static bool ValidVersion(string text) { try { _ = UpdateVersion.Parse(text); return true; } catch (Exception e) when (e is FormatException or OverflowException or InvalidDataException) { return false; } }
    private static Uri AssetUri(Asset asset, string tag)
    {
        string prefix = "https://github.com/" + UpdateProtocol.Repository + "/releases/download/" + tag + "/";
        if (!asset.Url.StartsWith(prefix, StringComparison.Ordinal) || asset.Url != prefix + asset.Name) throw new InvalidDataException("Unexpected release download URL.");
        return new Uri(asset.Url);
    }
    private async Task<HttpResponseMessage> Request(Uri uri, CancellationToken cancel)
    {
        for (int i = 0; i < 6; i++)
        {
            if (!UpdateProtocol.TrustedUri(uri)) throw new InvalidDataException("The update download left the trusted GitHub hosts.");
            var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancel);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is {} next) { uri = next.IsAbsoluteUri ? next : new Uri(uri, next); response.Dispose(); continue; }
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests) { response.Dispose(); throw new HttpRequestException("GitHub's request limit was reached. Try again later."); }
            try { response.EnsureSuccessStatusCode(); return response; } catch { response.Dispose(); throw; }
        }
        throw new InvalidDataException("Too many download redirects.");
    }
    private async Task<byte[]> Get(Uri uri, int maximum, CancellationToken cancel)
    {
        using var response = await Request(uri, cancel); using var memory = new MemoryStream(); await using var stream = await response.Content.ReadAsStreamAsync(cancel);
        byte[] buffer = new byte[8192]; int count; while ((count = await stream.ReadAsync(buffer, cancel)) > 0) { if (memory.Length + count > maximum) throw new InvalidDataException("Update metadata is too large."); memory.Write(buffer, 0, count); } return memory.ToArray();
    }
    private static bool Expected(Exception e) => e is IOException or UnauthorizedAccessException or HttpRequestException or JsonException or InvalidDataException or System.Security.Cryptography.CryptographicException or FormatException or InvalidOperationException or System.ComponentModel.Win32Exception;
    private static string Friendly(Exception e) => e is HttpRequestException ? "GitHub could not be reached or limited the request. Try again later." : e is IOException ? "Update files are busy or unavailable. Free disk space and close other update operations, then retry." : e.Message;
    public void Dispose() { timer.Stop(); preferenceWatcher?.Dispose();work?.Cancel(); http.Dispose(); }
    private sealed record Asset([property: JsonPropertyName("name")] string Name, [property: JsonPropertyName("size")] long Size, [property: JsonPropertyName("browser_download_url")] string Url);
    private sealed record Release([property: JsonPropertyName("tag_name")] string Tag, [property: JsonPropertyName("draft")] bool Draft, [property: JsonPropertyName("assets")] List<Asset> Assets);
}

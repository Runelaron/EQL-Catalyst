using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using EqlTracker.Core;

namespace EqlTracker.Updater;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        bool offscreen=args.Length==3&&args[2]=="--offscreen";
        if ((args.Length != 2&&!offscreen) || args[0] != "--apply") { MessageBox.Show("Start updates from EQL Catalyst Settings.", "EQL Catalyst"); return; }
        using var progress = new Form { Text = "EQL Catalyst update", Width = 490, Height = 150, StartPosition = FormStartPosition.CenterScreen, MaximizeBox = false, MinimizeBox = false };
        var text = new Label { Dock = DockStyle.Fill, Padding = new Padding(16), Text = "Verifying the update…", AutoSize = false }; progress.Controls.Add(text);
        if(offscreen){progress.StartPosition=FormStartPosition.Manual;progress.Left=progress.Top=-15000;progress.ShowInTaskbar=false;}
        progress.Shown += async (_, _) =>
        {
            progress.ControlBox = false;
            try { await Apply(args[1], value => text.Text = value); }
            catch (Exception error) { if(offscreen)File.WriteAllText(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!,"helper-error.json"),JsonSerializer.Serialize(new{Error=error.ToString()}));else MessageBox.Show(progress, "The update was not completed. Your profiles and recovery copies are retained.\n\n" + error.Message, "EQL Catalyst update", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            finally { progress.Close(); }
        };
        Application.Run(progress);
    }
    private static async Task<string> Command(int pid, string command)
    {
        await using var pipe = new NamedPipeClientStream(".", "EQLCatalyst-update-" + pid, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(5000); using var output = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true }; using var input = new StreamReader(pipe, leaveOpen: true);
        await output.WriteLineAsync(command); return await input.ReadLineAsync().WaitAsync(TimeSpan.FromMinutes(10)) ?? "Disconnected";
    }
    private static async Task Apply(string path, Action<string> status)
    {
        var intent = JsonSerializer.Deserialize<UpdateIntent>(await File.ReadAllTextAsync(path)) ?? throw new InvalidDataException("Missing update request.");
        string job = Path.GetFullPath(intent.JobDirectory), app = Path.GetFullPath(intent.AppDirectory).TrimEnd(Path.DirectorySeparatorChar);
        UpdateFiles.NoLinks(app); UpdateFiles.NoLinks(job);
        UpdateInstallation.Recover(Path.Combine(job,"transaction.json"));
        if (!Path.GetFullPath(path).Equals(Path.Combine(job, "apply.json"), StringComparison.OrdinalIgnoreCase) || !Path.GetFullPath(AppContext.BaseDirectory).StartsWith(job + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The helper is outside the verified update directory.");
        if (!LauncherPreferences.ValidName(intent.Character) || !LauncherPreferences.ValidName(intent.Server) || intent.Scope is not ("Portable" or "CurrentUser" or "AllUsers")) throw new InvalidDataException("Invalid update context.");
        if (!File.Exists(Path.Combine(app, "EQLTracker.exe")) || File.Exists(Path.Combine(app, "eqgame.exe")) || Path.GetFullPath(intent.DataRoot).StartsWith(app + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || app.StartsWith(Path.GetFullPath(intent.DataRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Application and profile directories must be separate.");
        var manifest = UpdateProtocol.Verify(await File.ReadAllBytesAsync(Path.Combine(job, "update-manifest.json")), Convert.FromBase64String(await File.ReadAllTextAsync(Path.Combine(job, "update-manifest.sig"))));
        if (manifest.Version != intent.Version) throw new InvalidDataException("Unexpected update version.");
        foreach (var package in manifest.Packages) UpdateProtocol.VerifyFile(Path.Combine(job, package.Name), package);
        string payload = Path.GetFullPath(intent.PayloadDirectory);
        if (!payload.StartsWith(job + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || intent.Installer != Path.Combine(job, manifest.Packages.Single(p => p.Kind == "Installer").Name)) throw new InvalidDataException("Unexpected update payload paths.");
        await Task.Run(() => UpdateInstallation.VerifyExtracted(Path.Combine(job, manifest.Packages.Single(p => p.Kind == "Portable").Name), payload));
        string leaseKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(app.ToUpperInvariant())));
        using var lease = new FileStream(Path.Combine(Path.GetDirectoryName(job)!, leaseKey + ".apply.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var instances = new List<Process>(); var prepared = new List<int>();var contexts=new List<UpdateRelaunch>(); bool committed = false;
        foreach (var process in Process.GetProcessesByName("EQLTracker"))
        {
            try { if (Path.GetFullPath(process.MainModule!.FileName).Equals(Path.Combine(app, "EQLTracker.exe"), StringComparison.OrdinalIgnoreCase)) instances.Add(process); else process.Dispose(); }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { process.Dispose(); }
        }
        if (instances.Count == 0) throw new InvalidOperationException("The requesting app is no longer running. Reopen it and start the update again.");
        try
        {
            status("Finish any Save / Discard / Cancel prompts in EQL Catalyst. Cancelling keeps the current version.");
            foreach (var process in instances)
            {
                string response=await Command(process.Id,"prepare");
                if(!response.StartsWith("Ready:",StringComparison.Ordinal))throw new OperationCanceledException("Update cancelled. No files were replaced.");
                prepared.Add(process.Id);var context=JsonSerializer.Deserialize<UpdateRelaunch>(response[6..])??throw new InvalidDataException("Missing restart context.");
                if(!LauncherPreferences.ValidName(context.Character)||!LauncherPreferences.ValidName(context.Server))throw new InvalidDataException("Invalid restart profile.");contexts.Add(context);
            }
            foreach (var process in instances) if (await Command(process.Id, "commit") != "Closing") throw new InvalidOperationException("An app instance is not ready to close. No files were replaced.");
            committed = true;
            foreach (var process in instances) await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
            status("Backing up application files and profiles…");
            await Task.Run(() =>
            {
                int index=0;foreach(string root in contexts.Select(c=>c.DataRoot).Distinct(StringComparer.OrdinalIgnoreCase))
                {string profiles=Path.Combine(job,"recovery","profiles-"+index++);if(Directory.Exists(root))UpdateInstallation.CopyTree(root,profiles,p=>!new[]{"updates","cache","caches"}.Contains(Path.GetFileName(p),StringComparer.OrdinalIgnoreCase));}
                if (intent.Scope != "Portable") UpdateInstallation.CopyTree(app, Path.Combine(job, "recovery", "application"));
            });
            status("Installing Beta " + manifest.Version + "…");
            if (intent.Scope == "Portable") await Task.Run(() => UpdateInstallation.ReplacePortable(app, payload, Path.Combine(job, "transaction.json")));
            else
            {
                var setup = new ProcessStartInfo(intent.Installer) { UseShellExecute = true };
                if (intent.Scope == "AllUsers") setup.Verb = "runas";
                setup.Arguments = "/SILENT /SP- /NORESTART /NOCLOSEAPPLICATIONS /NOFORCECLOSEAPPLICATIONS /NORESTARTAPPLICATIONS /" + (intent.Scope == "AllUsers" ? "ALLUSERS" : "CURRENTUSER") + " /DIR=\"" + app + "\"";
                using var process = Process.Start(setup) ?? throw new InvalidOperationException("Setup did not start."); await process.WaitForExitAsync();
                if (process.ExitCode != 0) throw new InvalidOperationException("Setup was cancelled or failed (code " + process.ExitCode + "). Application and profile backups are in the update's recovery folder. Reopen the current app if available, or rerun the verified installer.");
                UpdateFiles.VerifyPayload(app);
            }
            status("Opening the updated app…");
            var receipts=new List<string>();foreach(var context in contexts)
            {
                string receiptPath=Path.Combine(context.DataRoot,"updates","receipts",Guid.NewGuid().ToString("N")+".json");receipts.Add(receiptPath);
                var start=new ProcessStartInfo(Path.Combine(app,"EQLTracker.exe")){UseShellExecute=false,WorkingDirectory=app};
                foreach(string value in new[]{"--character",context.Character,"--server",context.Server,"--data-root",context.DataRoot,"--game-folder",context.GameFolder,"--update-receipt",receiptPath})start.ArgumentList.Add(value);
                Process.Start(start);
            }
            for(int i=0;i<150&&receipts.Any(r=>!File.Exists(r));i++)await Task.Delay(200);
            foreach(string pathReceipt in receipts)
            {
                if(!File.Exists(pathReceipt))throw new InvalidOperationException("Files were installed, but startup has not been confirmed. The backups remain available; open EQL Catalyst normally to review any startup message.");
                using var receipt=JsonDocument.Parse(await File.ReadAllTextAsync(pathReceipt));
                if(receipt.RootElement.GetProperty("Version").GetString()?.Split('+')[0]!=manifest.Version)throw new InvalidDataException("A reopened application reported an unexpected version.");
            }
            UpdateFiles.WriteJson(Path.Combine(job, "completed.json"), new { Version = manifest.Version, Completed = DateTimeOffset.UtcNow, Scope = intent.Scope });
        }
        catch (Exception e) when (e is IOException or TimeoutException or System.ComponentModel.Win32Exception)
        { throw new InvalidOperationException("Close other copies using this application folder, check disk space and permissions, then retry. No process was force-closed. " + (committed ? "Reopen EQL Catalyst if it has closed. Recovery files are retained." : "No files were replaced."), e); }
        finally
        {
            foreach (var instance in instances.Where(p=>prepared.Contains(p.Id)&&!p.HasExited)) { try { await Command(instance.Id, "resume"); } catch (Exception e) when (e is IOException or TimeoutException) { /* The instance has already closed. */ } }
            foreach (var process in instances) process.Dispose();
        }
    }
}

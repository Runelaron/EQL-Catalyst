using System.IO.Pipes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using EqlTracker.Core;
namespace EqlTracker.App;

public sealed partial class App
{
    internal ApplicationUpdates Updates { get; private set; } = null!;
    private readonly CancellationTokenSource updatePipeCancel = new();
    private bool updatePrepared;
    internal void OpenUpdates() { OpenSettings(); settings?.SelectSection(SettingsSection.Sync); }
    private async Task UpdateCommands()
    {
        while (!updatePipeCancel.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream("EQLCatalyst-update-" + Environment.ProcessId, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(updatePipeCancel.Token);
                using var input = new StreamReader(pipe, leaveOpen: true); using var output = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                string? command = await input.ReadLineAsync(updatePipeCancel.Token); string result = "Unknown";
                await Dispatcher.InvokeAsync(() =>
                {
                    if (command == "prepare")
                    {
                        updatePrepared = CanChangeEquipmentContext();
                        if (updatePrepared)
                        {
                            foreach (Window window in Windows) window.IsEnabled = false;
                            _ = Task.Delay(TimeSpan.FromMinutes(2)).ContinueWith(_ => Dispatcher.InvokeAsync(() => { if (updatePrepared && !Exiting) { updatePrepared=false; foreach(Window window in Windows)window.IsEnabled=true; } }));
                        }
                        result = updatePrepared ? "Ready:"+System.Text.Json.JsonSerializer.Serialize(new UpdateRelaunch(DataRoot,Controller.Profile.Character,Controller.Profile.Server,Controller.Profile.GameFolder)) : "Cancelled";
                    }
                    else if (command == "resume") { updatePrepared = false; foreach (Window window in Windows) window.IsEnabled = true; result = "Resumed"; }
                    else if (command == "commit" && updatePrepared) result = "Closing";
                });
                await output.WriteLineAsync(result);
                if (result == "Closing") await Dispatcher.InvokeAsync(ExitTracker);
            }
            catch (OperationCanceledException) { return; }
            catch (IOException) { /* Disconnected helpers can retry; never force shutdown. */ }
        }
    }
}

public sealed partial class SettingsWindow
{
    private UIElement BuildUpdateSettings()
    {
        var updates = ((App)Application.Current).Updates;
        var automatic = new CheckBox { Content = "Automatically check weekly and download updates", IsChecked = updates.Preferences.Automatic, ToolTip = "Opt in for all characters. Downloads prepare an update; you choose when to install and restart." };
        var text = UI.Text("", 12, "Muted"); text.TextWrapping = TextWrapping.Wrap;
        var check = UI.Button("Check for updates", () => _ = updates.Check()); check.ToolTip = "Check GitHub Releases now";
        var download = UI.Button("Download update", () => _ = updates.Download()); download.ToolTip = "Download and verify the available release";
        var apply = UI.Button("Update and restart", () => _ = updates.Apply(), true); apply.ToolTip = "Save your work, install the verified update and reopen this character";
        var cancel = UI.Button("Cancel download", updates.Cancel); cancel.ToolTip = "Stop this download";
        var notes = UI.Button("Release notes", () => UI.OpenUrl(updates.ReleaseUrl)); notes.ToolTip = "Open the release notes on GitHub";
        void Refresh()
        {
            automatic.IsChecked=updates.Preferences.Automatic;
            bool busy = updates.State is ApplicationUpdateState.Checking or ApplicationUpdateState.Downloading or ApplicationUpdateState.Applying;
            text.Text = updates.Status + (updates.Preferences.LastChecked is {} last ? "\nLast checked: " + last.ToLocalTime().ToString("MMM d, yyyy HH:mm") : "");
            check.IsEnabled = !busy; download.Visibility = updates.State == ApplicationUpdateState.Available ? Visibility.Visible : Visibility.Collapsed;
            apply.Visibility = updates.State == ApplicationUpdateState.Ready ? Visibility.Visible : Visibility.Collapsed;
            cancel.Visibility = updates.State == ApplicationUpdateState.Downloading ? Visibility.Visible : Visibility.Collapsed;
        }
        automatic.Click += (_, _) => updates.Automatic(automatic.IsChecked == true); updates.Changed += Refresh; Closed += (_, _) => updates.Changed -= Refresh; Refresh();
        return Group("Application updates · all characters", UI.Text(ReleaseInfo.Label, 13, "Accent", true), automatic, text, Flow(check, download, apply, cancel, notes));
    }
}

public sealed partial class QuestWindow
{
    internal static Button UpdateReadyButton()
    {
        var app = (App)Application.Current; var button = UI.Button("↻", app.OpenUpdates);
        button.Width = button.Height = 28; button.Margin = new(5, 5, 0, 0); button.Padding = new(0); button.FontSize = 19;
        button.SetResourceReference(Control.ForegroundProperty, "Gold"); button.Tag = "tracker-update-ready";
        void Refresh() { button.Visibility = app.Updates.State == ApplicationUpdateState.Ready ? Visibility.Visible : Visibility.Collapsed; button.ToolTip = "Update " + app.Updates.AvailableVersion + " ready · review and restart"; AutomationProperties.SetName(button, button.ToolTip.ToString()); }
        button.Loaded += (_, _) => { app.Updates.Changed += Refresh; Refresh(); }; button.Unloaded += (_, _) => app.Updates.Changed -= Refresh; Refresh(); return button;
    }
}

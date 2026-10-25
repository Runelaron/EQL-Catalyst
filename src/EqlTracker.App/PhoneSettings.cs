using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using EqlTracker.Companion;
using EqlTracker.Core;
using QRCoder;

namespace EqlTracker.App;

public sealed partial class SettingsWindow
{
    private UIElement BuildPhoneSettings()
    {
        var settings = Controller.PhoneSettings;
        var enabled = new CheckBox { Content = "Enable local phone sharing", IsChecked = settings.Enabled };
        var interfaces = new ComboBox { Width = 180, ItemsSource = LanBoundary.Interfaces().Select(i => i.Address.ToString()).ToArray(), SelectedItem = settings.Address };
        var port = UI.Input(settings.Port.ToString(), 80); var hostname = UI.Input(settings.BrowserHostname, 220); var thumbprint = UI.Input(settings.BrowserCertificateThumbprint, 280);
        var state = UI.Text(Controller.PhoneStatus, 12, "Mint"); var pairing = new StackPanel(); var devices = new StackPanel(); var pending = new StackPanel();
        string deviceKey = "";
        string baseline=PetComparisonEngine.Key(settings);
        CompanionHostSettings Edited()=>new(){Enabled=enabled.IsChecked==true,Address=interfaces.SelectedItem?.ToString()??"",Port=int.TryParse(port.Text,out int number)?number:0,BrowserHostname=hostname.Text.Trim(),BrowserCertificateThumbprint=thumbprint.Text.Trim()};
        var apply=WorkspaceForms.Control(UI.Button("Apply connection",()=>{}));
        apply.Click+=async(_,_)=>{await Controller.ConfigurePhoneAsync(Edited());baseline=PetComparisonEngine.Key(Controller.PhoneSettings);Dirty();Refresh();};
        void Dirty(){var draft=Edited();apply.IsEnabled=draft.Port is >0 and <=65535&&(!draft.Enabled||draft.Address.Length>0)&&PetComparisonEngine.Key(draft)!=baseline;}
        enabled.Click+=(_,_)=>Dirty();interfaces.SelectionChanged+=(_,_)=>Dirty();foreach(var box in new[]{port,hostname,thumbprint})box.TextChanged+=(_,_)=>Dirty();Dirty();
        var content=UI.Stack(Group("Phone companion",enabled,Flow(Field("Private LAN interface",interfaces,"Only this selected local interface and subnet can connect"),Field("Port",port,"HTTPS only; no port forwarding")),apply,state,UI.Text("Read-only · approve each device on this PC. Private network / LocalSubnet access only.",11,"SecondaryText")),
            FocusedLayout.Disclosure("Browser certificate setup",UI.Stack(Field("Trusted LAN hostname",hostname,"A matching certificate must already be trusted by the browser"),Field("Certificate thumbprint",thumbprint,"Windows Current User / Personal certificate, matching hostname and private key"),UI.Text("Resolve certificate warnings before pairing. Android QR trust is independent.",11,"SecondaryText")),"Optional browser prerequisite"),
            Group("Pair a device",Flow(UI.Button("Pair Android",()=>Pair(false)),UI.Button("Pair browser",()=>Pair(true))),pairing,pending),FocusedLayout.Disclosure("Approved devices & sharing",devices,"Revoke devices or explicitly allow other saved characters and holdings"));
        foreach(var control in Usability.Tree(content).OfType<Control>().Where(c=>c is Button or ComboBox or TextBox))WorkspaceForms.Control(control);
        void Pair(bool browser)
        {
            pairing.Children.Clear(); try
            {
                var challenge = Controller.PhoneHost?.CreatePairing(browser) ?? throw new InvalidOperationException("Enable and start the phone host first.");
                string payload = browser ? challenge.Address + "/#pair=" + challenge.Challenge : JsonSerializer.Serialize(challenge, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                using var generator = new QRCodeGenerator(); using var qr = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M); using var code = new PngByteQRCode(qr); byte[] png = code.GetGraphic(5);
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.StreamSource = new System.IO.MemoryStream(png); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.EndInit(); bitmap.Freeze();
                pairing.Children.Add(new Image { Source = bitmap, Width = 300, Height = 300, HorizontalAlignment = HorizontalAlignment.Left });
                pairing.Children.Add(UI.Text("Single use · expires " + challenge.ExpiresAt.LocalDateTime.ToString("HH:mm:ss") + " · approve the request on this PC", 11));
                if (browser) pairing.Children.Add(UI.Button("Copy browser pairing link", () => Clipboard.SetText(payload)));
                else { pairing.Children.Add(UI.Text(challenge.Address + "\nServer " + challenge.ServerId, 11, "SecondaryText")); pairing.Children.Add(UI.Button("Copy manual pairing details", () => Clipboard.SetText(payload))); }
            }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException) { pairing.Children.Add(UI.Text(error.Message, 12)); }
        }
        void Refresh()
        {
            state.Text = Controller.PhoneStatus; var host = Controller.PhoneHost;
            if (host == null) { pending.Children.Clear(); devices.Children.Clear(); return; }
            var requests = host.Pairing.Pending; var known = host.Pairing.Devices; string key = JsonSerializer.Serialize(new { requests, known }); if (key == deviceKey) return; deviceKey = key;
            pending.Children.Clear(); foreach (var request in requests)
            {
                var info = request; pending.Children.Add(UI.Text(info.Name + " · " + info.Kind + " · code "+info.Id[..6].ToUpperInvariant()+" · awaiting approval", 12, "Accent", true));
                pending.Children.Add(WorkspaceForms.Flow(UI.Button("Approve current character", async () => { await Task.Run(() => host.Pairing.Approve(info.Id, Controller.SharingContext)); Refresh(); }), UI.Button("Reject", () => { host.Pairing.Reject(info.Id); Refresh(); })));
            }
            devices.Children.Clear(); foreach (var device in known.Where(d => !d.Revoked))
            {
                var checks = new List<(string Id, CheckBox Check)>(); var body = new StackPanel();
                foreach (var character in Controller.ShareableCharacters()) { var check = new CheckBox { Content = character.Name, IsChecked = device.Contexts.Contains(character.Id) }; checks.Add((character.Id, check)); body.Children.Add(check); }
                var holdings = new CheckBox { Content = "Share holdings from allowed characters", IsChecked = device.ShareHoldings }; body.Children.Add(holdings);
                var save=WorkspaceForms.Control(UI.Button("Save sharing",async()=>{var contexts=checks.Where(c=>c.Check.IsChecked==true).Select(c=>c.Id).ToHashSet();bool share=holdings.IsChecked==true;await Task.Run(()=>host.Pairing.SetSharing(device.Id,contexts,share));deviceKey="";Refresh();}));save.IsEnabled=false;
                void Changed()=>save.IsEnabled=!checks.Where(c=>c.Check.IsChecked==true).Select(c=>c.Id).ToHashSet().SetEquals(device.Contexts)||holdings.IsChecked==true!=device.ShareHoldings;
                foreach(var check in checks)check.Check.Click+=(_,_)=>Changed();holdings.Click+=(_,_)=>Changed();
                body.Children.Add(WorkspaceForms.Flow(save,WorkspaceForms.Control(UI.Button("Revoke device",async()=>{await Task.Run(()=>host.Pairing.Revoke(device.Id));Refresh();}))));
                devices.Children.Add(FocusedLayout.Disclosure(device.Name + " · " + device.Kind, body, "Read-only access · approved " + device.ApprovedAt.ToLocalTime()));
            }
        }
        var refresh = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; refresh.Tick += (_, _) => { if (IsVisible && SelectedSection == SettingsSection.Phone) Refresh(); }; content.Loaded += (_, _) => { Refresh(); refresh.Start(); }; content.Unloaded += (_, _) => refresh.Stop(); return content;
    }
}

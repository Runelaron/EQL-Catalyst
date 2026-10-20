using System.Windows.Media.Imaging;
using EqlTracker.Core;

namespace EqlTracker.App;

public sealed record CatalystIcon(string Id, string Name, string Asset);
public static class CatalystBranding
{
    public const string Name = "EQL Catalyst";
    public static IReadOnlyList<CatalystIcon> Choices { get; } = new List<CatalystIcon> {
        new("blue", "Blue Orb", "catalyst"), new("gold-elements", "Gold Elements", "gold-elements"),
        new("class-war", "Warrior", "class-war"), new("class-clr", "Cleric", "class-clr"), new("class-pal", "Paladin", "class-pal"), new("class-rng", "Ranger", "class-rng"),
        new("class-shd", "Shadow Knight", "class-shd"), new("class-dru", "Druid", "class-dru"), new("class-mnk", "Monk", "class-mnk"), new("class-brd", "Bard", "class-brd"),
        new("class-rog", "Rogue", "class-rog"), new("class-shm", "Shaman", "class-shm"), new("class-nec", "Necromancer", "class-nec"), new("class-wiz", "Wizard", "class-wiz"),
        new("class-mag", "Magician", "class-mag"), new("class-enc", "Enchanter", "class-enc"), new("class-bst", "Beastlord", "class-bst"), new("class-ber", "Berserker", "class-ber") };
    private static readonly Dictionary<string, BitmapFrame> frames = [];
    public static string SelectedId { get; set; } = "blue";
    public static IconArtworkStyle SelectedStyle { get; set; } = IconArtworkStyle.Vibrant;
    public static CatalystIcon Resolve(string? id) => Choices.FirstOrDefault(c => c.Id == id) ?? Choices[0];
    private static Uri Resource(CatalystIcon choice, string extension) => new("pack://application:,,,/EQLTracker;component/Assets/" + choice.Asset + extension);
    public static BitmapFrame Image(string? id)
    {
        var choice = Resolve(id);
        if (IconArtwork.Style == IconArtworkStyle.Vibrant && IconArtwork.Painted(IconArtwork.AppRole(choice.Id)) is { } painted) return painted;
        if (frames.TryGetValue(choice.Id, out var image)) return image;
        try { image = BitmapFrame.Create(Resource(choice, "-256.png"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad); image.Freeze(); frames[choice.Id] = image; return image; }
        catch when (choice.Id != "blue") { return Image("blue"); }
    }
    public static BitmapFrame WindowIcon => Image(SelectedId);
    public static System.Drawing.Icon CreateTrayIcon(string? id = null)
    {
        var choice = Resolve(id ?? SelectedId);
        if (IconArtwork.Style == IconArtworkStyle.Vibrant)
        {
            try
            {
                using var paintedStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/EQLTracker;component/Assets/Painted/" + IconArtwork.AppRole(choice.Id) + ".ico"))!.Stream;
                using var painted = new System.Drawing.Icon(paintedStream, System.Windows.Forms.SystemInformation.SmallIconSize);
                return (System.Drawing.Icon)painted.Clone();
            }
            catch (Exception error) when (error is IOException or ArgumentException or NullReferenceException) { /* Retain this identity in Clean artwork. */ }
        }
        try
        {
            using var stream = System.Windows.Application.GetResourceStream(Resource(choice, ".ico"))!.Stream;
            using var icon = new System.Drawing.Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
            return (System.Drawing.Icon)icon.Clone();
        }
        catch when (choice.Id != "blue") { return CreateTrayIcon("blue"); }
    }
}

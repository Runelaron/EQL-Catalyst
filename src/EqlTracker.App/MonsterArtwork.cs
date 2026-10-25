using System.Windows;
using System.Windows.Media;

namespace EqlTracker.App;

/// <summary>Compatibility entry point for the shared approved Monster treatment.</summary>
internal static class MonsterArtwork
{
    internal static bool Detailed(double dip,Core.IconArtworkStyle style)=>ApprovedPetArtwork.Detailed(dip,style);
    internal static void Draw(DrawingContext dc,Rect bounds,bool vibrant)
    {
        ApprovedPetArtwork.Vector(dc,"pet-monster",bounds,vibrant);
    }
}

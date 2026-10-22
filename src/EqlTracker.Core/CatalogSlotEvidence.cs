namespace EqlTracker.Core;

public static class CatalogSlotEvidence
{
    public static void Apply(Catalog catalog, string path)
    {
        if(!File.Exists(path)) return;
        CatalogItemMerge.Apply(catalog,Json.Read<List<PetItemRules.WikiFixture>>(path).Select(f=>WikiClient.ParseItem(f.Title,f.Text,f.Revision)));
    }
}

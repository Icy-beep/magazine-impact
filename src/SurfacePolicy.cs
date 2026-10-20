namespace MagazineImpact
{
    public enum SurfaceKind
    {
        Concrete, Metal, Wood, Soil, Grass, Asphalt, Glass, Gravel, MetalThin,
        Puddle, Slate, Tile, WoodThick, Swamp, Garbage, GarbageMetal, Plastic,
        WholeGlass, WoodThin, Snow
    }

    internal static class SurfacePolicy
    {
        internal static string Family(string surface)
        {
            switch (surface)
            {
                case "Wood": case "WoodThin": case "WoodThick": return "wood";
                case "Soil": case "Gravel": return "ground";
                case "Concrete": case "Asphalt": return "concrete";
                case "Metal": case "MetalThin": case "GarbageMetal": return "metal";
                case "Glass": case "WholeGlass": return "glass";
                case "Slate": case "Tile": return "tile";
                case "Puddle": case "Swamp": return "water";
                case "Grass": return "grass";
                case "Snow": return "snow";
                case "Plastic": return "plastic";
                case "Garbage": return "garbage";
                default: return "default";
            }
        }
    }
}

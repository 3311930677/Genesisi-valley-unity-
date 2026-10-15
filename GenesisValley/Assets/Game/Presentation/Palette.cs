using UnityEngine;

namespace GenesisValley.Presentation
{
    /// <summary>GDD §7.1 base palette. Direction, not per-pixel law.</summary>
    public static class Palette
    {
        public static readonly Color Grass = Hex(0x84976A);
        public static readonly Color CanopyA = Hex(0x627B55);
        public static readonly Color CanopyB = Hex(0x476959);
        public static readonly Color Road = Hex(0xB69B70);
        public static readonly Color Wall = Hex(0xE0D4B7);
        public static readonly Color Roof = Hex(0xA76249);
        public static readonly Color Rock = Hex(0x88928D);
        public static readonly Color Water = Hex(0x7DAEAE);
        public static readonly Color NightLamp = Hex(0xE5B96D);
        public static readonly Color Bark = Hex(0x6E5644);
        public static readonly Color Cloth = Hex(0x9C7B5B);

        public static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);
    }
}

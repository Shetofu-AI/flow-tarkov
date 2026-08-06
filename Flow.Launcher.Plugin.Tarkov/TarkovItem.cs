using System.Text.Json.Serialization;

namespace Flow.Launcher.Plugin.Tarkov
{
    public class TarkovItem
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string ShortName { get; set; } = string.Empty;

        public string NormalizedName { get; set; } = string.Empty;

        public int Width { get; set; }

        public int Height { get; set; }

        public int BasePrice { get; set; }

        public int FleaPrice { get; set; }

        public int FleaAveragePrice { get; set; }

        public bool BannedFromFlea { get; set; }

        public string IconLink { get; set; } = string.Empty;

        public string Link { get; set; } = string.Empty;

        public string WikiLink { get; set; } = string.Empty;

        public string VendorName { get; set; } = string.Empty;

        public int VendorPrice { get; set; }

        [JsonIgnore]
        public string SearchText { get; private set; } = string.Empty;

        public int Slots => Math.Max(1, Width * Height);

        public void PrepareSearchText()
        {
            SearchText = $"{Name} {ShortName} {NormalizedName.Replace('-', ' ')}";
        }
    }
}

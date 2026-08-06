namespace Flow.Launcher.Plugin.Tarkov
{
    public class ItemsSnapshot
    {
        public bool NotModified { get; set; }

        public string Etag { get; set; } = string.Empty;

        public List<TarkovItem> Items { get; set; } = new List<TarkovItem>();
    }
}

using System.IO;
using System.Text.Json;

namespace Flow.Launcher.Plugin.Tarkov
{
    public class ItemIndex
    {
        private static readonly TimeSpan CACHE_LIFETIME = TimeSpan.FromMinutes(30);

        private readonly TarkovApi _api;
        private readonly string _cachePath;
        private readonly object _gate = new object();

        private List<TarkovItem> _items = new List<TarkovItem>();
        private DateTimeOffset _updatedAt = DateTimeOffset.MinValue;
        private Task? _refresh;
        private string _lastError = string.Empty;

        public ItemIndex(TarkovApi api, string cachePath)
        {
            _api = api;
            _cachePath = cachePath;
        }

        public IReadOnlyList<TarkovItem> Items => _items;

        public DateTimeOffset UpdatedAt => _updatedAt;

        public string LastError => _lastError;

        public bool IsLoading => _refresh != null && !_refresh.IsCompleted;

        public bool IsStale => DateTimeOffset.UtcNow - _updatedAt > CACHE_LIFETIME;

        public void LoadFromDisk()
        {
            if (!File.Exists(_cachePath))
            {
                return;
            }

            try
            {
                using FileStream stream = File.OpenRead(_cachePath);
                CacheFile? cache = JsonSerializer.Deserialize<CacheFile>(stream);
                if (cache == null || cache.Items.Count == 0)
                {
                    return;
                }

                PrepareSearchText(cache.Items);
                _items = cache.Items;
                _updatedAt = cache.UpdatedAt;
            }
            catch (Exception exception)
            {
                _lastError = $"Кэш не прочитан: {exception.Message}";
            }
        }

        public Task EnsureFreshAsync()
        {
            if (!IsStale)
            {
                return Task.CompletedTask;
            }

            return RefreshNowAsync();
        }

        public Task RefreshNowAsync()
        {
            lock (_gate)
            {
                if (_refresh != null && !_refresh.IsCompleted)
                {
                    return _refresh;
                }

                _refresh = RefreshAsync();
                return _refresh;
            }
        }

        private async Task RefreshAsync()
        {
            try
            {
                List<TarkovItem> fetched = await _api.FetchItemsAsync(CancellationToken.None);
                PrepareSearchText(fetched);
                _items = fetched;
                _updatedAt = DateTimeOffset.UtcNow;
                _lastError = string.Empty;
                SaveToDisk();
            }
            catch (Exception exception)
            {
                _lastError = exception.Message;
            }
        }

        private static void PrepareSearchText(List<TarkovItem> items)
        {
            foreach (TarkovItem item in items)
            {
                item.PrepareSearchText();
            }
        }

        private void SaveToDisk()
        {
            try
            {
                string? directory = Path.GetDirectoryName(_cachePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                CacheFile cache = new CacheFile();
                cache.UpdatedAt = _updatedAt;
                cache.Items = _items;

                string temporaryPath = _cachePath + ".tmp";
                using (FileStream stream = File.Create(temporaryPath))
                {
                    JsonSerializer.Serialize(stream, cache);
                }

                File.Move(temporaryPath, _cachePath, true);
            }
            catch (Exception exception)
            {
                _lastError = $"Кэш не сохранён: {exception.Message}";
            }
        }

        private class CacheFile
        {
            public DateTimeOffset UpdatedAt { get; set; }

            public List<TarkovItem> Items { get; set; } = new List<TarkovItem>();
        }
    }
}

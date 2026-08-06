using System.IO;
using System.Text.Json;

namespace Flow.Launcher.Plugin.Tarkov
{
    public class ItemIndex
    {
        private static readonly TimeSpan CACHE_LIFETIME = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan RETRY_DELAY = TimeSpan.FromSeconds(30);

        private readonly TarkovApi _api;
        private readonly string _cachePath;
        private readonly object _gate = new object();

        private List<TarkovItem> _items = new List<TarkovItem>();
        private DateTimeOffset _updatedAt = DateTimeOffset.MinValue;
        private DateTimeOffset _blockedUntil = DateTimeOffset.MinValue;
        private Task? _refresh;
        private string _lastError = string.Empty;
        private string _etag = string.Empty;

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
                _etag = cache.Etag;
            }
            catch (Exception exception)
            {
                _lastError = $"Кэш не прочитан: {exception.Message}";
            }
        }

        public Task EnsureFreshAsync()
        {
            if (!IsStale || DateTimeOffset.UtcNow < _blockedUntil)
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
                ItemsSnapshot snapshot = await _api.FetchItemsAsync(_etag, CancellationToken.None);
                if (!snapshot.NotModified)
                {
                    PrepareSearchText(snapshot.Items);
                    _items = snapshot.Items;
                }

                _etag = snapshot.Etag;
                _updatedAt = DateTimeOffset.UtcNow;
                _blockedUntil = DateTimeOffset.MinValue;
                _lastError = string.Empty;
                SaveToDisk();
            }
            catch (Exception exception)
            {
                _blockedUntil = DateTimeOffset.UtcNow + RETRY_DELAY;
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
                cache.Etag = _etag;
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

            public string Etag { get; set; } = string.Empty;

            public List<TarkovItem> Items { get; set; } = new List<TarkovItem>();
        }
    }
}

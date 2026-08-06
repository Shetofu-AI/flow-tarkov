using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace Flow.Launcher.Plugin.Tarkov
{
    public class TarkovApi
    {
        private const string BASE_URL = "https://json.tarkov.dev/";
        private const string GAME_MODE = "regular";
        private const string LANGUAGE = "ru";
        private const string ITEMS_DATASET = "items";
        private const string TRADERS_DATASET = "traders";
        private const string NO_FLEA_TYPE = "noFlea";
        private const string TRADER_NAME_SUFFIX = " Nickname";

        private readonly HttpClient _client;

        public TarkovApi()
        {
            HttpClientHandler handler = new HttpClientHandler();
            handler.AutomaticDecompression = DecompressionMethods.All;

            _client = new HttpClient(handler);
            _client.Timeout = TimeSpan.FromMinutes(2);
            _client.DefaultRequestHeaders.Add("User-Agent", "Flow.Launcher.Plugin.Tarkov");
            _client.DefaultRequestHeaders.Add("Accept", "application/json");
        }

        public async Task<ItemsSnapshot> FetchItemsAsync(string etag, CancellationToken token)
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, BuildUrl(ITEMS_DATASET));
            if (!string.IsNullOrEmpty(etag))
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            }

            using HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                ItemsSnapshot unchanged = new ItemsSnapshot();
                unchanged.NotModified = true;
                unchanged.Etag = etag;
                return unchanged;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new TarkovApiException($"json.tarkov.dev ответил {(int)response.StatusCode} на {ITEMS_DATASET}");
            }

            using JsonDocument items = await ReadDocumentAsync(response, token);
            Dictionary<string, string> itemNames = await FetchTranslationsAsync(ITEMS_DATASET, token);
            Dictionary<string, string> traderNames = await FetchTraderNamesAsync(token);

            ItemsSnapshot snapshot = new ItemsSnapshot();
            snapshot.Etag = response.Headers.ETag?.Tag ?? string.Empty;
            snapshot.Items = ParseItems(items, itemNames, traderNames);

            return snapshot;
        }

        public static List<TarkovItem> ParseItems(
            JsonDocument document,
            Dictionary<string, string> itemNames,
            Dictionary<string, string> traderNames)
        {
            if (!document.RootElement.TryGetProperty("data", out JsonElement data) ||
                !data.TryGetProperty("items", out JsonElement items) ||
                items.ValueKind != JsonValueKind.Object)
            {
                throw new TarkovApiException("json.tarkov.dev вернул ответ без предметов");
            }

            List<TarkovItem> parsed = new List<TarkovItem>();
            foreach (JsonProperty entry in items.EnumerateObject())
            {
                parsed.Add(ParseItem(entry.Value, itemNames, traderNames));
            }

            if (parsed.Count == 0)
            {
                throw new TarkovApiException("json.tarkov.dev вернул пустой список предметов");
            }

            return parsed;
        }

        public static Dictionary<string, string> ParseTranslations(JsonDocument document)
        {
            Dictionary<string, string> translations = new Dictionary<string, string>();
            if (!document.RootElement.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Object)
            {
                return translations;
            }

            foreach (JsonProperty entry in data.EnumerateObject())
            {
                if (entry.Value.ValueKind == JsonValueKind.String)
                {
                    translations[entry.Name] = entry.Value.GetString() ?? string.Empty;
                }
            }

            return translations;
        }

        private async Task<Dictionary<string, string>> FetchTranslationsAsync(string dataset, CancellationToken token)
        {
            using HttpResponseMessage response = await _client.GetAsync(BuildUrl($"{dataset}_{LANGUAGE}"), HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode)
            {
                throw new TarkovApiException($"json.tarkov.dev ответил {(int)response.StatusCode} на перевод {dataset}");
            }

            using JsonDocument document = await ReadDocumentAsync(response, token);
            return ParseTranslations(document);
        }

        private async Task<Dictionary<string, string>> FetchTraderNamesAsync(CancellationToken token)
        {
            Dictionary<string, string> translations = await FetchTranslationsAsync(TRADERS_DATASET, token);

            Dictionary<string, string> names = new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> entry in translations)
            {
                if (!entry.Key.EndsWith(TRADER_NAME_SUFFIX, StringComparison.Ordinal))
                {
                    continue;
                }

                string traderId = entry.Key.Substring(0, entry.Key.Length - TRADER_NAME_SUFFIX.Length);
                names[traderId] = entry.Value;
            }

            return names;
        }

        private static async Task<JsonDocument> ReadDocumentAsync(HttpResponseMessage response, CancellationToken token)
        {
            await using Stream stream = await response.Content.ReadAsStreamAsync(token);
            return await JsonDocument.ParseAsync(stream, default, token);
        }

        private static TarkovItem ParseItem(
            JsonElement element,
            Dictionary<string, string> itemNames,
            Dictionary<string, string> traderNames)
        {
            TarkovItem item = new TarkovItem();
            item.Id = ReadString(element, "id");
            item.Name = Translate(ReadString(element, "name"), itemNames);
            item.ShortName = Translate(ReadString(element, "shortName"), itemNames);
            item.NormalizedName = ReadString(element, "normalizedName");
            item.IconLink = ReadString(element, "iconLink");
            item.Link = ReadString(element, "link");
            item.WikiLink = ReadString(element, "wikiLink");
            item.Width = ReadInt(element, "width");
            item.Height = ReadInt(element, "height");
            item.BasePrice = ReadInt(element, "basePrice");
            item.FleaPrice = ReadInt(element, "lastLowPrice");
            item.FleaAveragePrice = ReadInt(element, "avg24hPrice");
            item.BannedFromFlea = HasType(element, NO_FLEA_TYPE);

            FillBestVendor(element, traderNames, item);

            return item;
        }

        private static void FillBestVendor(JsonElement element, Dictionary<string, string> traderNames, TarkovItem item)
        {
            if (!element.TryGetProperty("sellToTrader", out JsonElement offers) || offers.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (JsonElement offer in offers.EnumerateArray())
            {
                int price = ReadInt(offer, "priceRUB");
                if (price <= item.VendorPrice)
                {
                    continue;
                }

                string traderId = ReadString(offer, "trader");
                item.VendorPrice = price;
                item.VendorName = traderNames.TryGetValue(traderId, out string? name) ? name : traderId;
            }
        }

        private static string Translate(string key, Dictionary<string, string> translations)
        {
            return translations.TryGetValue(key, out string? translated) ? translated : key;
        }

        private static bool HasType(JsonElement element, string type)
        {
            if (!element.TryGetProperty("types", out JsonElement types) || types.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (JsonElement value in types.EnumerateArray())
            {
                if (value.ValueKind == JsonValueKind.String && value.GetString() == type)
                {
                    return true;
                }
            }

            return false;
        }

        private static string ReadString(JsonElement element, string property)
        {
            if (element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? string.Empty;
            }

            return string.Empty;
        }

        private static int ReadInt(JsonElement element, string property)
        {
            if (element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.Number)
            {
                return value.GetInt32();
            }

            return 0;
        }

        private static string BuildUrl(string dataset)
        {
            return $"{BASE_URL}{GAME_MODE}/{dataset}";
        }
    }
}

using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Flow.Launcher.Plugin.Tarkov
{
    public class TarkovApi
    {
        private const string ENDPOINT = "https://api.tarkov.dev/graphql";
        private const string FLEA_MARKET_VENDOR = "flea-market";
        private const string NO_FLEA_TYPE = "noFlea";
        private const string QUERY =
            "{items(lang:ru){id name shortName normalizedName basePrice width height iconLink link wikiLink types " +
            "avg24hPrice lastLowPrice sellFor{priceRUB vendor{normalizedName name}}}}";

        private readonly HttpClient _client;

        public TarkovApi()
        {
            _client = new HttpClient();
            _client.Timeout = TimeSpan.FromSeconds(60);
            _client.DefaultRequestHeaders.Add("User-Agent", "Flow.Launcher.Plugin.Tarkov");
        }

        public async Task<List<TarkovItem>> FetchItemsAsync(CancellationToken token)
        {
            string payload = JsonSerializer.Serialize(new { query = QUERY });
            using StringContent body = new StringContent(payload, Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await _client.PostAsync(ENDPOINT, body, token);
            if (!response.IsSuccessStatusCode)
            {
                throw new TarkovApiException($"tarkov.dev ответил {(int)response.StatusCode}");
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(token);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, default, token);

            return ParseResponse(document);
        }

        public static List<TarkovItem> ParseResponse(JsonDocument document)
        {
            if (document.RootElement.TryGetProperty("errors", out JsonElement errors))
            {
                throw new TarkovApiException(DescribeErrors(errors));
            }

            if (!document.RootElement.TryGetProperty("data", out JsonElement data) ||
                !data.TryGetProperty("items", out JsonElement items) ||
                items.ValueKind != JsonValueKind.Array)
            {
                throw new TarkovApiException("tarkov.dev вернул ответ без предметов");
            }

            List<TarkovItem> parsed = new List<TarkovItem>(items.GetArrayLength());
            foreach (JsonElement element in items.EnumerateArray())
            {
                parsed.Add(ParseItem(element));
            }

            if (parsed.Count == 0)
            {
                throw new TarkovApiException("tarkov.dev вернул пустой список предметов");
            }

            return parsed;
        }

        private static TarkovItem ParseItem(JsonElement element)
        {
            TarkovItem item = new TarkovItem();
            item.Id = ReadString(element, "id");
            item.Name = ReadString(element, "name");
            item.ShortName = ReadString(element, "shortName");
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

            FillBestVendor(element, item);

            return item;
        }

        private static void FillBestVendor(JsonElement element, TarkovItem item)
        {
            if (!element.TryGetProperty("sellFor", out JsonElement offers) || offers.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (JsonElement offer in offers.EnumerateArray())
            {
                if (!offer.TryGetProperty("vendor", out JsonElement vendor))
                {
                    continue;
                }

                string vendorId = ReadString(vendor, "normalizedName");
                if (vendorId == FLEA_MARKET_VENDOR)
                {
                    continue;
                }

                int price = ReadInt(offer, "priceRUB");
                if (price <= item.VendorPrice)
                {
                    continue;
                }

                item.VendorPrice = price;
                item.VendorName = ReadString(vendor, "name");
            }
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

        private static string DescribeErrors(JsonElement errors)
        {
            if (errors.ValueKind != JsonValueKind.Array || errors.GetArrayLength() == 0)
            {
                return "tarkov.dev вернул ошибку";
            }

            JsonElement first = errors[0];
            if (first.ValueKind == JsonValueKind.String)
            {
                return first.GetString() ?? "tarkov.dev вернул ошибку";
            }

            string message = ReadString(first, "message");
            return string.IsNullOrEmpty(message) ? "tarkov.dev вернул ошибку" : message;
        }
    }
}

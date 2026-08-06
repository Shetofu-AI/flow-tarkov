using System.IO;
using Flow.Launcher.Plugin.SharedModels;

namespace Flow.Launcher.Plugin.Tarkov
{
    public class Main : IAsyncPlugin, IContextMenu
    {
        private const int MAX_RESULTS = 15;
        private const int FIRST_LOAD_WAIT_MS = 4000;
        private const string CACHE_FOLDER = "FlowLauncher.Tarkov";
        private const string CACHE_FILE = "items.json";

        private PluginInitContext _context = null!;
        private ItemIndex _index = null!;

        public Task InitAsync(PluginInitContext context)
        {
            _context = context;
            _index = new ItemIndex(new TarkovApi(), BuildCachePath());
            _index.LoadFromDisk();
            _ = _index.EnsureFreshAsync();

            return Task.CompletedTask;
        }

        public async Task<List<Result>> QueryAsync(Query query, CancellationToken token)
        {
            string search = query.Search.Trim();

            if (_index.Items.Count == 0)
            {
                await Task.WhenAny(_index.EnsureFreshAsync(), Task.Delay(FIRST_LOAD_WAIT_MS, token));
            }
            else
            {
                _ = _index.EnsureFreshAsync();
            }

            if (_index.Items.Count == 0)
            {
                return BuildStatusResults();
            }

            if (search.Length == 0)
            {
                return BuildIdleResults();
            }

            List<Result> results = Search(search, token);
            if (results.Count == 0)
            {
                results = Search(KeyboardLayout.Swap(search), token);
            }

            if (results.Count == 0)
            {
                return BuildEmptySearchResults(search);
            }

            return results;
        }

        public List<Result> LoadContextMenus(Result selectedResult)
        {
            List<Result> menu = new List<Result>();
            if (selectedResult.ContextData is not TarkovItem item)
            {
                return menu;
            }

            if (item.FleaPrice > 0)
            {
                menu.Add(BuildCopyResult("Копировать цену барахолки", PriceFormatter.Rubles(item.FleaPrice), item.FleaPrice.ToString()));
            }

            if (item.VendorPrice > 0)
            {
                menu.Add(BuildCopyResult($"Копировать цену от «{item.VendorName}»", PriceFormatter.Rubles(item.VendorPrice), item.VendorPrice.ToString()));
            }

            menu.Add(BuildCopyResult("Копировать название", item.Name, item.Name));

            if (!string.IsNullOrEmpty(item.WikiLink))
            {
                Result wiki = new Result();
                wiki.Title = "Открыть на вики";
                wiki.SubTitle = item.WikiLink;
                wiki.IcoPath = _context.CurrentPluginMetadata.IcoPath;
                wiki.Action = _ =>
                {
                    _context.API.OpenUrl(item.WikiLink);
                    return true;
                };
                menu.Add(wiki);
            }

            return menu;
        }

        private List<Result> Search(string search, CancellationToken token)
        {
            string phonetic = Phonetics.Key(search);
            List<TarkovItem> matched = new List<TarkovItem>();
            List<int> scores = new List<int>();

            foreach (TarkovItem item in _index.Items)
            {
                if (token.IsCancellationRequested)
                {
                    return new List<Result>();
                }

                int score = ItemScorer.Score(item, phonetic);
                if (score == 0)
                {
                    score = Math.Min(_context.API.FuzzySearch(search, item.SearchText).Score, ItemScorer.FUZZY_LIMIT);
                }

                if (score <= 0)
                {
                    continue;
                }

                matched.Add(item);
                scores.Add(score);
            }

            List<int> order = new List<int>(matched.Count);
            for (int index = 0; index < matched.Count; index++)
            {
                order.Add(index);
            }

            order.Sort(delegate (int left, int right)
            {
                int byScore = scores[right].CompareTo(scores[left]);
                if (byScore != 0)
                {
                    return byScore;
                }

                return matched[left].Name.Length.CompareTo(matched[right].Name.Length);
            });

            List<Result> results = new List<Result>();
            for (int index = 0; index < order.Count && results.Count < MAX_RESULTS; index++)
            {
                TarkovItem item = matched[order[index]];
                results.Add(BuildItemResult(item, scores[order[index]], search));
            }

            return results;
        }

        private Result BuildItemResult(TarkovItem item, int score, string search)
        {
            Result result = new Result();
            result.Title = item.Name;
            result.SubTitle = BuildSubtitle(item);
            result.IcoPath = string.IsNullOrEmpty(item.IconLink) ? _context.CurrentPluginMetadata.IcoPath : item.IconLink;
            result.Score = score;
            result.ContextData = item;
            result.CopyText = item.FleaPrice > 0 ? item.FleaPrice.ToString() : item.Name;
            result.AutoCompleteText = $"{_context.CurrentPluginMetadata.ActionKeyword} {item.Name}";

            MatchResult titleMatch = _context.API.FuzzySearch(search, item.Name);
            if (titleMatch.IsSearchPrecisionScoreMet())
            {
                result.TitleHighlightData = titleMatch.MatchData;
            }

            result.Action = _ =>
            {
                _context.API.OpenUrl(item.Link);
                return true;
            };

            return result;
        }

        private static string BuildSubtitle(TarkovItem item)
        {
            List<string> parts = new List<string>();

            if (!string.IsNullOrEmpty(item.ShortName) && item.ShortName != item.Name)
            {
                parts.Add(item.ShortName);
            }

            if (item.BannedFromFlea)
            {
                parts.Add("не продаётся на барахолке");
            }
            else if (item.FleaPrice > 0)
            {
                string flea = $"барахолка {PriceFormatter.Rubles(item.FleaPrice)}";
                if (item.Slots > 1)
                {
                    flea += $" ({PriceFormatter.Rubles(item.FleaPrice / item.Slots)}/слот)";
                }

                parts.Add(flea);
            }
            else
            {
                parts.Add("предложений на барахолке нет");
            }

            if (item.VendorPrice > 0)
            {
                parts.Add($"{item.VendorName} {PriceFormatter.Rubles(item.VendorPrice)}");
            }

            return string.Join("  ·  ", parts);
        }

        private List<Result> BuildIdleResults()
        {
            Result hint = new Result();
            hint.Title = "Введи название предмета";
            hint.SubTitle = $"В базе {_index.Items.Count} предметов, цены от {PriceFormatter.Time(_index.UpdatedAt)}. Ищет по русским и английским названиям";
            hint.IcoPath = _context.CurrentPluginMetadata.IcoPath;
            hint.Score = 100;

            Result refresh = new Result();
            refresh.Title = "Обновить цены сейчас";
            refresh.SubTitle = _index.IsLoading ? "Обновление уже идёт" : "Заново скачать базу с tarkov.dev";
            refresh.IcoPath = _context.CurrentPluginMetadata.IcoPath;
            refresh.Score = 50;
            refresh.Action = _ =>
            {
                StartManualRefresh();
                return true;
            };

            return new List<Result> { hint, refresh };
        }

        private List<Result> BuildStatusResults()
        {
            Result status = new Result();
            status.IcoPath = _context.CurrentPluginMetadata.IcoPath;

            if (_index.IsLoading)
            {
                status.Title = "Загружаю базу предметов…";
                status.SubTitle = "Первая загрузка занимает несколько секунд, повтори запрос";
                return new List<Result> { status };
            }

            status.Title = "Данные tarkov.dev недоступны";
            status.SubTitle = string.IsNullOrEmpty(_index.LastError) ? "Enter — попробовать снова" : $"{_index.LastError}. Enter — попробовать снова";
            status.Action = _ =>
            {
                StartManualRefresh();
                return true;
            };

            return new List<Result> { status };
        }

        private List<Result> BuildEmptySearchResults(string search)
        {
            Result empty = new Result();
            empty.Title = $"Ничего не нашлось по «{search}»";
            empty.SubTitle = "Попробуй короче или английское название";
            empty.IcoPath = _context.CurrentPluginMetadata.IcoPath;

            return new List<Result> { empty };
        }

        private Result BuildCopyResult(string title, string subtitle, string payload)
        {
            Result result = new Result();
            result.Title = title;
            result.SubTitle = subtitle;
            result.IcoPath = _context.CurrentPluginMetadata.IcoPath;
            result.Action = _ =>
            {
                _context.API.CopyToClipboard(payload);
                return true;
            };

            return result;
        }

        private void StartManualRefresh()
        {
            _context.API.ShowMsg("Tarkov", "Обновляю цены с tarkov.dev");
            _ = _index.RefreshNowAsync();
        }

        private static string BuildCachePath()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(root, CACHE_FOLDER, CACHE_FILE);
        }
    }
}

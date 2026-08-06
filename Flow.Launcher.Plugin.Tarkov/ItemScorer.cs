namespace Flow.Launcher.Plugin.Tarkov
{
    public static class ItemScorer
    {
        public const int FUZZY_LIMIT = 600;

        private const int EXACT_SHORT_NAME = 1000;
        private const int EXACT_NAME = 990;
        private const int NAME_PREFIX = 900;
        private const int SHORT_NAME_PREFIX = 880;
        private const int WORD_PREFIX = 800;
        private const int CONTAINS = 700;

        public static int Score(TarkovItem item, string phoneticQuery)
        {
            if (phoneticQuery.Length == 0)
            {
                return 0;
            }

            if (item.PhoneticShortName == phoneticQuery)
            {
                return EXACT_SHORT_NAME;
            }

            if (item.PhoneticName == phoneticQuery)
            {
                return EXACT_NAME;
            }

            if (item.PhoneticName.StartsWith(phoneticQuery, StringComparison.Ordinal))
            {
                return NAME_PREFIX;
            }

            if (item.PhoneticShortName.StartsWith(phoneticQuery, StringComparison.Ordinal))
            {
                return SHORT_NAME_PREFIX;
            }

            if (item.PhoneticSearchText.Contains(" " + phoneticQuery, StringComparison.Ordinal))
            {
                return WORD_PREFIX;
            }

            if (item.PhoneticSearchText.Contains(phoneticQuery, StringComparison.Ordinal))
            {
                return CONTAINS;
            }

            return 0;
        }
    }
}

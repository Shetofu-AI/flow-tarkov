using System.Globalization;

namespace Flow.Launcher.Plugin.Tarkov
{
    public static class PriceFormatter
    {
        private static readonly CultureInfo RUSSIAN = CultureInfo.GetCultureInfo("ru-RU");

        public static string Rubles(int value)
        {
            return value.ToString("N0", RUSSIAN) + " ₽";
        }

        public static string Time(DateTimeOffset value)
        {
            return value.ToLocalTime().ToString("HH:mm", RUSSIAN);
        }
    }
}

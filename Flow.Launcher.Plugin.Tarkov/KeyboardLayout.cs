using System.Text;

namespace Flow.Launcher.Plugin.Tarkov
{
    public static class KeyboardLayout
    {
        private const string LATIN = "qwertyuiop[]asdfghjkl;'zxcvbnm,.`";
        private const string CYRILLIC = "йцукенгшщзхъфывапролджэячсмитьбюё";

        public static string Swap(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            StringBuilder builder = new StringBuilder(text.Length);
            foreach (char character in text)
            {
                builder.Append(SwapCharacter(character));
            }

            return builder.ToString();
        }

        private static char SwapCharacter(char character)
        {
            char lower = char.ToLowerInvariant(character);
            bool isUpper = char.IsUpper(character);

            int latinIndex = LATIN.IndexOf(lower);
            if (latinIndex >= 0)
            {
                char mapped = CYRILLIC[latinIndex];
                return isUpper ? char.ToUpperInvariant(mapped) : mapped;
            }

            int cyrillicIndex = CYRILLIC.IndexOf(lower);
            if (cyrillicIndex >= 0)
            {
                char mapped = LATIN[cyrillicIndex];
                return isUpper ? char.ToUpperInvariant(mapped) : mapped;
            }

            return character;
        }
    }
}

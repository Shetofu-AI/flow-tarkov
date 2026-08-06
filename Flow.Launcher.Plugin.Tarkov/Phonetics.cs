using System.Text;

namespace Flow.Launcher.Plugin.Tarkov
{
    public static class Phonetics
    {
        private const string CYRILLIC = "абвгдеёжзийклмнопрстуфхцчшщъыьэюя";
        private static readonly string[] LATIN =
        {
            "a", "b", "v", "g", "d", "e", "e", "zh", "z", "i", "i", "k", "l", "m", "n", "o", "p",
            "r", "s", "t", "u", "f", "h", "ts", "ch", "sh", "sh", "", "i", "", "e", "yu", "ya"
        };

        public static string Key(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(text.Length + 4);
            foreach (char character in text.ToLowerInvariant())
            {
                Append(builder, character);
            }

            return Collapse(builder.ToString());
        }

        private static void Append(StringBuilder builder, char character)
        {
            int cyrillicIndex = CYRILLIC.IndexOf(character);
            if (cyrillicIndex >= 0)
            {
                builder.Append(LATIN[cyrillicIndex]);
                return;
            }

            if (character >= '0' && character <= '9')
            {
                builder.Append(character);
                return;
            }

            if (character < 'a' || character > 'z')
            {
                builder.Append(' ');
                return;
            }

            switch (character)
            {
                case 'w':
                    builder.Append('v');
                    return;
                case 'x':
                    builder.Append("ks");
                    return;
                case 'q':
                case 'c':
                    builder.Append('k');
                    return;
                case 'y':
                    builder.Append('i');
                    return;
                default:
                    builder.Append(character);
                    return;
            }
        }

        private static string Collapse(string text)
        {
            StringBuilder builder = new StringBuilder(text.Length);
            char previous = '\0';

            foreach (char character in text)
            {
                if (character == previous)
                {
                    continue;
                }

                if (character == ' ' && builder.Length == 0)
                {
                    continue;
                }

                builder.Append(character);
                previous = character;
            }

            return builder.ToString().TrimEnd();
        }
    }
}

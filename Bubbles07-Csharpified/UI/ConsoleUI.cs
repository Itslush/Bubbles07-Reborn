namespace Continuance.UI
{
    public static class ConsoleUI
    {
        public const string T_Branch = "├";
        public const string T_Vertical = "│";
        public const string T_End = "└";
        public const string T_Horz = "─";
        public const string T_TopLeft = "┌";
        public const string T_TopRight = "┐";
        public const string T_BottomLeft = "└";
        public const string T_BottomRight = "┘";
        public const string T_HorzBar = "─";

        public static string TreeLine(string connector, string content) => $"{T_Vertical}   {connector}{T_Horz} {content}";

        public static void PrintMenuTitle(string title)
        {
            _ = Math.Max(title.Length + 4, 50);
            int width;
            try { width = Math.Min(Console.WindowWidth - 1, Math.Max(title.Length + 4, 50)); } catch { width = 50; }
            string line = new(T_HorzBar[0], width - 2);
            int totalPadding = width - 2 - title.Length;
            int leftPadding = totalPadding / 2;
            int rightPadding = totalPadding - leftPadding;

            Console.WriteLine($"\n{T_TopLeft}{line}{T_TopRight}");
            Console.WriteLine($"{T_Vertical}{new string(' ', leftPadding)}{title}{new string(' ', rightPadding)}{T_Vertical}");
            Console.WriteLine($"{T_Vertical}{line}{T_Vertical}");
        }

        public static void PrintMenuFooter(string prompt = "Choose option") => Console.Write($"{T_BottomLeft}{T_Horz}{T_Horz}[?] {prompt}: ");

        public static string Truncate(string? value, int maxLength = 150)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            maxLength = Math.Max(0, maxLength);

            if (value.Length <= maxLength)
            {
                return value;
            }
            else
            {
                return value[..maxLength] + "...";
            }
        }
        public static void WriteLineInsideBox(string message) => Console.WriteLine($"{T_Vertical}   {message}");

        public static void WriteErrorLine(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"   [!] {message}");
            Console.ResetColor();
        }
        public static void WriteSuccessLine(string message)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"   [+] {message}");
            Console.ResetColor();
        }
        public static void WriteInfoLine(string message)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"   [*] {message}");
            Console.ResetColor();
        }
        public static void WriteWarningLine(string message)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"   [?] {message}");
            Console.ResetColor();
        }
    }
}
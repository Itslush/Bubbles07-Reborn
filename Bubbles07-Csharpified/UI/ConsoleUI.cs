using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace
    _Csharpified.UI
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
            string line = new string(T_HorzBar[0], title.Length + 4);
            Console.WriteLine($"\n{T_TopLeft}{line}{T_TopRight}");
            Console.WriteLine($"{T_Vertical}  {title}  {T_Vertical}");
            Console.WriteLine($"{T_Vertical}{line}{T_Vertical}");
        }
        public static void PrintMenuFooter(string prompt = "Choose option") => Console.Write($"{T_BottomLeft}{T_Horz}{T_Horz}{T_Horz}[?] {prompt}: ");

        public static string Truncate(string? value, int maxLength = 30) => string.IsNullOrEmpty(value) ? string.Empty : (value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...");

        public static void WriteLineInsideBox(string message) => Console.WriteLine($"{T_Vertical}   {message}");
        public static void WriteErrorLine(string message) => Console.WriteLine($"{T_Vertical}   [!] {message}");
        public static void WriteSuccessLine(string message) => Console.WriteLine($"{T_Vertical}   [+] {message}");
        public static void WriteInfoLine(string message) => Console.WriteLine($"{T_Vertical}   [*] {message}");

    }
}
using _Csharpified.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace
    _Csharpified.UI
{
    public class MainMenu
    {
        private readonly AccountManager _accountManager;
        private readonly ActionsMenu _actionsMenu; // Dependency for sub-menu

        public MainMenu(AccountManager accountManager, ActionsMenu actionsMenu)
        {
            _accountManager = accountManager ?? throw new ArgumentNullException(nameof(accountManager));
            _actionsMenu = actionsMenu ?? throw new ArgumentNullException(nameof(actionsMenu));
        }

        public async Task Show()
        {
            bool exit = false;
            while (!exit)
            {
                int totalAccounts = _accountManager.GetAllAccounts().Count;
                int selectedCount = _accountManager.GetSelectedAccountIndices().Count;

                ConsoleUI.PrintMenuTitle("Main Menu");

                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"1. Add Account (Single Cookie)"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"2. Import Cookies (Bulk Paste)"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"3. List Accounts (Show Roster - {totalAccounts} loaded)"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"4. Select/Deselect (Toggle Selection - {selectedCount} selected)"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"5. Show Selected"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"6. Actions Menu (Execute Tasks on Selected)"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"7. Adjust Rate Limits (API={AppConfig.CurrentApiDelayMs}ms, Friend={AppConfig.CurrentFriendActionDelayMs}ms)"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_End, $"0. Exit"));

                ConsoleUI.PrintMenuFooter();
                string? choice = Console.ReadLine();

                switch (choice)
                {
                    case "1": await AddAccountUI(); break;
                    case "2": await ImportCookiesFromInputUI(); break;
                    case "3": ListAccountsUI(); break;
                    case "4": SelectAccountsUI(); break;
                    case "5": ShowSelectedAccountsUI(); break;
                    case "6":
                        if (selectedCount == 0) { ConsoleUI.WriteErrorLine("No accounts selected. Use option 4 first."); }
                        else { await _actionsMenu.Show(); } // Call the sub-menu
                        break;
                    case "7": _actionsMenu.AdjustRateLimitsUI(); break; // Delegate rate limit UI to ActionsMenu or a dedicated class
                    case "0": exit = true; break;
                    default: ConsoleUI.WriteErrorLine("Invalid choice. Please enter a number from the menu."); break;
                }
                if (!exit) await Task.Delay(100);
            }
        }

        private async Task AddAccountUI()
        {
            Console.Write($"[?] Enter Roblox Cookie (.ROBLOSECURITY starts with _ | WARNING:-): ");
            string? cookie = Console.ReadLine()?.Trim();
            if (!string.IsNullOrWhiteSpace(cookie))
            {
                await _accountManager.AddAccountAsync(cookie);
            }
            else
            {
                Console.WriteLine($"[!] Input empty. Aborting.");
            }
        }

        private async Task ImportCookiesFromInputUI()
        {
            Console.WriteLine($"[?] Paste cookies below (one per line). Format: _ | WARNING:-...");
            Console.WriteLine($"Press Enter on an empty line when finished.");

            var cookiesToImport = new List<string>();
            string? line; int lineNum = 1;
            Console.Write($"[{lineNum++}] >>> ");
            while (!string.IsNullOrWhiteSpace(line = Console.ReadLine()))
            {
                string trimmedCookie = line.Trim();
                if (!string.IsNullOrEmpty(trimmedCookie) && trimmedCookie.StartsWith("_|WARNING:-", StringComparison.OrdinalIgnoreCase))
                {
                    if (!cookiesToImport.Contains(trimmedCookie)) { cookiesToImport.Add(trimmedCookie); }
                    else { Console.WriteLine($"[!] Skipping line {lineNum - 1}: Duplicate cookie in input batch."); }
                }
                else if (!string.IsNullOrEmpty(trimmedCookie)) { Console.WriteLine($"[!] Skipping line {lineNum - 1}: Invalid format ({ConsoleUI.Truncate(trimmedCookie, 15)}...)"); }
                Console.Write($"[{lineNum++}] >>> ");
            }

            if (cookiesToImport.Count > 0)
            {
                await _accountManager.ImportAccountsAsync(cookiesToImport);
            }
            else
            {
                Console.WriteLine($"[!] No valid, unique cookies provided for import.");
            }
        }

        private void ListAccountsUI()
        {
            var accounts = _accountManager.GetAllAccounts();
            var selectedIndices = _accountManager.GetSelectedAccountIndices();
            Console.WriteLine($"\n---[ Account Roster ({accounts.Count} Total) ]---");
            if (accounts.Count == 0) { Console.WriteLine($"No accounts loaded. Use option 1 or 2 to add accounts."); return; }

            for (int i = 0; i < accounts.Count; i++)
            {
                string selectionMarker = selectedIndices.Contains(i) ? "[*]" : "[ ]";
                Console.WriteLine($"{selectionMarker} {i + 1}: {accounts[i]}");
            }
            Console.WriteLine($"Use option 4 to select/deselect accounts for actions.");
        }

        private void SelectAccountsUI()
        {
            ListAccountsUI();
            if (_accountManager.GetAllAccounts().Count == 0) return;

            Console.WriteLine($"\n[?] Enter numbers to toggle selection (e.g., 1 3 5).");
            Console.WriteLine($" Commands: 'all', 'none'/'clear', 'valid', 'invalid', 'failed'");
            Console.Write($"Selection: ");
            string? input = Console.ReadLine()?.ToLower().Trim();

            if (string.IsNullOrWhiteSpace(input)) { Console.WriteLine($"[!] No input provided. Selection unchanged."); return; }

            bool commandProcessed = true;
            switch (input)
            {
                case "all": _accountManager.SelectAll(); break;
                case "none": case "clear": _accountManager.SelectNone(); break;
                case "valid": _accountManager.SelectValid(); break;
                case "invalid": _accountManager.SelectInvalid(); break;
                case "failed": _accountManager.SelectFailedVerification(); break;
                default: commandProcessed = false; break;
            }

            if (!commandProcessed)
            {
                var indices = input.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                var numericIndices = new List<int>();
                int invalidNum = 0;
                foreach (var indexStr in indices)
                {
                    if (int.TryParse(indexStr, out int userIndex))
                    {
                        numericIndices.Add(userIndex); // Keep 1-based for manager
                    }
                    else { Console.WriteLine($"[!] Invalid input format: '{indexStr}'. Skipped."); invalidNum++; }
                }

                if (numericIndices.Count > 0)
                {
                    _accountManager.UpdateSelection(numericIndices);
                }
                else if (invalidNum == indices.Length)
                {
                    Console.WriteLine($"[!] No valid numbers entered. Selection unchanged.");
                }
            }
            ShowSelectedAccountsUI();
        }

        private void ShowSelectedAccountsUI()
        {
            var selectedIndices = _accountManager.GetSelectedAccountIndices();
            var allAccounts = _accountManager.GetAllAccounts();
            Console.WriteLine($"\n---[ Currently Selected ({selectedIndices.Count}) ]---");
            if (selectedIndices.Count == 0) { Console.WriteLine($"None selected. Use option 4 to select accounts."); return; }
            foreach (int index in selectedIndices.OrderBy(i => i))
            {
                if (index >= 0 && index < allAccounts.Count) { Console.WriteLine($" {index + 1}: {allAccounts[index]}"); }
                else { Console.WriteLine($" [!] Error: Selected index {index} is out of bounds."); }
            }
            Console.WriteLine($"----------------------------------");
        }
    }
}
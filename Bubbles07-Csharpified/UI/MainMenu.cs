using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using _Csharpified;
using Core;
using Models;

namespace UI
{
    public class MainMenu
    {
        private readonly AccountManager _accountManager;
        private readonly ActionsMenu _actionsMenu;

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
                int validCount = _accountManager.GetAllAccounts().Count(a => a.IsValid);
                int invalidCount = totalAccounts - validCount;

                Console.Clear();
                ConsoleUI.PrintMenuTitle("Bubbles07 - Reborn");

                ConsoleUI.WriteLineInsideBox($"Accounts Loaded: {totalAccounts} ({validCount} Valid / {invalidCount} Invalid)");
                ConsoleUI.WriteLineInsideBox($"Currently Selected: {selectedCount}");
                ConsoleUI.WriteLineInsideBox("");

                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"1. Add Account (Single Cookie)"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"2. Import Cookies (Bulk Paste)"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"3. List Accounts (Show Roster)"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"4. Select / Deselect Accounts"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"5. Show Selected Accounts"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"6. Show Accounts with Full Cookies"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"7. Actions Menu (Execute Tasks on Selected)"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"8. Adjust Rate Limits & Timeout"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_End, $"0. Exit"));

                ConsoleUI.PrintMenuFooter();
                string? choice = Console.ReadLine();

                switch (choice)
                {
                    case "1":
                        Console.Clear();
                        await AddAccountUI();
                        break;
                    case "2":
                        Console.Clear();
                        await ImportCookiesFromInputUI();
                        break;
                    case "3":
                        Console.Clear();
                        ListAccountsUI();
                        break;
                    case "4":
                        Console.Clear();
                        SelectAccountsUI();
                        break;
                    case "5":
                        Console.Clear();
                        ShowSelectedAccountsUI();
                        break;
                    case "6":
                        Console.Clear();
                        ShowAccountsWithCookiesUI();
                        break;
                    case "7":
                        if (selectedCount == 0) { ConsoleUI.WriteErrorLine("No accounts selected. Use option 4 first."); }
                        else { await _actionsMenu.Show(); }
                        break;
                    case "8":
                        ActionsMenu.AdjustRateLimitsUI();
                        break;
                    case "0":
                        Console.WriteLine("[*] Exiting application...");
                        exit = true;
                        break;
                    default: ConsoleUI.WriteErrorLine("Invalid choice. Please enter a number from the menu."); break;
                }

                if (!exit && choice != "7")
                {
                    Console.WriteLine("\nPress Enter to return to the Main Menu...");
                    Console.ReadLine();
                }
            }
        }

        private async Task AddAccountUI()
        {
            ConsoleUI.PrintMenuTitle("Add Single Account");
            ConsoleUI.WriteLineInsideBox("Enter the full .ROBLOSECURITY cookie value below.");
            ConsoleUI.WriteLineInsideBox("It should start with: _|WARNING:-");
            Console.Write($"{ConsoleUI.T_Vertical}   Cookie: ");
            string? cookie = Console.ReadLine()?.Trim();

            if (!string.IsNullOrWhiteSpace(cookie))
            {
                ConsoleUI.WriteInfoLine("Attempting to add and validate...");
                bool added = await _accountManager.AddAccountAsync(cookie);
                if (added)
                {
                    ConsoleUI.WriteSuccessLine("Account processed. Check roster (Option 3).");
                    var addedAcc = _accountManager.GetAllAccounts().LastOrDefault(a => a.Cookie == cookie);
                    if (addedAcc != null)
                    {
                        ConsoleUI.WriteInfoLine($"Added: {addedAcc}");
                    }
                }
                else { ConsoleUI.WriteErrorLine("Failed to add account (duplicate or invalid - see messages above)."); }
            }
            else
            {
                ConsoleUI.WriteErrorLine("Input empty. Aborting.");
            }
            Console.WriteLine(ConsoleUI.T_BottomLeft + new string(ConsoleUI.T_HorzBar[0], 50) + ConsoleUI.T_BottomRight);
        }

        private async Task ImportCookiesFromInputUI()
        {
            ConsoleUI.PrintMenuTitle("Import Cookies");
            ConsoleUI.WriteLineInsideBox("Paste one cookie per line below. Format: _|WARNING:-...");
            ConsoleUI.WriteLineInsideBox("Press Enter on an empty line when finished.");
            ConsoleUI.WriteLineInsideBox("");

            var cookiesToImport = new List<string>();
            string? line;
            int lineNum = 1;
            Console.Write($"{ConsoleUI.T_Vertical}   [{lineNum++,3}] >>> ");
            while (!string.IsNullOrWhiteSpace(line = Console.ReadLine()))
            {
                string trimmedCookie = line.Trim();
                if (!string.IsNullOrEmpty(trimmedCookie) && trimmedCookie.StartsWith("_|WARNING:-", StringComparison.OrdinalIgnoreCase))
                {
                    if (!cookiesToImport.Contains(trimmedCookie))
                    {
                        cookiesToImport.Add(trimmedCookie);
                    }
                    else
                    {
                        Console.WriteLine($"{ConsoleUI.T_Vertical}   [!] Skipping line {lineNum - 1}: Duplicate cookie in this input batch.");
                    }
                }
                else if (!string.IsNullOrEmpty(trimmedCookie))
                {
                    Console.WriteLine($"{ConsoleUI.T_Vertical}   [!] Skipping line {lineNum - 1}: Invalid format ({ConsoleUI.Truncate(trimmedCookie, 15)}...)");
                }
                Console.Write($"{ConsoleUI.T_Vertical}   [{lineNum++,3}] >>> ");
            }

            if (cookiesToImport.Count > 0)
            {
                ConsoleUI.WriteInfoLine($"\nAttempting import of {cookiesToImport.Count} unique cookie(s) provided...");
                await _accountManager.ImportAccountsAsync(cookiesToImport);
            }
            else
            {
                ConsoleUI.WriteErrorLine("No valid, unique cookies provided for import.");
            }
            Console.WriteLine(ConsoleUI.T_BottomLeft + new string(ConsoleUI.T_HorzBar[0], 50) + ConsoleUI.T_BottomRight);
        }

        private void ListAccountsUI(bool showFooter = true)
        {
            var accounts = _accountManager.GetAllAccounts();
            var selectedIndices = _accountManager.GetSelectedAccountIndices();

            if (accounts.Count == 0)
            {
                ConsoleUI.WriteLineInsideBox("No accounts loaded. Use option 1 or 2 to add accounts.");
                if (showFooter) Console.WriteLine(ConsoleUI.T_BottomLeft + new string(ConsoleUI.T_HorzBar[0], 40) + ConsoleUI.T_BottomRight);
                return;
            }

            ConsoleUI.WriteLineInsideBox("Format: [Sel] No: [Status] ID, User (Verify Status)");
            ConsoleUI.WriteLineInsideBox("--------------------------------------------------");

            for (int i = 0; i < accounts.Count; i++)
            {
                string selectionMarker = selectedIndices.Contains(i) ? "[*]" : "[ ]";
                var status = _accountManager.GetVerificationStatus(accounts[i].UserId);
                string statusMarker = status switch
                {
                    VerificationStatus.Passed => "(PASS)",
                    VerificationStatus.Failed => "(FAIL)",
                    VerificationStatus.Error => "(ERR)",
                    _ => ""
                };
                ConsoleUI.WriteLineInsideBox($"{selectionMarker} {i + 1,3}: {accounts[i]} {statusMarker}");
            }
            ConsoleUI.WriteLineInsideBox("--------------------------------------------------");
            if (showFooter)
            {
                ConsoleUI.WriteLineInsideBox($"Use option 4 to select/deselect accounts.");
                Console.WriteLine(ConsoleUI.T_BottomLeft + new string(ConsoleUI.T_HorzBar[0], 50) + ConsoleUI.T_BottomRight);
            }
        }

        private void ShowAccountsWithCookiesUI()
        {
            ConsoleUI.PrintMenuTitle("Accounts with Full Cookies");
            var accounts = _accountManager.GetAllAccounts();

            if (accounts.Count == 0)
            {
                ConsoleUI.WriteLineInsideBox("No accounts loaded.");
                Console.WriteLine(ConsoleUI.T_BottomLeft + new string(ConsoleUI.T_HorzBar[0], 40) + ConsoleUI.T_BottomRight);
                return;
            }

            ConsoleUI.WriteLineInsideBox("Format: Number: [Status] User (ID)");
            ConsoleUI.WriteLineInsideBox("         Cookie: _ | WARNING:-...");
            ConsoleUI.WriteLineInsideBox("--------------------------------------------------");

            for (int i = 0; i < accounts.Count; i++)
            {
                ConsoleUI.WriteLineInsideBox($"{i + 1,3}: {accounts[i]}");
                ConsoleUI.WriteLineInsideBox($"      Cookie: {accounts[i].Cookie}");
            }
            ConsoleUI.WriteLineInsideBox("--------------------------------------------------");
            Console.WriteLine(ConsoleUI.T_BottomLeft + new string(ConsoleUI.T_HorzBar[0], 50) + ConsoleUI.T_BottomRight);
        }

        private void SelectAccountsUI()
        {
            ConsoleUI.PrintMenuTitle("Select / Deselect Accounts");
            ListAccountsUI(showFooter: false);

            var accounts = _accountManager.GetAllAccounts();
            if (accounts.Count == 0)
            {
                Console.WriteLine(ConsoleUI.T_BottomLeft + new string(ConsoleUI.T_HorzBar[0], 50) + ConsoleUI.T_BottomRight);
                return;
            }

            ConsoleUI.WriteLineInsideBox("\nEnter numbers to toggle selection (e.g., 1 3 5).");
            ConsoleUI.WriteLineInsideBox("Commands: 'all', 'none', 'valid', 'invalid', 'failed'");
            ConsoleUI.WriteWarningLine("Selection Input");
            string? input = Console.ReadLine()?.ToLower().Trim();

            if (string.IsNullOrWhiteSpace(input)) { ConsoleUI.WriteErrorLine("No input provided. Selection unchanged."); return; }

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
                var indices = input.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var numericIndices = new List<int>();
                int invalidNumCount = 0;

                foreach (var indexStr in indices)
                {
                    if (int.TryParse(indexStr, out int userIndex))
                    {
                        if (userIndex >= 1 && userIndex <= accounts.Count)
                        {
                            numericIndices.Add(userIndex);
                        }
                        else
                        {
                            ConsoleUI.WriteErrorLine($"Input number '{userIndex}' out of range (1-{accounts.Count}). Skipped.");
                            invalidNumCount++;
                        }
                    }
                    else { ConsoleUI.WriteErrorLine($"Invalid input format: '{indexStr}'. Skipped."); invalidNumCount++; }
                }

                if (numericIndices.Count > 0)
                {
                    _accountManager.UpdateSelection(numericIndices);
                }
                else if (invalidNumCount == indices.Length && indices.Length > 0)
                {
                    ConsoleUI.WriteErrorLine("No valid numbers entered. Selection unchanged.");
                }
                else if (indices.Length == 0 && !string.IsNullOrWhiteSpace(input))
                {
                    ConsoleUI.WriteErrorLine("Unrecognized command or invalid input. Selection unchanged.");
                }
            }
            Console.WriteLine();
            ShowSelectedAccountsUI(showFooter: true);
        }

        private void ShowSelectedAccountsUI(bool showFooter = true)
        {
            var selectedIndices = _accountManager.GetSelectedAccountIndices();
            var allAccounts = _accountManager.GetAllAccounts();

            ConsoleUI.WriteInfoLine($"--- Currently Selected ({selectedIndices.Count}) ---");

            if (selectedIndices.Count == 0)
            {
                ConsoleUI.WriteLineInsideBox("None selected.");
                if (showFooter) Console.WriteLine(ConsoleUI.T_BottomLeft + new string(ConsoleUI.T_HorzBar[0], 40) + ConsoleUI.T_BottomRight);
                return;
            }

            foreach (int index in selectedIndices.OrderBy(i => i))
            {
                if (index >= 0 && index < allAccounts.Count)
                {
                    var account = allAccounts[index];
                    var status = _accountManager.GetVerificationStatus(account.UserId);
                    string statusMarker = status switch
                    {
                        VerificationStatus.Passed => "(PASS)",
                        VerificationStatus.Failed => "(FAIL)",
                        VerificationStatus.Error => "(ERR)",
                        _ => ""
                    };
                    ConsoleUI.WriteLineInsideBox($" {index + 1,3}: {account} {statusMarker}");
                }
                else { ConsoleUI.WriteErrorLine($"Error: Selected index {index} is out of bounds!"); }
            }
            if (showFooter) Console.WriteLine(ConsoleUI.T_BottomLeft + new string(ConsoleUI.T_HorzBar[0], 50) + ConsoleUI.T_BottomRight);
        }
    }
}
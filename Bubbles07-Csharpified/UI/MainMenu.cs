using Newtonsoft.Json;
using Continuance.Models;
using Continuance.Core;
using Continuance;

namespace Continuance.UI
{
    public class MainMenu(AccountManager accountManager, ActionsMenu actionsMenu)
    {
        private readonly AccountManager _accountManager = accountManager ?? throw new ArgumentNullException(nameof(accountManager));
        private readonly ActionsMenu _actionsMenu = actionsMenu ?? throw new ArgumentNullException(nameof(actionsMenu));
        private const string SettingsFilePath = "settings.json";

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
                ConsoleUI.PrintMenuTitle("Continuance");

                ConsoleUI.WriteLineInsideBox($"Accounts Loaded: {totalAccounts} ({validCount} Valid / {invalidCount} Invalid)");
                ConsoleUI.WriteLineInsideBox($"Currently Selected: {selectedCount}");
                ConsoleUI.WriteLineInsideBox("");

                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $" 1. Add Account (Single Cookie)"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $" 2. Import Cookies from File (.txt)"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $" 3. Import Cookies (Bulk Paste)"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $" 4. List Accounts (Show Account Pool)"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $" 5. Select / Deselect Accounts"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $" 6. Show Selected Accounts"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $" 7. Show Accounts with Full Cookies"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $" 8. Actions Menu (Execute Tasks on Selected)"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $" 9. Adjust Rate Limits, Timeout & Retries"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"10. Export Cookies & Usernames to File"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"11. Save Current Settings to '{SettingsFilePath}'"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_End, $" 0. Exit"));

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
                        await ImportCookiesFromFileUI();
                        break;
                    case "3":
                        Console.Clear();
                        await ImportCookiesFromInputUI();
                        break;
                    case "4":
                        Console.Clear();
                        ConsoleUI.PrintMenuTitle("Account Pool");
                        ListAccountsUI();
                        break;
                    case "5":
                        Console.Clear();
                        SelectAccountsUI();
                        break;
                    case "6":
                        Console.Clear();
                        ConsoleUI.PrintMenuTitle("Selected Accounts");
                        ShowSelectedAccountsUI();
                        break;
                    case "7":
                        Console.Clear();
                        ShowAccountsWithCookiesUI();
                        break;
                    case "8":
                        if (selectedCount == 0)
                        {
                            ConsoleUI.WriteErrorLine("No accounts selected. Use option 5 first.");
                        }
                        else
                        {
                            await _actionsMenu.Show();
                        }
                        break;
                    case "9":
                        ActionsMenu.AdjustRateLimitsUI();
                        break;
                    case "10":
                        Console.Clear();
                        await ExportAccountsToFileUI();
                        break;
                    case "11":
                        SaveCurrentSettingsUI();
                        break;
                    case "0":
                        ConsoleUI.WriteInfoLine("Exiting application...");
                        exit = true;
                        break;
                    default:
                        ConsoleUI.WriteErrorLine("Invalid choice. Please enter a number from the menu.");
                        break;
                }

                if (!exit && choice != "8")
                {
                    Console.WriteLine("\nPress Enter to return to the Main Menu...");
                    Console.ReadLine();
                }
                else if (choice == "8" && selectedCount == 0)
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
                    var addedAcc = _accountManager.GetAllAccounts().LastOrDefault(a => a.Cookie == cookie);
                    if (addedAcc != null)
                    {
                        ConsoleUI.WriteInfoLine($"Processed: {addedAcc}");
                    }
                }
            }
            else
            {
                ConsoleUI.WriteErrorLine("Input empty. Aborting.");
            }
            Console.WriteLine(ConsoleUI.T_BottomLeft + new string(ConsoleUI.T_HorzBar[0], 50) + ConsoleUI.T_BottomRight);
        }

        private async Task ImportCookiesFromFileUI()
        {
            ConsoleUI.PrintMenuTitle("Import Cookies from File");
            ConsoleUI.WriteLineInsideBox("Enter the full path to the text file containing cookies.");
            ConsoleUI.WriteLineInsideBox("The file should have one cookie per line.");
            ConsoleUI.WriteLineInsideBox("Example: C:\\Users\\YourUser\\Desktop\\cookies.txt");
            Console.Write($"{ConsoleUI.T_Vertical}   File Path: ");
            string? filePath = Console.ReadLine()?.Trim();

            if (!string.IsNullOrWhiteSpace(filePath))
            {
                await _accountManager.ImportAccountsFromFileAsync(filePath);
            }
            else
            {
                ConsoleUI.WriteErrorLine("File path cannot be empty. Aborting.");
            }
            Console.WriteLine(ConsoleUI.T_BottomLeft + new string(ConsoleUI.T_HorzBar[0], 50) + ConsoleUI.T_BottomRight);
        }

        private async Task ImportCookiesFromInputUI()
        {
            ConsoleUI.PrintMenuTitle("Import Cookies (Bulk Paste)");
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
                        ConsoleUI.WriteErrorLine($"Skipping line {lineNum - 1}: Duplicate cookie in this input batch.");
                    }
                }
                else if (!string.IsNullOrEmpty(trimmedCookie))
                {
                    ConsoleUI.WriteErrorLine($"Skipping line {lineNum - 1}: Invalid format ({ConsoleUI.Truncate(trimmedCookie, 15)}...)");
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
            int listWidth = 75;
            try { listWidth = Math.Max(75, Console.WindowWidth - 5); } catch { }

            if (accounts.Count == 0)
            {
                ConsoleUI.WriteLineInsideBox("No accounts loaded. Use option 1, 2 or 3 to add accounts.");
                if (showFooter) Console.WriteLine(ConsoleUI.T_BottomLeft + new string(ConsoleUI.T_HorzBar[0], 50) + ConsoleUI.T_BottomRight);
                return;
            }

            ConsoleUI.WriteLineInsideBox(" [Sel]   #  Status  ID          Username              (Verify)");
            ConsoleUI.WriteLineInsideBox(new string('-', listWidth));

            for (int i = 0; i < accounts.Count; i++)
            {
                string selectionMarker = selectedIndices.Contains(i) ? "[*]" : "[ ]";
                var account = accounts[i];
                string statusMarker = account.IsValid ? "[OK]  " : "[BAD] ";
                var verifyStatus = _accountManager.GetVerificationStatus(account.UserId);
                string verifyMarker = verifyStatus switch
                {
                    VerificationStatus.Passed => "(PASS)",
                    VerificationStatus.Failed => "(FAIL)",
                    VerificationStatus.Error => "(ERR)",
                    _ => ""
                };

                string line = $"{selectionMarker} {i + 1,3}  {statusMarker} {account.UserId,-10} {ConsoleUI.Truncate(account.Username, 20).PadRight(20)} {verifyMarker}";
                ConsoleUI.WriteLineInsideBox(line);
            }
            ConsoleUI.WriteLineInsideBox(new string('-', listWidth));
            if (showFooter)
            {
                ConsoleUI.WriteLineInsideBox($"Use option 5 to select/deselect accounts.");
                Console.WriteLine(ConsoleUI.T_BottomLeft + new string(ConsoleUI.T_HorzBar[0], 50) + ConsoleUI.T_BottomRight);
            }
        }

        private void ShowSelectedAccountsUI(bool showFooter = true)
        {
            var selectedIndices = _accountManager.GetSelectedAccountIndices();
            var allAccounts = _accountManager.GetAllAccounts();
            int listWidth = 75;
            try { listWidth = Math.Max(75, Console.WindowWidth - 5); } catch { }


            if (selectedIndices.Count == 0)
            {
                ConsoleUI.WriteLineInsideBox("None selected.");
                if (showFooter) Console.WriteLine(ConsoleUI.T_BottomLeft + new string(ConsoleUI.T_HorzBar[0], 50) + ConsoleUI.T_BottomRight);
                return;
            }
            ConsoleUI.WriteLineInsideBox("   #  Status  ID          Username              (Verify)");
            ConsoleUI.WriteLineInsideBox(new string('-', listWidth));

            foreach (int index in selectedIndices.OrderBy(i => i))
            {
                if (index >= 0 && index < allAccounts.Count)
                {
                    var account = allAccounts[index];
                    string statusMarker = account.IsValid ? "[OK]  " : "[BAD] ";
                    var verifyStatus = _accountManager.GetVerificationStatus(account.UserId);
                    string verifyMarker = verifyStatus switch
                    {
                        VerificationStatus.Passed => "(PASS)",
                        VerificationStatus.Failed => "(FAIL)",
                        VerificationStatus.Error => "(ERR)",
                        _ => ""
                    };
                    string line = $" {index + 1,3}  {statusMarker} {account.UserId,-10} {ConsoleUI.Truncate(account.Username, 20).PadRight(20)} {verifyMarker}";
                    ConsoleUI.WriteLineInsideBox(line);
                }
                else
                {
                    ConsoleUI.WriteErrorLine($"Internal Error: Selected index {index} is out of bounds!");
                }
            }
            ConsoleUI.WriteLineInsideBox(new string('-', listWidth));
            if (showFooter) Console.WriteLine(ConsoleUI.T_BottomLeft + new string(ConsoleUI.T_HorzBar[0], 50) + ConsoleUI.T_BottomRight);
        }


        private void ShowAccountsWithCookiesUI()
        {
            ConsoleUI.PrintMenuTitle("Accounts with Full Cookies");
            var accounts = _accountManager.GetAllAccounts();
            int listWidth = 75;
            try { listWidth = Math.Max(75, Console.WindowWidth - 5); } catch { }

            if (accounts.Count == 0)
            {
                ConsoleUI.WriteLineInsideBox("No accounts loaded.");
                Console.WriteLine(ConsoleUI.T_BottomLeft + new string(ConsoleUI.T_HorzBar[0], 50) + ConsoleUI.T_BottomRight);
                return;
            }

            ConsoleUI.WriteLineInsideBox("   #  Status  ID          Username");
            ConsoleUI.WriteLineInsideBox("         Cookie: _ | WARNING:-...");
            ConsoleUI.WriteLineInsideBox(new string('-', listWidth));

            for (int i = 0; i < accounts.Count; i++)
            {
                var account = accounts[i];
                string statusMarker = account.IsValid ? "[OK]  " : "[BAD] ";
                ConsoleUI.WriteLineInsideBox($" {i + 1,3}  {statusMarker} {account.UserId,-10} {ConsoleUI.Truncate(account.Username, 20)}");
                ConsoleUI.WriteLineInsideBox($"         Cookie: {account.Cookie}");
                if (i < accounts.Count - 1) ConsoleUI.WriteLineInsideBox("");
            }
            ConsoleUI.WriteLineInsideBox(new string('-', listWidth));
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

            ConsoleUI.WriteLineInsideBox("\nEnter numbers (e.g., 1 3 5) or ranges (e.g., 1-5 8 10-12) to toggle.");
            ConsoleUI.WriteLineInsideBox("Commands: 'all', 'none', 'valid', 'invalid', 'failed'");
            Console.Write($"{ConsoleUI.T_Vertical}   Selection Input: ");
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
                var indicesToToggle = new List<int>();
                var parts = input.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                int invalidCount = 0;

                foreach (var part in parts)
                {
                    if (part.Contains('-'))
                    {
                        var rangeParts = part.Split('-');
                        if (rangeParts.Length == 2 &&
                            int.TryParse(rangeParts[0], out int start) &&
                            int.TryParse(rangeParts[1], out int end) &&
                            start <= end && start >= 1 && end <= accounts.Count)
                        {
                            for (int i = start; i <= end; i++)
                            {
                                if (!indicesToToggle.Contains(i))
                                    indicesToToggle.Add(i);
                            }
                        }
                        else
                        {
                            ConsoleUI.WriteErrorLine($"Invalid range format or bounds: '{part}'. Skipped.");
                            invalidCount++;
                        }
                    }
                    else if (int.TryParse(part, out int userIndex))
                    {
                        if (userIndex >= 1 && userIndex <= accounts.Count)
                        {
                            if (!indicesToToggle.Contains(userIndex))
                                indicesToToggle.Add(userIndex);
                        }
                        else
                        {
                            ConsoleUI.WriteErrorLine($"Input number '{userIndex}' out of range (1-{accounts.Count}). Skipped.");
                            invalidCount++;
                        }
                    }
                    else
                    {
                        ConsoleUI.WriteErrorLine($"Invalid input format: '{part}'. Skipped.");
                        invalidCount++;
                    }
                }

                if (indicesToToggle.Count > 0)
                {
                    _accountManager.UpdateSelection(indicesToToggle);
                }
                else if (invalidCount == parts.Length && parts.Length > 0)
                {
                    ConsoleUI.WriteErrorLine("No valid numbers or ranges entered. Selection unchanged.");
                }
                else if (parts.Length == 0 && !string.IsNullOrWhiteSpace(input))
                {
                    ConsoleUI.WriteErrorLine("Unrecognized command or invalid input format. Selection unchanged.");
                }
            }

            Console.WriteLine();
            ConsoleUI.PrintMenuTitle("Updated Selection");
            ShowSelectedAccountsUI(showFooter: true);
        }

        private async Task ExportAccountsToFileUI()
        {
            ConsoleUI.PrintMenuTitle("Export Cookies & Usernames");

            if (_accountManager.GetAllAccounts().Count == 0)
            {
                ConsoleUI.WriteErrorLine("No accounts are loaded to export.");
                Console.WriteLine(ConsoleUI.T_BottomLeft + new string(ConsoleUI.T_HorzBar[0], 50) + ConsoleUI.T_BottomRight);
                return;
            }

            ConsoleUI.WriteLineInsideBox("Enter the desired filename for the export.");
            ConsoleUI.WriteLineInsideBox("Example: exported_accounts.txt");
            Console.Write($"{ConsoleUI.T_Vertical}   Filename (or press Enter for 'cookies_export.txt'): ");
            string? fileName = Console.ReadLine()?.Trim();

            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = "cookies_export.txt";
                ConsoleUI.WriteInfoLine($"Using default filename: {fileName}");
            }

            if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                ConsoleUI.WriteErrorLine($"Filename '{fileName}' contains invalid characters. Aborting.");
                Console.WriteLine(ConsoleUI.T_BottomLeft + new string(ConsoleUI.T_HorzBar[0], 50) + ConsoleUI.T_BottomRight);
                return;
            }

            ConsoleUI.WriteInfoLine($"Attempting to export to '{fileName}'...");
            _ = await _accountManager.ExportAccountsToFileAsync(fileName);

            Console.WriteLine(ConsoleUI.T_BottomLeft + new string(ConsoleUI.T_HorzBar[0], 50) + ConsoleUI.T_BottomRight);
        }

        private static void SaveCurrentSettingsUI()
        {
            Console.Clear();
            ConsoleUI.PrintMenuTitle("Save Current Settings");
            ConsoleUI.WriteInfoLine("This will save the current rate limits, timeouts, retries, action defaults,");
            ConsoleUI.WriteInfoLine($"and other configurable values to '{SettingsFilePath}'.");
            ConsoleUI.WriteLineInsideBox("These settings will be loaded the next time the application starts.");
            Console.Write($"{ConsoleUI.T_Vertical}   Save current settings now? (y/n): ");
            if (Console.ReadLine()?.Trim().ToLower() == "y")
            {
                SaveSettings(AppConfig.GetCurrentSettings());
            }
            else
            {
                ConsoleUI.WriteErrorLine("Save cancelled.");
            }
            Console.WriteLine(ConsoleUI.T_BottomLeft + new string(ConsoleUI.T_HorzBar[0], 50) + ConsoleUI.T_BottomRight);
        }

        private static void SaveSettings(AppSettings settings)
        {
            try
            {
                string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(SettingsFilePath, json);
                ConsoleUI.WriteSuccessLine($"Settings successfully saved to {SettingsFilePath}");
            }
            catch (Exception ex)
            {
                ConsoleUI.WriteErrorLine($"Failed to save settings to {SettingsFilePath}: {ex.Message}");
            }
        }
    }
}
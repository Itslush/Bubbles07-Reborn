using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Models;
using Roblox.Services;
using UI;
using System.Text;

namespace Core
{
    public class AccountManager
    {
        private readonly List<Account> _accounts = [];
        private readonly List<int> _selectedAccountIndices = [];
        private readonly Dictionary<long, (VerificationStatus Status, string Details)> _verificationResults = new Dictionary<long, (VerificationStatus, string)>();
        private readonly AuthenticationService _authService;
        private readonly Lock _lock = new();

        public AccountManager(AuthenticationService authService)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        }

        public IReadOnlyList<Account> GetAllAccounts()
        {
            lock (_lock)
            {
                return _accounts.AsReadOnly();
            }
        }

        public List<int> GetSelectedAccountIndices()
        {
            lock (_lock)
            {
                return new List<int>(_selectedAccountIndices);
            }
        }

        public List<Account> GetSelectedAccounts()
        {
            lock (_lock)
            {
                return _selectedAccountIndices
                    .Where(index => index >= 0 && index < _accounts.Count)
                    .Select(index => _accounts[index])
                    .ToList();
            }
        }

        public async Task<bool> AddAccountAsync(string cookie)
        {
            if (string.IsNullOrWhiteSpace(cookie) || !cookie.StartsWith("_|WARNING:-", StringComparison.OrdinalIgnoreCase))
            {
                ConsoleUI.WriteErrorLine("Invalid Format/Empty Cookie.");
                return false;
            }

            lock (_lock)
            {
                if (_accounts.Any(a => a.Cookie == cookie))
                {
                    ConsoleUI.WriteErrorLine("Duplicate: This cookie is already in the roster.");
                    return false;
                }
            }

            ConsoleUI.WriteInfoLine("Validating cookie integrity...");
            var (isValid, userId, username) = await _authService.ValidateCookieAsync(cookie);

            if (isValid && userId > 0)
            {
                ConsoleUI.WriteSuccessLine($"Cookie Valid :: User: {username} (ID: {userId}). Fetching XCSRF token...");
                string xcsrfRaw = await AuthenticationService.FetchXCSRFTokenAsync(cookie);
                string xcsrf = xcsrfRaw?.Trim() ?? "";

                var newAccount = new Account
                {
                    Cookie = cookie,
                    UserId = userId,
                    Username = username,
                    XcsrfToken = xcsrf,
                    IsValid = !string.IsNullOrEmpty(xcsrf)
                };

                lock (_lock)
                {
                    if (!_accounts.Any(a => a.Cookie == newAccount.Cookie))
                    {
                        _accounts.Add(newAccount);
                    }
                    else
                    {
                        ConsoleUI.WriteErrorLine("Duplicate: Cookie added by another thread concurrently.");
                        return false;
                    }
                }

                if (newAccount.IsValid)
                {
                    ConsoleUI.WriteSuccessLine($"Account Secured & Added to roster. ({_accounts.Count} total)");
                    return true;
                }
                else
                {
                    ConsoleUI.WriteErrorLine($"XCSRF Fetch Failed. Account added but marked as INVALID. Actions requiring XCSRF will fail.");
                    return true;
                }
            }
            else
            {
                ConsoleUI.WriteErrorLine($"Cookie Validation Failed. Could not retrieve user info. Account not added.");
                return false;
            }
        }

        public async Task ImportAccountsFromFileAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                ConsoleUI.WriteErrorLine("File path provided is empty.");
                return;
            }

            if (!File.Exists(filePath))
            {
                ConsoleUI.WriteErrorLine($"File not found at path: {filePath}");
                return;
            }

            ConsoleUI.WriteInfoLine($"Reading cookies from file: {filePath}...");
            List<string> cookiesFromFile = [];
            int potentialCookiesFound = 0;

            int linesRead;
            try
            {
                string[] lines = await File.ReadAllLinesAsync(filePath);
                linesRead = lines.Length;

                foreach (string line in lines)
                {
                    string trimmedCookie = line.Trim();
                    if (!string.IsNullOrEmpty(trimmedCookie) && trimmedCookie.StartsWith("_|WARNING:-", StringComparison.OrdinalIgnoreCase))
                    {
                        potentialCookiesFound++;
                        if (!cookiesFromFile.Contains(trimmedCookie))
                        {
                            cookiesFromFile.Add(trimmedCookie);
                        }
                        else
                        {
                            ConsoleUI.WriteErrorLine($"Skipping duplicate cookie found within file: {ConsoleUI.Truncate(trimmedCookie, 20)}...");
                        }
                    }
                }
            }
            catch (FileNotFoundException)
            {
                ConsoleUI.WriteErrorLine($"File not found error during read operation: {filePath}");
                return;
            }
            catch (UnauthorizedAccessException)
            {
                ConsoleUI.WriteErrorLine($"Permission denied. Cannot read file: {filePath}");
                return;
            }
            catch (IOException ioEx)
            {
                ConsoleUI.WriteErrorLine($"An I/O error occurred while reading the file: {ioEx.Message}");
                return;
            }
            catch (Exception ex)
            {
                ConsoleUI.WriteErrorLine($"An unexpected error occurred reading the file: {ex.Message}");
                return;
            }

            ConsoleUI.WriteInfoLine($"Read {linesRead} lines from file. Found {potentialCookiesFound} potential cookies ({cookiesFromFile.Count} unique).");

            if (cookiesFromFile.Count > 0)
            {
                await ImportAccountsAsync(cookiesFromFile);
            }
            else
            {
                ConsoleUI.WriteErrorLine("No valid, unique cookies found in the specified file.");
            }
        }

        public async Task ImportAccountsAsync(List<string> cookiesToImport)
        {
            if (cookiesToImport == null || cookiesToImport.Count == 0)
            {
                ConsoleUI.WriteErrorLine("No cookies provided to import.");
                return;
            }
            ConsoleUI.WriteInfoLine($"\nStarting import/validation process for {cookiesToImport.Count} potential cookie(s)...");
            int successCount = 0, duplicateCount = 0, invalidCount = 0, fetchFailCount = 0;
            var stopwatch = Stopwatch.StartNew();
            var tasks = new List<Task>();

            int maxConcurrency = 5;
            using (var semaphore = new SemaphoreSlim(maxConcurrency))
            {
                for (int i = 0; i < cookiesToImport.Count; i++)
                {
                    await semaphore.WaitAsync();
                    int currentIndex = i;
                    string cookie = cookiesToImport[currentIndex];

                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            Console.WriteLine($"[{currentIndex + 1}/{cookiesToImport.Count}] Processing {ConsoleUI.Truncate(cookie, 20)}... ");
                            bool alreadyExists;
                            lock (_lock) { alreadyExists = _accounts.Any(a => a.Cookie == cookie); }
                            if (alreadyExists) { Console.WriteLine($"Duplicate (in roster)."); Interlocked.Increment(ref duplicateCount); return; }

                            var (isValid, userId, username) = await _authService.ValidateCookieAsync(cookie);
                            if (isValid && userId > 0)
                            {
                                string xcsrfRaw = await AuthenticationService.FetchXCSRFTokenAsync(cookie);
                                string xcsrf = xcsrfRaw?.Trim() ?? "";
                                var newAccount = new Account { Cookie = cookie, UserId = userId, Username = username, XcsrfToken = xcsrf, IsValid = !string.IsNullOrEmpty(xcsrf) };

                                lock (_lock)
                                {
                                    if (!_accounts.Any(a => a.Cookie == newAccount.Cookie))
                                    {
                                        _accounts.Add(newAccount);
                                        if (newAccount.IsValid) { Interlocked.Increment(ref successCount); Console.WriteLine($"OK ({username})"); }
                                        else { Interlocked.Increment(ref fetchFailCount); Console.WriteLine($"XCSRF Fail ({username})"); }
                                    }
                                    else { Console.WriteLine($"Duplicate (Race)."); Interlocked.Increment(ref duplicateCount); }
                                }
                            }
                            else { Console.WriteLine($"Invalid."); Interlocked.Increment(ref invalidCount); }
                        }
                        catch (Exception ex) { Console.WriteLine($"Error Processing {ConsoleUI.Truncate(cookie, 20)}: {ex.Message}"); Interlocked.Increment(ref invalidCount); }
                        finally { semaphore.Release(); }
                        await Task.Delay(150);
                    }));
                }
                await Task.WhenAll(tasks);
            }
            stopwatch.Stop();

            Console.WriteLine($"\n---[ Import Summary ]---");
            ConsoleUI.WriteSuccessLine($"Added & Valid: {successCount}");
            ConsoleUI.WriteErrorLine($"Duplicates Skipped (already in roster or race condition): {duplicateCount}");
            ConsoleUI.WriteErrorLine($"Invalid / Validation Failed: {invalidCount}");
            ConsoleUI.WriteErrorLine($"Valid Cookie but XCSRF Fetch Failed: {fetchFailCount}");
            ConsoleUI.WriteInfoLine($"Total accounts in roster: {_accounts.Count}");
            ConsoleUI.WriteInfoLine($"Total time: {stopwatch.ElapsedMilliseconds}ms ({stopwatch.Elapsed.TotalSeconds:F1}s)");
            Console.WriteLine($"------------------------");
        }

        public void UpdateSelection(List<int> indicesToToggle)
        {
            lock (_lock)
            {
                int toggledOn = 0; int toggledOff = 0;
                foreach (int userIndex in indicesToToggle)
                {
                    if (userIndex >= 1 && userIndex <= _accounts.Count)
                    {
                        int zeroBasedIndex = userIndex - 1;
                        if (_selectedAccountIndices.Contains(zeroBasedIndex))
                        {
                            _selectedAccountIndices.Remove(zeroBasedIndex);
                            toggledOff++;
                        }
                        else
                        {
                            _selectedAccountIndices.Add(zeroBasedIndex);
                            toggledOn++;
                        }
                    }
                    else
                    {
                        ConsoleUI.WriteErrorLine($"Invalid input number: '{userIndex}'. Must be between 1 and {_accounts.Count}. Skipped.");
                    }
                }
                if (toggledOn > 0 || toggledOff > 0) ConsoleUI.WriteInfoLine($"Selection updated: +{toggledOn} selected, -{toggledOff} deselected.");

                var distinctSorted = _selectedAccountIndices.Distinct().OrderBy(i => i).ToList();
                _selectedAccountIndices.Clear();
                _selectedAccountIndices.AddRange(distinctSorted);
            }
        }

        public void SelectAll()
        {
            lock (_lock)
            {
                _selectedAccountIndices.Clear();
                _selectedAccountIndices.AddRange(Enumerable.Range(0, _accounts.Count));
                ConsoleUI.WriteInfoLine($"All {_accounts.Count} accounts selected.");
            }
        }

        public void SelectNone()
        {
            lock (_lock)
            {
                _selectedAccountIndices.Clear();
                ConsoleUI.WriteErrorLine($"Selection Cleared. No accounts selected.");
            }
        }

        public void SelectValid()
        {
            lock (_lock)
            {
                _selectedAccountIndices.Clear();
                _selectedAccountIndices.AddRange(_accounts.Select((a, i) => new { Account = a, Index = i })
                .Where(x => x.Account.IsValid)
                .Select(x => x.Index));
                ConsoleUI.WriteInfoLine($"All {_selectedAccountIndices.Count} valid accounts selected.");
            }
        }

        public void SelectInvalid()
        {
            lock (_lock)
            {
                _selectedAccountIndices.Clear();
                _selectedAccountIndices.AddRange(_accounts.Select((a, i) => new { Account = a, Index = i })
                .Where(x => !x.Account.IsValid)
                .Select(x => x.Index));
                ConsoleUI.WriteInfoLine($"All {_selectedAccountIndices.Count} invalid accounts selected.");
            }
        }

        public void SelectFailedVerification()
        {
            lock (_lock)
            {
                if (_verificationResults.Count == 0)
                {
                    ConsoleUI.WriteErrorLine("Verification check has not been run recently. Run the Verify action first.");
                    return;
                }

                _selectedAccountIndices.Clear();
                int count = 0;
                for (int i = 0; i < _accounts.Count; i++)
                {
                    if (_verificationResults.TryGetValue(_accounts[i].UserId, out var result) &&
                        (result.Status == VerificationStatus.Failed || result.Status == VerificationStatus.Error))
                    {
                        _selectedAccountIndices.Add(i);
                        count++;
                    }
                }
                if (count > 0) { ConsoleUI.WriteInfoLine($"Selected {count} accounts that failed or had errors in the last verification check."); }
                else { ConsoleUI.WriteErrorLine("No accounts failed or had errors in the last verification check."); }

                var distinctSorted = _selectedAccountIndices.Distinct().OrderBy(i => i).ToList();
                _selectedAccountIndices.Clear();
                _selectedAccountIndices.AddRange(distinctSorted);
            }
        }

        public VerificationStatus GetVerificationStatus(long userId)
        {
            lock (_lock)
            {
                if (_verificationResults.TryGetValue(userId, out var result))
                {
                    return result.Status;
                }
                return VerificationStatus.NotChecked;
            }
        }

        public void SetVerificationStatus(long userId, VerificationStatus status, string details)
        {
            lock (_lock)
            {
                _verificationResults[userId] = (status, details ?? string.Empty);

            }
        }

        public void ClearVerificationResults()
        {
            lock (_lock)
            {
                _verificationResults.Clear();
            }
        }

        public async Task<bool> ExportAccountsToFileAsync(string filePath)
        {
            List<Account> accountsToExport;
            lock (_lock)
            {
                accountsToExport = new List<Account>(_accounts);
            }

            if (accountsToExport.Count == 0)
            {
                ConsoleUI.WriteErrorLine("No accounts loaded to export.");
                return false;
            }

            var cookies = new List<string>();
            var usernames = new List<string>();

            foreach (var account in accountsToExport)
            {
                if (!string.IsNullOrWhiteSpace(account.Cookie) && !string.IsNullOrWhiteSpace(account.Username) && account.Username != "N/A")
                {
                    cookies.Add(account.Cookie);
                    usernames.Add(account.Username);
                }
                else
                {
                    ConsoleUI.WriteErrorLine($"Skipping account ID {account.UserId} from export due to missing cookie or username.");
                }
            }

            if (cookies.Count == 0)
            {
                ConsoleUI.WriteErrorLine("No accounts with both valid cookies and usernames found to export.");
                return false;
            }

            usernames.Sort(StringComparer.OrdinalIgnoreCase);

            try
            {
                using (var writer = new StreamWriter(filePath, false, Encoding.UTF8))
                {
                    foreach (var cookie in cookies)
                    {
                        await writer.WriteLineAsync(cookie);
                    }

                    await writer.WriteLineAsync();
                    await writer.WriteLineAsync("--- Usernames ---");
                    await writer.WriteLineAsync();

                    foreach (var username in usernames)
                    {
                        await writer.WriteLineAsync(username);
                    }
                }
                ConsoleUI.WriteSuccessLine($"Successfully exported {cookies.Count} cookies and usernames to: {filePath}");
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                ConsoleUI.WriteErrorLine($"Error: Permission denied. Cannot write to path: {filePath}");
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                ConsoleUI.WriteErrorLine($"Error: Directory not found. Cannot write to path: {filePath}");
                return false;
            }
            catch (IOException ioEx)
            {
                ConsoleUI.WriteErrorLine($"Error: An I/O error occurred while writing to file: {ioEx.Message}");
                return false;
            }
            catch (Exception ex)
            {
                ConsoleUI.WriteErrorLine($"Error: An unexpected error occurred during export: {ex.Message}");
                return false;
            }
        }
    }
}
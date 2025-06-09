using System.Text;
using System.Diagnostics;
using Continuance.Models;
using Continuance.Roblox.Services;
using Continuance.UI;

namespace Continuance.Core
{
    public class AccountManager(AuthenticationService authService)
    {
        private readonly List<Account> _accounts = [];
        private readonly List<int> _selectedAccountIndices = [];
        private readonly Dictionary<long, (VerificationStatus Status, string Details)> _verificationResults = new Dictionary<long, (VerificationStatus, string)>();
        private readonly AuthenticationService _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        private readonly Lock _lock = new();

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
                    ConsoleUI.WriteErrorLine("Duplicate: This cookie is already in the account pool.");
                    return false;
                }
            }

            ConsoleUI.WriteInfoLine("Validating cookie integrity...");
            var (isValid, userId, username) = await AuthenticationService.ValidateCookieAsync(cookie);

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
                    ConsoleUI.WriteSuccessLine($"Account Secured & Added to Account Pool. ({_accounts.Count} total)");
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
            int processedCount = 0;

            int maxConcurrency = 5;
            using (var semaphore = new SemaphoreSlim(maxConcurrency))
            {
                foreach (string cookie in cookiesToImport)
                {
                    await semaphore.WaitAsync();
                    string currentCookie = cookie;

                    tasks.Add(Task.Run(async () =>
                    {
                        string resultMessage = "";
                        string truncatedCookie = ConsoleUI.Truncate(currentCookie, 20) + "...";
                        bool addedSuccessfully = false;
                        string finalUsername = "N/A";
                        bool isDuplicate = false;
                        bool isValidCookie = false;
                        bool xcsrfFailed = false;

                        try
                        {
                            bool alreadyExists;
                            lock (_lock) { alreadyExists = _accounts.Any(a => a.Cookie == currentCookie); }
                            if (alreadyExists) { resultMessage = $"Duplicate (already in account pool)."; isDuplicate = true; return; }

                            var (isValid, userId, username) = await AuthenticationService.ValidateCookieAsync(currentCookie);
                            if (isValid && userId > 0)
                            {
                                finalUsername = username ?? "N/A";
                                isValidCookie = true;
                                string xcsrfRaw = await AuthenticationService.FetchXCSRFTokenAsync(currentCookie);
                                string xcsrf = xcsrfRaw?.Trim() ?? "";
                                var newAccount = new Account { Cookie = currentCookie, UserId = userId, Username = finalUsername, XcsrfToken = xcsrf, IsValid = !string.IsNullOrEmpty(xcsrf) };

                                lock (_lock)
                                {
                                    if (!_accounts.Any(a => a.Cookie == newAccount.Cookie))
                                    {
                                        _accounts.Add(newAccount);
                                        if (newAccount.IsValid) { Interlocked.Increment(ref successCount); resultMessage = $"OK ({finalUsername})"; addedSuccessfully = true; }
                                        else { Interlocked.Increment(ref fetchFailCount); resultMessage = $"XCSRF Fail ({finalUsername})"; xcsrfFailed = true; }
                                    }
                                    else { Interlocked.Increment(ref duplicateCount); resultMessage = "Duplicate (Race)"; isDuplicate = true; }
                                }
                            }
                            else { resultMessage = $"Invalid."; Interlocked.Increment(ref invalidCount); }
                        }
                        catch (Exception ex) { resultMessage = $"Error: {ex.Message}"; Interlocked.Increment(ref invalidCount); }
                        finally
                        {
                            int currentCount = Interlocked.Increment(ref processedCount);
                            if (isDuplicate) { ConsoleUI.WriteWarningLine($"[{currentCount}/{cookiesToImport.Count}] Skipped Cookie: {truncatedCookie} - {resultMessage}"); }
                            else if (addedSuccessfully) { ConsoleUI.WriteSuccessLine($"[{currentCount}/{cookiesToImport.Count}] Added Cookie: {truncatedCookie} - {resultMessage}"); }
                            else if (xcsrfFailed) { ConsoleUI.WriteErrorLine($"[{currentCount}/{cookiesToImport.Count}] Added (INVALID) Cookie: {truncatedCookie} - {resultMessage}"); }
                            else if (!isValidCookie) { ConsoleUI.WriteErrorLine($"[{currentCount}/{cookiesToImport.Count}] Invalid Cookie: {truncatedCookie} - {resultMessage}"); }
                            else { ConsoleUI.WriteErrorLine($"[{currentCount}/{cookiesToImport.Count}] Failed Cookie: {truncatedCookie} - {resultMessage}"); }

                            semaphore.Release();
                            await Task.Delay(150);
                        }
                    }));
                }
                await Task.WhenAll(tasks);
            }
            stopwatch.Stop();

            Console.WriteLine($"\n---[ Import Summary ]---");
            ConsoleUI.WriteSuccessLine($"Added & Valid: {successCount}");
            ConsoleUI.WriteWarningLine($"Duplicates Skipped (already in account pool or race condition): {duplicateCount}");
            ConsoleUI.WriteErrorLine($"Invalid / Validation Failed: {invalidCount}");
            ConsoleUI.WriteErrorLine($"Valid Cookie but XCSRF Fetch Failed: {fetchFailCount}");
            ConsoleUI.WriteInfoLine($"Total accounts in account pool: {_accounts.Count}");
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

        public static async Task<bool> ExportAccountsToFileAsync(string filePath, List<Account> accountsToExport, bool sortByUsername = true)
        {
            if (accountsToExport == null || accountsToExport.Count == 0)
            {
                ConsoleUI.WriteErrorLine("No accounts provided or found matching the filter to export.");
                return false;
            }

            var cookies = new List<string>();
            var usernames = new List<string>();

            foreach (var account in accountsToExport)
            {
                if (account != null && !string.IsNullOrWhiteSpace(account.Cookie) && !string.IsNullOrWhiteSpace(account.Username) && account.Username != "N/A")
                {
                    cookies.Add(account.Cookie);
                    usernames.Add(account.Username);
                }
                else
                {
                    ConsoleUI.WriteErrorLine($"Skipping account ID {account?.UserId.ToString() ?? "Unknown"} from export due to missing cookie or username, or null account object.");
                }
            }

            if (cookies.Count == 0)
            {
                ConsoleUI.WriteErrorLine("No accounts with both valid cookies and usernames found in the provided list to export.");
                return false;
            }

            if (sortByUsername)
            {
                usernames.Sort(StringComparer.OrdinalIgnoreCase);
                ConsoleUI.WriteInfoLine("   (Usernames will be sorted alphabetically)");
            }
            else
            {
                ConsoleUI.WriteInfoLine("   (Usernames will be kept in their provided order)");
            }

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
                string sortStatus = sortByUsername ? "sorted" : "original order";
                ConsoleUI.WriteSuccessLine($"Successfully exported {cookies.Count} cookies and {sortStatus} usernames to: {filePath}");
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
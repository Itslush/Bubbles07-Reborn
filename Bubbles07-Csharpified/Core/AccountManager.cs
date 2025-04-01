using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Models;
using Roblox.Services;
using UI;

namespace Core
{
    public class AccountManager
    {
        private readonly List<Account> _accounts = new List<Account>();
        private readonly List<int> _selectedAccountIndices = new List<int>();
        private readonly Dictionary<long, (VerificationStatus Status, string Details)> _verificationResults = new Dictionary<long, (VerificationStatus, string)>();
        private readonly AuthenticationService _authService;
        private readonly object _lock = new object();

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
                Console.WriteLine("[!] Invalid Format/Empty Cookie.");
                return false;
            }

            lock (_lock)
            {
                if (_accounts.Any(a => a.Cookie == cookie))
                {
                    Console.WriteLine("[!] Duplicate: This cookie is already in the roster.");
                    return false;
                }
            }

            Console.WriteLine("[*] Validating cookie integrity...");
            var (isValid, userId, username) = await _authService.ValidateCookieAsync(cookie);

            if (isValid && userId > 0)
            {
                Console.WriteLine($"[+] Cookie Valid :: User: {username} (ID: {userId}). Fetching XCSRF token...");
                string xcsrf = await _authService.FetchXCSRFTokenAsync(cookie);

                var newAccount = new Account
                {
                    Cookie = cookie,
                    UserId = userId,
                    Username = username,
                    XcsrfToken = xcsrf ?? "",
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
                        Console.WriteLine("[!] Duplicate: Cookie added by another thread concurrently.");
                        return false;
                    }
                }

                if (newAccount.IsValid)
                {
                    Console.WriteLine($"[+] Account Secured & Added to roster. ({_accounts.Count} total)");
                    return true;
                }
                else
                {
                    Console.WriteLine($"[-] XCSRF Fetch Failed. Account added but marked as INVALID. Actions requiring XCSRF will fail.");
                    return true;
                }
            }
            else
            {
                Console.WriteLine($"[-] Cookie Validation Failed. Could not retrieve user info. Account not added.");
                return false;
            }
        }

        public async Task ImportAccountsAsync(List<string> cookiesToImport)
        {
            Console.WriteLine($"\n[*] Attempting import of {cookiesToImport.Count} potential cookie(s)...");
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
                            Console.Write($"[{currentIndex + 1}/{cookiesToImport.Count}] Processing {ConsoleUI.Truncate(cookie, 20)}... ");
                            bool alreadyExists;
                            lock (_lock) { alreadyExists = _accounts.Any(a => a.Cookie == cookie); }
                            if (alreadyExists) { Console.WriteLine($"Duplicate (in roster)."); Interlocked.Increment(ref duplicateCount); return; }

                            var (isValid, userId, username) = await _authService.ValidateCookieAsync(cookie);
                            if (isValid && userId > 0)
                            {
                                string xcsrf = await _authService.FetchXCSRFTokenAsync(cookie);
                                var newAccount = new Account { Cookie = cookie, UserId = userId, Username = username, XcsrfToken = xcsrf ?? "", IsValid = !string.IsNullOrEmpty(xcsrf) };

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
            Console.WriteLine($" [+] Added & Valid: {successCount}");
            Console.WriteLine($" [!] Duplicates Skipped (already in roster): {duplicateCount}");
            Console.WriteLine($" [-] Invalid / Validation Failed: {invalidCount}");
            Console.WriteLine($" [-] Valid Cookie but XCSRF Fetch Failed: {fetchFailCount}");
            Console.WriteLine($" Total accounts in roster: {_accounts.Count}");
            Console.WriteLine($" Total time: {stopwatch.ElapsedMilliseconds}ms ({stopwatch.Elapsed.TotalSeconds:F1}s)");
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
                        Console.WriteLine($"[!] Invalid input number: '{userIndex}'. Must be between 1 and {_accounts.Count}. Skipped.");
                    }
                }
                if (toggledOn > 0 || toggledOff > 0) Console.WriteLine($"[*] Selection updated: +{toggledOn} selected, -{toggledOff} deselected.");

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
                Console.WriteLine($"[*] All {_accounts.Count} accounts selected.");
            }
        }

        public void SelectNone()
        {
            lock (_lock)
            {
                _selectedAccountIndices.Clear();
                Console.WriteLine($"[!] Selection Cleared. No accounts selected.");
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
                Console.WriteLine($"[*] All {_selectedAccountIndices.Count} valid accounts selected.");
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
                Console.WriteLine($"[*] All {_selectedAccountIndices.Count} invalid accounts selected.");
            }
        }

        public void SelectFailedVerification()
        {
            lock (_lock)
            {
                if (_verificationResults.Count == 0)
                {
                    Console.WriteLine("[!] Verification check has not been run recently. Run the Verify action first.");
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
                if (count > 0) { Console.WriteLine($"[*] Selected {count} accounts that failed or had errors in the last verification check."); }
                else { Console.WriteLine("[!] No accounts failed or had errors in the last verification check."); }

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

                var account = GetAccountById(userId);
                if (account != null)
                {
                }
            }
        }

        public void ClearVerificationResults()
        {
            lock (_lock)
            {
                _verificationResults.Clear();
            }
        }

        private Account? GetAccountById(long userId)
        {
            lock (_lock)
            {
                return _accounts.FirstOrDefault(a => a.UserId == userId);
            }
        }
    }
}
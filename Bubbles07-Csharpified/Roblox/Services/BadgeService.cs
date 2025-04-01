using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Models;
using Roblox.Http;
using _Csharpified;

namespace Roblox.Services
{
    public class BadgeService
    {
        private readonly RobloxHttpClient _robloxHttpClient;

        public BadgeService(RobloxHttpClient robloxHttpClient)
        {
            _robloxHttpClient = robloxHttpClient ?? throw new ArgumentNullException(nameof(robloxHttpClient));
        }

        private static string TruncateForLog(string? value, int maxLength = 100)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= maxLength ? value.Substring(0, maxLength) + "..." : value;
        }

        public async Task<int> GetBadgeCountAsync(Account account, int limit = 10)
        {
            if (account == null)
            {
                Console.WriteLine($"[-] Cannot GetBadgeCount: Account object is null.");
                return -1;
            }
            limit = Math.Clamp(limit, 1, 100);

            string url = $"{AppConfig.RobloxApiBaseUrl_Badges}/v1/users/{account.UserId}/badges?limit={limit}&sortOrder=Desc";

            var (success, content) = await _robloxHttpClient.SendRequestAndReadAsync(
                HttpMethod.Get, url, account, null, $"Get Badge Count (Limit {limit})",
                allowRetryOnXcsrf: false
                );

            if (success && !string.IsNullOrEmpty(content))
            {
                try
                {
                    var json = JObject.Parse(content);
                    if (json["data"] is JArray dataArray)
                    {
                        int count = dataArray.Count;
                        return count;
                    }
                    else
                    {
                        Console.WriteLine($"[-] Could not parse badge count (missing or invalid 'data' array) from response for {account.Username}: {TruncateForLog(content)}");
                    }
                }
                catch (JsonReaderException jex) { Console.WriteLine($"[-] Error parsing badge count JSON for {account.Username}: {jex.Message}"); }
                catch (Exception ex) { Console.WriteLine($"[-] Error processing badge count response for {account.Username}: {ex.Message}"); }
            }
            else if (!success)
            {
                // Error logged by SendRequestAndReadAsync
            }
            else
            {
                Console.WriteLine($"[-] Get Badge Count request succeeded but returned empty content for {account.Username}.");
            }

            return -1;
        }

        public async Task MonitorBadgeAcquisitionAsync(Account account, int badgeGoal = AppConfig.DefaultBadgeGoal)
        {
            if (account == null)
            {
                Console.WriteLine($"[-] Cannot MonitorBadgeAcquisition: Account object is null.");
                return;
            }
            if (!Environment.UserInteractive)
            {
                Console.WriteLine("[!] Skipping badge monitoring in non-interactive environment.");
                return;
            }

            int checkCount = 0;
            const int maxChecks = 4;
            const int checkIntervalSeconds = 6;
            int initialBadgeCount = -1;

            Console.WriteLine($"[*] Monitoring badge acquisition for {account.Username} (Goal: {badgeGoal})...");
            Console.WriteLine($"    Checking every {checkIntervalSeconds}s up to {maxChecks} times (~{maxChecks * checkIntervalSeconds}s total).");
            Console.WriteLine($"    Press Enter in console to stop monitoring early.");

            Console.Write("[>] Performing initial badge check...");
            initialBadgeCount = await GetBadgeCountAsync(account, 10);
            if (initialBadgeCount != -1)
            {
                Console.WriteLine($" Initial badges found: {initialBadgeCount}");
            }
            else
            {
                Console.WriteLine(" Failed to get initial count.");
            }

            bool stopWaitingFlag = false;
            using CancellationTokenSource cts = new CancellationTokenSource();

            Task keyListener = Task.Run(async () =>
            {
                if (Console.IsInputRedirected)
                {
                    try { await Task.Delay(-1, cts.Token); } catch (TaskCanceledException) { }
                    return;
                };

                try
                {
                    while (!cts.IsCancellationRequested && !Console.KeyAvailable)
                    {
                        await Task.Delay(250, cts.Token);
                    }
                    if (!cts.IsCancellationRequested)
                    {
                        Console.ReadKey(intercept: true);
                        stopWaitingFlag = true;
                        Console.WriteLine($"\n[!] User requested monitor abort.");
                        try { if (!cts.IsCancellationRequested) cts.Cancel(); } catch (ObjectDisposedException) { }
                    }
                }
                catch (InvalidOperationException) { }
                catch (TaskCanceledException) { }
                catch (Exception ex) { Console.WriteLine($"\n[!] Error in key listener: {ex.Message}"); }
                finally
                {
                    try { if (!cts.IsCancellationRequested) cts.Cancel(); }
                    catch (ObjectDisposedException) { }
                    catch (Exception) { }
                }

            }, cts.Token);

            while (checkCount < maxChecks && !stopWaitingFlag && !cts.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(checkIntervalSeconds), cts.Token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }

                if (stopWaitingFlag || cts.IsCancellationRequested) break;
                checkCount++;

                try
                {
                    Console.Write($"[>] Badge Check ({checkCount}/{maxChecks}) for {account.Username}...");
                    int currentBadgeCount = await GetBadgeCountAsync(account, 10);

                    if (currentBadgeCount != -1)
                    {
                        Console.WriteLine($" Recent Badges: {currentBadgeCount}");
                        if (initialBadgeCount != -1 && currentBadgeCount > initialBadgeCount)
                        {
                            Console.WriteLine($"[+] Change detected! ({initialBadgeCount} -> {currentBadgeCount}). Monitor continuing.");
                            initialBadgeCount = currentBadgeCount;
                        }
                        if (currentBadgeCount >= badgeGoal)
                        {
                            Console.WriteLine($"[*] Badge goal ({badgeGoal}) met or exceeded based on API count ({currentBadgeCount}).");
                        }
                    }
                    else
                    {
                        Console.WriteLine($" Check Failed (API error retrieving count).");
                    }
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    Console.WriteLine($" Unexpected Check Error: {ex.GetType().Name} - {TruncateForLog(ex.Message)}");
                }
            }

            try { if (!cts.IsCancellationRequested) cts.Cancel(); } catch (ObjectDisposedException) { } catch (Exception) { }
            await Task.WhenAny(keyListener, Task.Delay(100));
            try { cts.Dispose(); } catch { }

            if (stopWaitingFlag) { }
            else if (checkCount >= maxChecks) { Console.WriteLine($"[!] Max badge checks ({maxChecks}) reached for {account.Username}. Monitoring finished."); }
            else if (cts.IsCancellationRequested) { }

            int finalCount = await GetBadgeCountAsync(account, 10);
            if (finalCount != -1) Console.WriteLine($"[*] Final recent badge count check result: {finalCount}");
        }
    }
}
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
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
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
        }

        public async Task<int> GetBadgeCountAsync(Account account, int limit = 10)
        {
            if (account == null)
            {
                Console.WriteLine($"[-] Cannot GetBadgeCount: Account object is null.");
                return -1;
            }

            string url = $"https://badges.roblox.com/v1/users/{account.UserId}/badges?limit={limit}&sortOrder=Desc";

            var (success, content) = await _robloxHttpClient.SendRequestAndReadAsync(
                HttpMethod.Get, url, account, null, "Get Badge Count", allowRetryOnXcsrf: false // Getting count shouldn't need XCSRF retry
                );

            if (success && !string.IsNullOrEmpty(content))
            {
                try
                {
                    var json = JObject.Parse(content);
                    int count = json["data"] is JArray dataArray ? dataArray.Count : -1;
                    if (count != -1) { return count; }
                    else { Console.WriteLine($"[-] Could not parse badge count (missing or invalid 'data' array) from response for {account.Username}: {TruncateForLog(content)}"); }
                }
                catch (JsonReaderException jex) { Console.WriteLine($"[-] Error parsing badge count JSON for {account.Username}: {jex.Message}"); }
                catch (Exception ex) { Console.WriteLine($"[-] Error processing badge count for {account.Username}: {ex.Message}"); }
            }
            else if (!success) { }
            else
            {
                Console.WriteLine($"[-] Get Badge Count request succeeded but returned empty content for {account.Username}.");
            }

            await Task.Delay(AppConfig.CurrentApiDelayMs / 2);
            return -1;
        }

        public async Task MonitorBadgeAcquisitionAsync(Account account, int badgeGoal = AppConfig.DefaultBadgeGoal)
        {
            if (account == null)
            {
                Console.WriteLine($"[-] Cannot MonitorBadgeAcquisition: Account object is null.");
                return;
            }

            int checkCount = 0;
            const int maxChecks = 4;
            const int checkIntervalSeconds = 6;
            Console.WriteLine($"[*] Monitoring badge acquisition for {account.Username} (Checks every {checkIntervalSeconds}s for ~{maxChecks * checkIntervalSeconds} seconds, Goal: {badgeGoal})...");
            Console.WriteLine($"    Press Enter in console to stop monitoring early.");

            bool stopWaitingFlag = false;
            using CancellationTokenSource cts = new CancellationTokenSource();

            Task keyListener = Task.Run(() =>
            {
                if (Console.IsInputRedirected) return;
                try
                {
                    Console.ReadKey(intercept: true);
                }
                catch (InvalidOperationException) { }
                catch (Exception ex) { Console.WriteLine($"\n[!] Error in key listener: {ex.Message}"); }

                stopWaitingFlag = true;
                try { if (!cts.IsCancellationRequested) cts.Cancel(); }
                catch (ObjectDisposedException) { }
                catch (Exception ex) { Console.WriteLine($"\n[!] Error cancelling monitor task: {ex.Message}"); }
                Console.WriteLine($"\n[!] User requested monitor abort.");
            }, cts.Token); // Pass token to allow cancellation of the listener task


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
                        Console.WriteLine($" Recent API Badges Found: {currentBadgeCount}");
                        // Could compare to a previous count, or check if >= badgeGoal.
                        if (currentBadgeCount > 0)
                        {
                            Console.WriteLine($"[+] Activity detected. Monitor continuing.");
                            // Could check `if (currentBadgeCount >= badgeGoal)` and break loop early?
                            // API might not update instantly.
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

            if (!keyListener.IsCompleted && !keyListener.IsCanceled && !keyListener.IsFaulted)
            {
                try { if (!cts.IsCancellationRequested) cts.Cancel(); } catch { }
                await Task.WhenAny(keyListener, Task.Delay(100));
            }

            if (stopWaitingFlag) { }
            else if (checkCount >= maxChecks) { Console.WriteLine($"[!] Max badge checks ({maxChecks}) reached for {account.Username}."); }
            else if (cts.IsCancellationRequested) { }
        }
    }
}
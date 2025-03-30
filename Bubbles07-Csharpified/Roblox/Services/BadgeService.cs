using _Csharpified.Roblox.Http;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using _Csharpified.Models;
namespace
    _Csharpified.Roblox.Services
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
            string url = $"https://badges.roblox.com/v1/users/{account.UserId}/badges?limit={limit}&sortOrder=Desc";

            var (success, content) = await _robloxHttpClient.SendRequestAndReadAsync(
                HttpMethod.Get, url, account, null, "Get Badge Count", allowRetryOnXcsrf: false
                );

            if (success)
            {
                try
                {
                    var json = JObject.Parse(content);
                    int count = json["data"]?.Count() ?? -1;
                    if (count != -1) { return count; }
                    else { Console.WriteLine($"[-] Could not parse badge count (data array) from response for {account.Username}: {TruncateForLog(content)}"); }
                }
                catch (JsonReaderException jex) { Console.WriteLine($"[-] Error parsing badge count JSON for {account.Username}: {jex.Message}"); }
                catch (Exception ex) { Console.WriteLine($"[-] Error processing badge count for {account.Username}: {ex.Message}"); }
            }
            await Task.Delay(AppConfig.CurrentApiDelayMs / 2);
            return -1;
        }

        // Badge monitoring part from GetBadgesAsync (could be in GameLauncher too)
        public async Task MonitorBadgeAcquisitionAsync(Account account)
        {
            int checkCount = 0;
            const int maxChecks = 5;
            const int checkIntervalSeconds = 6;
            Console.WriteLine($"[*] Monitoring badge acquisition for {account.Username} (Checks every {checkIntervalSeconds}s for ~{maxChecks * checkIntervalSeconds} seconds)... Press Enter in console to stop monitoring early.");

            bool stopWaitingFlag = false;
            using CancellationTokenSource cts = new CancellationTokenSource();

            Task keyListener = Task.Run(() =>
            {
                if (Console.IsInputRedirected) return;
                try { Console.ReadKey(intercept: true); } catch (InvalidOperationException) { }
                stopWaitingFlag = true;
                try { cts.Cancel(); } catch (ObjectDisposedException) { } catch (Exception ex) { Console.WriteLine($"Error cancelling monitor task: {ex.Message}"); }
                Console.WriteLine($"\n[!] User requested monitor abort.");
            }, cts.Token);

            while (checkCount < maxChecks && !stopWaitingFlag && !cts.IsCancellationRequested)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(checkIntervalSeconds), cts.Token); }
                catch (TaskCanceledException) { break; }

                if (stopWaitingFlag || cts.IsCancellationRequested) break;
                checkCount++;

                try
                {
                    Console.Write($"[>] Badge Check ({checkCount}/{maxChecks}) for {account.Username}...");
                    int currentBadgeCount = await GetBadgeCountAsync(account, 10); // Reuse the service method

                    if (currentBadgeCount != -1)
                    {
                        Console.WriteLine($" Recent API Badges: {currentBadgeCount}");
                        if (currentBadgeCount > 0) { Console.WriteLine($"[+] Activity detected (found {currentBadgeCount} recent badges). Assuming progress for {account.Username}. Monitor continuing."); }
                    }
                    else
                    {
                        Console.WriteLine($" Check Failed (API error retrieving count).");
                    }
                }
                catch (Exception ex) when (ex is not TaskCanceledException)
                {
                    Console.WriteLine($" Unexpected Check Error: {ex.GetType().Name} - {TruncateForLog(ex.Message)}");
                }
            }

            if (!keyListener.IsCompleted && !keyListener.IsCanceled && !keyListener.IsFaulted)
            {
                try { if (!cts.IsCancellationRequested) cts.Cancel(); } catch { }
                await Task.WhenAny(keyListener, Task.Delay(100));
            }

            if (!stopWaitingFlag && checkCount >= maxChecks) { Console.WriteLine($"[!] Max badge checks ({maxChecks}) reached for {account.Username}. Badge acquisition confirmation uncertain."); }
            else if (!stopWaitingFlag && cts.IsCancellationRequested) { Console.WriteLine($"[!] Badge monitoring ended unexpectedly (external cancellation?)."); }
        }
    }
}
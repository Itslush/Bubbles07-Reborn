using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Models;
using Roblox.Http;
using _Csharpified;
using UI;

namespace Roblox.Services
{
    public class BadgeService(RobloxHttpClient robloxHttpClient)
    {
        private readonly RobloxHttpClient _robloxHttpClient = robloxHttpClient ?? throw new ArgumentNullException(nameof(robloxHttpClient));

        public async Task<int> GetBadgeCountAsync(Account account, int limit = 10)
        {
            if (account == null)
            {
                ConsoleUI.WriteErrorLine($"Cannot GetBadgeCount: Account object is null.");
                return -1;
            }
            if (account.UserId <= 0)
            {
                ConsoleUI.WriteErrorLine($"Cannot GetBadgeCount: Invalid User ID ({account.UserId}) in Account object.");
                return -1;
            }

            limit = Math.Clamp(limit, 10, 100);

            string url = $"{AppConfig.RobloxApiBaseUrl_Badges}/v1/users/{account.UserId}/badges?limit={limit}&sortOrder=Desc";

            var (statusCode, success, content) = await _robloxHttpClient.SendRequestAndReadAsync(
                HttpMethod.Get,
                url,
                account,
                null,
                $"Get Badge Count (Limit {limit})",
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
                        ConsoleUI.WriteErrorLine($"Could not parse badge count (missing or invalid 'data' array) from response for {account.Username}: {ConsoleUI.Truncate(content)}");
                    }
                }
                catch (JsonReaderException jex)
                {
                    ConsoleUI.WriteErrorLine($"Error parsing badge count JSON for {account.Username}: {jex.Message}");
                }
                catch (Exception ex)
                {
                    ConsoleUI.WriteErrorLine($"Error processing badge count response for {account.Username}: {ex.Message}");
                }
            }
            else if (!success)
            {
            }
            else
            {
                ConsoleUI.WriteWarningLine($"Get Badge Count request succeeded but returned empty content for {account.Username}.");
            }

            return -1;
        }


        public async Task MonitorBadgeAcquisitionAsync(Account account, int badgeGoal = AppConfig.DefaultBadgeGoal)
        {
            if (account == null)
            {
                ConsoleUI.WriteErrorLine($"Cannot MonitorBadgeAcquisition: Account object is null.");
                return;
            }
            if (!Environment.UserInteractive)
            {
                ConsoleUI.WriteWarningLine("Skipping badge monitoring in non-interactive environment.");
                return;
            }
            if (badgeGoal <= 0)
            {
                ConsoleUI.WriteInfoLine($"Skipping badge monitoring: Badge goal ({badgeGoal}) is not positive.");
                return;
            }

            int checkCount = 0;
            const int maxChecks = 4;
            const int checkIntervalSeconds = 6;
            int initialBadgeCount = -1;

            ConsoleUI.WriteInfoLine($"Monitoring badge acquisition for {account.Username} (Goal: {badgeGoal})...");
            Console.WriteLine($"    Checking every {checkIntervalSeconds}s up to {maxChecks} times (~{maxChecks * checkIntervalSeconds}s total).");
            Console.WriteLine($"    Press Enter in console to stop monitoring early.");

            Console.Write("[>] Performing initial badge check...");
            initialBadgeCount = await GetBadgeCountAsync(account, Math.Max(10, badgeGoal));
            if (initialBadgeCount != -1)
            {
                Console.WriteLine($" Initial recent badges found: {initialBadgeCount}");
                if (initialBadgeCount >= badgeGoal)
                {
                    ConsoleUI.WriteSuccessLine($"Goal already met or exceeded ({initialBadgeCount} >= {badgeGoal}). Monitoring finished early.");
                    return;
                }
            }
            else
            {
                Console.WriteLine(" Failed to get initial count. Monitoring will continue.");
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
                        var key = Console.ReadKey(intercept: true);
                        if (key.Key == ConsoleKey.Enter)
                        {
                            stopWaitingFlag = true;
                            Console.WriteLine($"\n[!] User pressed Enter. Aborting monitor.");
                            try { if (!cts.IsCancellationRequested) cts.Cancel(); } catch (ObjectDisposedException) { }
                        }
                    }
                }
                catch (InvalidOperationException) { }
                catch (TaskCanceledException) { }
                catch (Exception ex) { ConsoleUI.WriteErrorLine($"\nError in key listener: {ex.Message}"); }
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
                    int currentBadgeCount = await GetBadgeCountAsync(account, Math.Max(10, badgeGoal));

                    if (currentBadgeCount != -1)
                    {
                        Console.WriteLine($" Recent Badges: {currentBadgeCount}");
                        if (initialBadgeCount != -1 && currentBadgeCount > initialBadgeCount)
                        {
                            Console.WriteLine($"    [+] Change detected! ({initialBadgeCount} -> {currentBadgeCount}).");
                            initialBadgeCount = currentBadgeCount;
                        }
                        else if (initialBadgeCount != -1 && currentBadgeCount < initialBadgeCount)
                        {
                            ConsoleUI.WriteWarningLine($"Count decreased? ({initialBadgeCount} -> {currentBadgeCount}). API might show badges differently over time.");
                            initialBadgeCount = currentBadgeCount;
                        }
                        if (currentBadgeCount >= badgeGoal)
                        {
                            ConsoleUI.WriteSuccessLine($"Badge goal ({badgeGoal}) met or exceeded ({currentBadgeCount}). Stopping monitor.");
                            stopWaitingFlag = true;
                            try { if (!cts.IsCancellationRequested) cts.Cancel(); } catch (ObjectDisposedException) { }
                        }
                    }
                    else
                    {
                        Console.WriteLine($" Check Failed (API error retrieving count).");
                    }
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    ConsoleUI.WriteErrorLine($"Unexpected Check Error: {ex.GetType().Name} - {ConsoleUI.Truncate(ex.Message)}");
                }
            }

            try { if (!cts.IsCancellationRequested) cts.Cancel(); } catch (ObjectDisposedException) { } catch (Exception) { }

            await Task.WhenAny(keyListener, Task.Delay(100));

            try { cts.Dispose(); } catch { }

            if (stopWaitingFlag && !keyListener.IsCompleted) { }
            else if (checkCount >= maxChecks) { ConsoleUI.WriteWarningLine($"Max badge checks ({maxChecks}) reached for {account.Username}. Monitoring finished."); }
            else if (cts.IsCancellationRequested && !stopWaitingFlag) { }


            await Task.Delay(500);
            Console.Write("[>] Performing final badge count check...");
            int finalCount = await GetBadgeCountAsync(account, Math.Max(10, badgeGoal));
            if (finalCount != -1) Console.WriteLine($" Final recent badge count: {finalCount} (Goal was {badgeGoal})");
            else Console.WriteLine(" Final check failed.");
        }
    }
}
﻿using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Models;
using Roblox.Http;
using _Csharpified;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Threading;
using UI;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Roblox.Services
{
    public class BadgeService
    {
        private readonly RobloxHttpClient _robloxHttpClient;

        public BadgeService(RobloxHttpClient robloxHttpClient)
        {
            _robloxHttpClient = robloxHttpClient ?? throw new ArgumentNullException(nameof(robloxHttpClient));
        }

        public async Task<int> GetBadgeCountAsync(Account account, int limit = 10)
        {
            if (account == null)
            {
                Console.WriteLine($"[-] Cannot GetBadgeCount: Account object is null.");
                return -1;
            }
            if (account.UserId <= 0)
            {
                Console.WriteLine($"[-] Cannot GetBadgeCount: Invalid User ID ({account.UserId}) in Account object.");
                return -1;
            }

            limit = Math.Clamp(limit, 10, 100);

            string url = $"{AppConfig.RobloxApiBaseUrl_Badges}/v1/users/{account.UserId}/badges?limit={limit}&sortOrder=Desc";

            var (_, success, content) = await _robloxHttpClient.SendRequestAndReadAsync(
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
                        Console.WriteLine($"[-] Could not parse badge count (missing or invalid 'data' array) from response for {account.Username}: {ConsoleUI.Truncate(content)}");
                    }
                }
                catch (JsonReaderException jex)
                {
                    Console.WriteLine($"[-] Error parsing badge count JSON for {account.Username}: {jex.Message}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[-] Error processing badge count response for {account.Username}: {ex.Message}");
                }
            }
            else if (!success) { }
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
            if (badgeGoal <= 0)
            {
                Console.WriteLine($"[*] Skipping badge monitoring: Badge goal ({badgeGoal}) is not positive.");
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
            initialBadgeCount = await GetBadgeCountAsync(account, Math.Max(10, badgeGoal));
            if (initialBadgeCount != -1)
            {
                Console.WriteLine($" Initial recent badges found: {initialBadgeCount}");
                if (initialBadgeCount >= badgeGoal)
                {
                    Console.WriteLine($"[*] Goal already met or exceeded ({initialBadgeCount} >= {badgeGoal}). Monitoring finished early.");
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
                        else
                        {
                        }
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
                            Console.WriteLine($"    [?] Count decreased? ({initialBadgeCount} -> {currentBadgeCount}). API might show badges differently over time.");
                            initialBadgeCount = currentBadgeCount;
                        }
                        if (currentBadgeCount >= badgeGoal)
                        {
                            Console.WriteLine($"[*] Badge goal ({badgeGoal}) met or exceeded ({currentBadgeCount}). Stopping monitor.");
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
                    Console.WriteLine($" Unexpected Check Error: {ex.GetType().Name} - {ConsoleUI.Truncate(ex.Message)}");
                }
            }

            try { if (!cts.IsCancellationRequested) cts.Cancel(); } catch (ObjectDisposedException) { } catch (Exception) { }

            await Task.WhenAny(keyListener, Task.Delay(100));

            try { cts.Dispose(); } catch { }

            if (stopWaitingFlag && !keyListener.IsCompleted) { }
            else if (checkCount >= maxChecks) { Console.WriteLine($"[!] Max badge checks ({maxChecks}) reached for {account.Username}. Monitoring finished."); }
            else if (cts.IsCancellationRequested) { }

            await Task.Delay(500);
            Console.Write("[>] Performing final badge count check...");
            int finalCount = await GetBadgeCountAsync(account, Math.Max(10, badgeGoal));
            if (finalCount != -1) Console.WriteLine($" Final recent badge count: {finalCount} (Goal was {badgeGoal})");
            else Console.WriteLine(" Final check failed.");
        }
    }
}
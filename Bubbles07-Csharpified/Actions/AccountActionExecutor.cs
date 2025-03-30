using System.Diagnostics;
using Newtonsoft.Json;
using _Csharpified;
using Core;
using Models;
using Roblox.Automation;
using Roblox.Services;

namespace Actions
{
    public class AccountActionExecutor
    {
        private readonly AccountManager _accountManager;
        private readonly AuthenticationService _authService;
        private readonly UserService _userService;
        private readonly AvatarService _avatarService;
        private readonly GroupService _groupService;
        private readonly FriendService _friendService;
        private readonly BadgeService _badgeService;
        private readonly WebDriverManager _webDriverManager;
        private readonly GameLauncher _gameLauncher;

        public AccountActionExecutor(
            AccountManager accountManager,
            AuthenticationService authService,
            UserService userService,
            AvatarService avatarService,
            GroupService groupService,
            FriendService friendService,
            BadgeService badgeService,
            WebDriverManager webDriverManager,
            GameLauncher gameLauncher)
        {
            _accountManager = accountManager ?? throw new ArgumentNullException(nameof(accountManager));
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
            _avatarService = avatarService ?? throw new ArgumentNullException(nameof(avatarService));
            _groupService = groupService ?? throw new ArgumentNullException(nameof(groupService));
            _friendService = friendService ?? throw new ArgumentNullException(nameof(friendService));
            _badgeService = badgeService ?? throw new ArgumentNullException(nameof(badgeService));
            _webDriverManager = webDriverManager ?? throw new ArgumentNullException(nameof(webDriverManager));
            _gameLauncher = gameLauncher ?? throw new ArgumentNullException(nameof(gameLauncher));
        }

        private static string TruncateForLog(string? value, int maxLength = 50)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= maxLength ? value : value[..maxLength] + "...";
        }

        private async Task PerformActionOnSelectedAsync(Func<Account, Task<bool>> action, string actionName, bool requireInteraction = false, bool requireValidToken = true)
        {
            if (requireInteraction && !Environment.UserInteractive)
            {
                Console.WriteLine($"[!] Skipping interactive action '{actionName}' in non-interactive environment.");
                return;
            }

            var selectedAccounts = _accountManager.GetSelectedAccounts();
            var accountsToProcess = selectedAccounts
                .Where(acc => acc != null && acc.IsValid && (!requireValidToken || !string.IsNullOrEmpty(acc.XcsrfToken)))
                .ToList();

            int totalSelected = selectedAccounts.Count;
            int validCount = accountsToProcess.Count;
            int skippedCount = totalSelected - validCount;

            Console.WriteLine($"\n[>>] Executing Action: {actionName} for {validCount} valid account(s)...");
            if (skippedCount > 0)
            {
                string reason = requireValidToken ? "invalid/lacked XCSRF" : "invalid";
                Console.WriteLine($"   ({skippedCount} selected accounts were {reason} and will be skipped)");
            }
            if (validCount == 0)
            {
                Console.WriteLine($"[!] No valid accounts selected for this action.");
                return;
            }

            int successCount = 0, failCount = 0;
            var stopwatch = Stopwatch.StartNew();

            for (int i = 0; i < accountsToProcess.Count; i++)
            {
                Account acc = accountsToProcess[i];
                Console.WriteLine($"[{i + 1}/{validCount}] Processing: {acc.Username} (ID: {acc.UserId})");
                try
                {
                    if (await action(acc)) { successCount++; }
                    else { failCount++; Console.WriteLine($"   Action '{actionName}' reported failure for {acc.Username}."); }
                }
                catch (InvalidOperationException ioex) { Console.WriteLine($"[!] Config/State Error for {acc.Username} during '{actionName}': {ioex.Message}"); failCount++; }
                catch (HttpRequestException hrex) { Console.WriteLine($"[!] Network Error during '{actionName}' for {acc.Username}: {hrex.StatusCode} - {hrex.Message}"); failCount++; }
                catch (JsonException jex) { Console.WriteLine($"[!] JSON Error during '{actionName}' for {acc.Username}: {jex.Message}"); failCount++; }
                catch (Exception ex) { Console.WriteLine($"[!] Runtime Error during '{actionName}' for {acc.Username}: {ex.GetType().Name} - {ex.Message}"); failCount++; }
            }
            stopwatch.Stop();

            Console.WriteLine($"[<<] Action '{actionName}' Finished.");
            Console.WriteLine($"   Success: {successCount}, Failed: {failCount}, Skipped (Invalid): {skippedCount}");
            Console.WriteLine($"   Total Time: {stopwatch.ElapsedMilliseconds}ms ({stopwatch.Elapsed.TotalSeconds:F1}s)");
        }

        public Task SetDisplayNameOnSelectedAsync() =>
           PerformActionOnSelectedAsync(acc => _userService.SetDisplayNameAsync(acc, AppConfig.DefaultDisplayName), "SetDisplayName");

        public Task SetAvatarOnSelectedAsync() =>
            PerformActionOnSelectedAsync(acc => _avatarService.SetAvatarAsync(acc, AppConfig.DefaultTargetUserIdForAvatarCopy), "SetAvatar");

        public Task JoinGroupOnSelectedAsync() =>
            PerformActionOnSelectedAsync(acc => _groupService.JoinGroupAsync(acc, AppConfig.DefaultGroupId), "JoinGroup");

        public Task GetBadgesOnSelectedAsync(int badgeGoal = AppConfig.DefaultBadgeGoal) =>
             PerformActionOnSelectedAsync(async acc =>
             {
                 // Pass the goal to the game launcher method
                 await _gameLauncher.LaunchGameForBadgesAsync(acc, AppConfig.DefaultBadgeGameId, badgeGoal);
                 return true; // Assume launch success is action success here
             }, $"GetBadges (Goal: {badgeGoal})", requireInteraction: true);

        public Task OpenInBrowserOnSelectedAsync() =>
           PerformActionOnSelectedAsync(acc =>
           {
               var driver = _webDriverManager.StartBrowserWithCookie(acc, AppConfig.HomePageUrl, headless: false);
               if (driver == null) { Console.WriteLine($"[-] Failed to launch browser session."); return Task.FromResult(false); }
               else { Console.WriteLine($"[+] Browser session initiated for {acc.Username}. Close the browser window manually when done."); return Task.FromResult(true); }
           }, "OpenInBrowser", requireInteraction: true, requireValidToken: false); // Token not strictly needed just to open browser with cookie

        public async Task HandleLimitedFriendRequestsAsync()
        {
            List<Account> selectedAccountsRaw = _accountManager.GetSelectedAccounts();

            Console.WriteLine($"\n[*] Pre-checking and refreshing XCSRF tokens for {selectedAccountsRaw.Count} selected accounts before limited friend actions...");
            List<Account> selectedValidAccounts = new List<Account>();
            int preCheckFailures = 0; int preCheckRefreshed = 0;

            for (int i = 0; i < selectedAccountsRaw.Count; i++)
            {
                Account acc = selectedAccountsRaw[i];
                Console.Write($"[{i + 1}/{selectedAccountsRaw.Count}] Checking {acc.Username}... ");
                if (!acc.IsValid || string.IsNullOrEmpty(acc.Cookie)) { Console.WriteLine("Skipped (Marked Invalid or No Cookie)."); preCheckFailures++; continue; }

                string oldToken = acc.XcsrfToken;
                bool tokenOk = await _authService.RefreshXCSRFTokenIfNeededAsync(acc);

                if (tokenOk)
                {
                    if (acc.XcsrfToken != oldToken && !string.IsNullOrEmpty(oldToken)) { Console.WriteLine($"   -> Token Refreshed."); preCheckRefreshed++; }
                    else if (string.IsNullOrEmpty(oldToken) && !string.IsNullOrEmpty(acc.XcsrfToken)) { Console.WriteLine($"   -> Token Initialized."); preCheckRefreshed++; }
                    else { Console.WriteLine($"   -> Token OK."); }
                    if (acc.IsValid && !string.IsNullOrEmpty(acc.XcsrfToken)) { selectedValidAccounts.Add(acc); }
                    else { Console.WriteLine($"   -> Account marked invalid after token check."); preCheckFailures++; }
                }
                else { Console.WriteLine("   -> Token Refresh/Validation FAILED."); preCheckFailures++; acc.IsValid = false; }
                await Task.Delay(Random.Shared.Next(AppConfig.SafeApiDelayMs - 200, AppConfig.SafeApiDelayMs + 300));
            }

            if (preCheckRefreshed > 0) { Console.WriteLine($"[*] Pre-check completed. {preCheckRefreshed} tokens were refreshed/initialized."); }
            if (preCheckFailures > 0) { Console.WriteLine($"[!] Pre-check skipped/failed for {preCheckFailures} accounts due to initial invalid state or token fetch failure."); }

            selectedValidAccounts = [.. selectedValidAccounts.OrderBy(a => a.UserId)];
            int count = selectedValidAccounts.Count;

            if (count < 2) { Console.WriteLine($"[!] Need at least 2 *currently valid* selected accounts for limited friend actions. Found {count} after pre-check."); return; }
            if (count > 15 && !Environment.UserInteractive) { Console.WriteLine($"[!] Warning: Running limited friends for a large number ({count}) of accounts non-interactively may take a very long time and is prone to rate limits or captchas."); }

            Console.WriteLine($"\n[*] Phase 1: Sending Friend Requests among {count} validated accounts (targeting ~2 friends each)...");
            int attemptedSends = 0, successSends = 0, failedSends = 0;
            var stopwatchSend = Stopwatch.StartNew();
            var sendAttempts = new List<Tuple<long, long>>();
            int baseSendDelay = AppConfig.CurrentFriendActionDelayMs;
            int sendRandomness = 500;

            for (int i = 0; i < count; i++)
            {
                Account receiver = selectedValidAccounts[i];
                Account sender1 = selectedValidAccounts[(i + 1) % count];
                Account? sender2 = null;
                if (count > 2) { sender2 = selectedValidAccounts[(i + 2) % count]; if (sender1.UserId == sender2?.UserId) { sender2 = selectedValidAccounts[(i + 3) % count]; if (sender1.UserId == sender2?.UserId) { sender2 = null; } } }

                Console.WriteLine($"\n  Sending requests targeting: {receiver.Username} (Index {i})");

                Console.WriteLine($"    Processing Send: {sender1.Username} -> {receiver.Username}"); attemptedSends++;
                try
                {
                    bool sendOk1 = await _friendService.SendFriendRequestAsync(sender1, receiver.UserId, receiver.Username);
                    await Task.Delay(Random.Shared.Next(Math.Max(AppConfig.MinAllowedDelayMs, baseSendDelay - sendRandomness), baseSendDelay + sendRandomness));
                    if (sendOk1) { Console.WriteLine($"    -> Send OK"); successSends++; sendAttempts.Add(Tuple.Create(sender1.UserId, receiver.UserId)); }
                    else { Console.WriteLine($"    -> Send Fail (API Error/Limit/Etc)"); failedSends++; }
                }
                catch (Exception ex) { Console.WriteLine($"    -> Error Sending S1: {ex.GetType().Name}"); failedSends++; }

                if (sender2 != null && sender1.UserId != sender2.UserId)
                {
                    Console.WriteLine($"    Processing Send: {sender2.Username} -> {receiver.Username}"); attemptedSends++;
                    try
                    {
                        bool sendOk2 = await _friendService.SendFriendRequestAsync(sender2, receiver.UserId, receiver.Username);
                        await Task.Delay(Random.Shared.Next(Math.Max(AppConfig.MinAllowedDelayMs, baseSendDelay - sendRandomness), baseSendDelay + sendRandomness));
                        if (sendOk2) { Console.WriteLine($"    -> Send OK"); successSends++; sendAttempts.Add(Tuple.Create(sender2.UserId, receiver.UserId)); }
                        else { Console.WriteLine($"    -> Send Fail (API Error/Limit/Etc)"); failedSends++; }
                    }
                    catch (Exception ex) { Console.WriteLine($"    -> Error Sending S2: {ex.GetType().Name}"); failedSends++; }
                }
                Console.WriteLine($"    -- Delaying slightly before next receiver --");
                await Task.Delay(Random.Shared.Next(AppConfig.CurrentApiDelayMs / 2, AppConfig.CurrentApiDelayMs));
            }
            stopwatchSend.Stop();
            Console.WriteLine($"[*] Phase 1 Complete: Sending Friend Requests.");
            Console.WriteLine($"   Attempted Sends: {attemptedSends}, Successful: {successSends}, Failed: {failedSends}");
            Console.WriteLine($"   Time: {stopwatchSend.ElapsedMilliseconds}ms ({stopwatchSend.Elapsed.TotalSeconds:F1}s)");


            int waitSeconds = 10;
            Console.WriteLine($"\n[*] Waiting {waitSeconds} seconds before starting Phase 2 (Accepting Requests)...");
            await Task.Delay(waitSeconds * 1000);

            Console.WriteLine($"\n[*] Phase 2: Accepting Friend Requests based on {sendAttempts.Count} attempted sends...");
            int attemptedAccepts = 0, successAccepts = 0, failedAccepts = 0;
            var stopwatchAccept = Stopwatch.StartNew();
            var acceptedPairs = new HashSet<Tuple<long, long>>();
            int baseAcceptDelay = AppConfig.CurrentFriendActionDelayMs;
            int acceptRandomness = 500;
            var acceptsToDo = sendAttempts.GroupBy(pair => pair.Item2).ToDictionary(g => g.Key, g => g.Select(pair => pair.Item1).Distinct().ToList());

            foreach (var kvp in acceptsToDo)
            {
                long receiverId = kvp.Key; List<long> senderIds = kvp.Value;
                Account? receiverAccount = selectedValidAccounts.FirstOrDefault(a => a.UserId == receiverId);
                if (receiverAccount == null)
                {
                    Console.WriteLine($"\n  Skipping accepts for receiver ID {receiverId} - Account not found in valid list (became invalid?).");
                    continue;
                }

                Console.WriteLine($"\n  Accepting requests for: {receiverAccount.Username}");

                foreach (long senderId in senderIds)
                {
                    Account? senderAccount = selectedValidAccounts.FirstOrDefault(a => a.UserId == senderId);
                    string senderUsername = senderAccount?.Username ?? $"ID {senderId}";

                    Console.WriteLine($"    Processing Accept: {senderUsername} -> {receiverAccount.Username}"); attemptedAccepts++;
                    var currentPair = Tuple.Create(senderId, receiverId);
                    if (acceptedPairs.Contains(currentPair)) { Console.WriteLine($"    -> Skipped (Already accepted in this run)"); continue; }

                    if (senderAccount == null)
                    {
                        Console.WriteLine($"    -> Skipped (Sender account {senderId} not found or became invalid).");
                        failedAccepts++;
                        continue;
                    }

                    try
                    {
                        bool acceptOk = await _friendService.AcceptFriendRequestAsync(receiverAccount, senderId, senderUsername);
                        await Task.Delay(Random.Shared.Next(Math.Max(AppConfig.MinAllowedDelayMs, baseAcceptDelay - acceptRandomness), baseAcceptDelay + acceptRandomness));
                        if (acceptOk) { Console.WriteLine($"    -> Accept OK"); successAccepts++; acceptedPairs.Add(currentPair); }
                        else { Console.WriteLine($"    -> Accept Fail (API Error/No Req/Already Friends?)"); failedAccepts++; }
                    }
                    catch (Exception ex) { Console.WriteLine($"    -> Error Accepting: {ex.GetType().Name}"); failedAccepts++; }
                    await Task.Delay(Random.Shared.Next(AppConfig.CurrentApiDelayMs / 3, AppConfig.CurrentApiDelayMs / 2));
                }
                Console.WriteLine($"    -- Delaying slightly before next receiver --");
                await Task.Delay(Random.Shared.Next(AppConfig.CurrentApiDelayMs / 2, AppConfig.CurrentApiDelayMs));
            }
            stopwatchAccept.Stop();
            Console.WriteLine($"\n[*] Phase 2 Complete: Accepting Friend Requests.");
            Console.WriteLine($"   Attempted Accepts (based on prior sends): {attemptedAccepts}");
            Console.WriteLine($"   Successful Accepts: {successAccepts} ({acceptedPairs.Count} unique pairs confirmed)");
            Console.WriteLine($"   Failed Accepts: {failedAccepts}");
            Console.WriteLine($"   Time: {stopwatchAccept.ElapsedMilliseconds}ms ({stopwatchAccept.Elapsed.TotalSeconds:F1}s)");
            Console.WriteLine($"[*] Limited friend action cycle finished.");
            Console.WriteLine($"   Reminder: Check accounts with Verify action to confirm final friend counts using your desired goal.");
        }

        public async Task<bool> VerifyAccountStatusOnSelectedAsync(int requiredFriends = AppConfig.DefaultFriendGoal, int requiredBadges = AppConfig.DefaultBadgeGoal)
        {
            _accountManager.ClearVerificationResults();

            var accountsToProcess = _accountManager.GetSelectedAccounts()
               .Where(acc => acc != null && acc.IsValid).ToList();

            int totalSelected = _accountManager.GetSelectedAccounts().Count;
            int validCount = accountsToProcess.Count;
            int skippedCount = totalSelected - validCount;

            Console.WriteLine($"\n[>>] Executing Action: Verify Account Status (Friends >= {requiredFriends}, Badges >= {requiredBadges}) for {validCount} valid account(s)...");
            if (skippedCount > 0) { Console.WriteLine($"   ({skippedCount} selected accounts were invalid and will be skipped)"); }
            if (validCount == 0) { Console.WriteLine($"[!] No valid accounts selected for verification."); return false; }

            int passedCount = 0, failedReqCount = 0, failedErrCount = 0;
            var stopwatch = Stopwatch.StartNew();

            for (int i = 0; i < accountsToProcess.Count; i++)
            {
                Account acc = accountsToProcess[i];
                Console.WriteLine($"[{i + 1}/{validCount}] Verifying: {acc.Username} (ID: {acc.UserId})");
                int friendCount = -1, badgeCount = -1; bool errorOccurred = false;
                VerificationStatus currentStatus = VerificationStatus.NotChecked;

                try
                {
                    Console.Write("   Checking Friends... ");
                    friendCount = await _friendService.GetFriendCountAsync(acc);
                    if (friendCount == -1) { Console.WriteLine("Failed to get count."); errorOccurred = true; }
                    else { Console.WriteLine($"{friendCount} found."); }

                    if (!errorOccurred)
                    {
                        Console.Write("   Checking Badges... ");
                        badgeCount = await _badgeService.GetBadgeCountAsync(acc, limit: 10);
                        if (badgeCount == -1) { Console.WriteLine("Failed to get count."); errorOccurred = true; }
                        else { Console.WriteLine($"{badgeCount} found (in first 10)."); }
                    }

                    if (errorOccurred)
                    {
                        currentStatus = VerificationStatus.Error;
                        Console.WriteLine($"   -> Status: ERROR (Could not retrieve counts)");
                        failedErrCount++;
                    }
                    else
                    {
                        bool friendsOk = friendCount >= requiredFriends;
                        bool badgesOk = badgeCount >= requiredBadges;
                        string friendStatus = friendsOk ? "OK" : "FAIL";
                        string badgeStatus = badgesOk ? "OK" : "FAIL";
                        Console.WriteLine($"   -> Friends: {friendCount} (Required: {requiredFriends}) [{friendStatus}]");
                        Console.WriteLine($"   -> Badges: {badgeCount} (Required: {requiredBadges}) [{badgeStatus}] (Note: Checked first 10 only)");

                        if (friendsOk && badgesOk)
                        {
                            currentStatus = VerificationStatus.Passed;
                            Console.WriteLine($"   -> Overall Status: PASS"); passedCount++;
                        }
                        else
                        {
                            currentStatus = VerificationStatus.Failed;
                            Console.WriteLine($"   -> Overall Status: FAIL (Requirements Not Met)"); failedReqCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[!] Runtime Error during verification for {acc.Username}: {ex.GetType().Name} - {ex.Message}");
                    currentStatus = VerificationStatus.Error;
                    failedErrCount++;
                }
                finally
                {
                    _accountManager.SetVerificationStatus(acc.UserId, currentStatus);
                }
                await Task.Delay(AppConfig.CurrentApiDelayMs / 2);
            }
            stopwatch.Stop();
            Console.WriteLine($"[<<] Action 'Verify Account Status' Finished.");
            Console.WriteLine($"   Passed: {passedCount}, Failed (Reqs): {failedReqCount}, Failed (Error): {failedErrCount}, Skipped (Invalid): {skippedCount}");
            Console.WriteLine($"   Total Time: {stopwatch.ElapsedMilliseconds}ms ({stopwatch.Elapsed.TotalSeconds:F1}s)");

            return failedReqCount > 0 || failedErrCount > 0;
        }

        public async Task ExecuteAllAutoAsync()
        {
            Console.WriteLine($"\n[*] Executing Multi-Action Sequence (Auto - Uses Defaults)...");
            Console.WriteLine($"   Sequence: Set Name -> Set Avatar -> Limited Friends -> Get Badges");

            await SetDisplayNameOnSelectedAsync();
            await Task.Delay(AppConfig.CurrentApiDelayMs);

            await SetAvatarOnSelectedAsync();
            await Task.Delay(AppConfig.CurrentApiDelayMs);

            await HandleLimitedFriendRequestsAsync();

            if (!Environment.UserInteractive)
            {
                Console.WriteLine("[!] Skipping 'Get Badges' step in non-interactive environment for 'Execute All Auto'.");
            }
            else
            {
                await GetBadgesOnSelectedAsync(AppConfig.DefaultBadgeGoal);
                await Task.Delay(AppConfig.CurrentApiDelayMs);
            }


            Console.WriteLine($"[*] Multi-Action Sequence Complete.");
        }
    }
}
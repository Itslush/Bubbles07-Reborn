using System.Diagnostics;
using Newtonsoft.Json;
using _Csharpified;
using Core;
using Models;
using Roblox.Automation;
using Roblox.Services;
using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

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

        private static AvatarDetails? _targetAvatarDetailsCache;
        private static long _targetAvatarCacheSourceId = -1;
        private static readonly object _avatarCacheLock = new object();

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
            maxLength = Math.Max(0, maxLength);
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= maxLength ? value : value[..maxLength] + "...";
        }

        private async Task<AvatarDetails?> GetOrFetchTargetAvatarDetailsAsync(long sourceUserId)
        {
            lock (_avatarCacheLock)
            {
                if (_targetAvatarDetailsCache != null && _targetAvatarCacheSourceId == sourceUserId)
                {
                    return _targetAvatarDetailsCache;
                }
            }

            Console.WriteLine($"[*] Fetching target avatar details from User ID {sourceUserId} for comparison/cache...");
            var fetchedDetails = await _avatarService.FetchAvatarDetailsAsync(sourceUserId);

            if (fetchedDetails != null)
            {
                lock (_avatarCacheLock)
                {
                    _targetAvatarDetailsCache = fetchedDetails;
                    _targetAvatarCacheSourceId = sourceUserId;
                    Console.WriteLine($"[+] Target avatar details cached successfully for {sourceUserId}.");
                }
                return fetchedDetails;
            }
            else
            {
                Console.WriteLine($"[!] Failed to fetch target avatar details for comparison ({sourceUserId}). Cannot perform pre-check.");
                lock (_avatarCacheLock) { _targetAvatarDetailsCache = null; _targetAvatarCacheSourceId = -1; }
                return null;
            }
        }

        private async Task PerformActionOnSelectedAsync(
            Func<Account, Task<(bool Success, bool Skipped)>> action,
            string actionName,
            bool requireInteraction = false,
            bool requireValidToken = true)
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
            int skippedInvalidCount = totalSelected - validCount;

            Console.WriteLine($"\n[>>] Executing Action: {actionName} for {validCount} valid account(s)...");
            if (skippedInvalidCount > 0)
            {
                string reason = requireValidToken ? "invalid/lacked XCSRF" : "invalid";
                Console.WriteLine($"   ({skippedInvalidCount} selected accounts were {reason} and will be skipped)");
            }
            if (validCount == 0)
            {
                Console.WriteLine($"[!] No valid accounts selected for this action.");
                return;
            }

            int successCount = 0, failCount = 0, skippedPreCheckCount = 0;
            var stopwatch = Stopwatch.StartNew();

            for (int i = 0; i < accountsToProcess.Count; i++)
            {
                Account acc = accountsToProcess[i];
                Console.WriteLine($"\n[{i + 1}/{validCount}] Processing: {acc.Username} (ID: {acc.UserId}) for '{actionName}'");
                try
                {
                    var (success, skipped) = await action(acc);

                    if (skipped)
                    {
                        skippedPreCheckCount++;
                    }
                    else if (success)
                    {
                        successCount++;
                    }
                    else
                    {
                        failCount++;
                        Console.WriteLine($"   [-] Action '{actionName}' reported failure for {acc.Username}.");
                    }
                }
                catch (InvalidOperationException ioex) { Console.WriteLine($"[!] Config/State Error for {acc.Username} during '{actionName}': {ioex.Message}"); failCount++; }
                catch (HttpRequestException hrex) { Console.WriteLine($"[!] Network Error during '{actionName}' for {acc.Username}: {hrex.StatusCode} - {hrex.Message}"); failCount++; }
                catch (JsonException jex) { Console.WriteLine($"[!] JSON Error during '{actionName}' for {acc.Username}: {jex.Message}"); failCount++; }
                catch (Exception ex) { Console.WriteLine($"[!] Runtime Error during '{actionName}' for {acc.Username}: {ex.GetType().Name} - {ex.Message}"); failCount++; }
                finally
                {
                    if (i < accountsToProcess.Count - 1)
                    {
                        await Task.Delay(AppConfig.CurrentApiDelayMs / 2);
                    }
                }
            }
            stopwatch.Stop();

            Console.WriteLine($"\n[<<] Action '{actionName}' Finished.");
            Console.WriteLine($"   Success (Action Performed): {successCount}, Failed: {failCount}");
            Console.WriteLine($"   Skipped (Pre-Check Met): {skippedPreCheckCount}, Skipped (Invalid Account): {skippedInvalidCount}");
            Console.WriteLine($"   Total Time: {stopwatch.ElapsedMilliseconds}ms ({stopwatch.Elapsed.TotalSeconds:F1}s)");
        }

        public Task SetDisplayNameOnSelectedAsync() =>
           PerformActionOnSelectedAsync(async acc =>
           {
               string targetName = AppConfig.DefaultDisplayName;
               Console.WriteLine($"   Checking current display name for {acc.Username}...");
               string? currentName = await _userService.GetCurrentDisplayNameAsync(acc);

               if (currentName == null)
               {
                   Console.WriteLine($"   [-] Failed to fetch current display name. Proceeding with set attempt...");
                   bool setResult = await _userService.SetDisplayNameAsync(acc, targetName);
                   if (setResult) Console.WriteLine($"      [+] Display name set successfully.");
                   else Console.WriteLine($"      [-] Display name set failed.");
                   return (setResult, false);
               }
               else if (string.Equals(currentName, targetName, StringComparison.OrdinalIgnoreCase))
               {
                   Console.WriteLine($"   [*] Skipping SetDisplayName: Already set to '{targetName}'.");
                   return (true, true);
               }
               else
               {
                   Console.WriteLine($"   Current name is '{currentName}'. Attempting update to '{targetName}'...");
                   bool setResult = await _userService.SetDisplayNameAsync(acc, targetName);
                   if (setResult) Console.WriteLine($"      [+] Display name set successfully.");
                   else Console.WriteLine($"      [-] Display name set failed.");
                   return (setResult, false);
               }
           }, "SetDisplayName");

        public Task SetAvatarOnSelectedAsync() =>
            PerformActionOnSelectedAsync(async acc =>
            {
                long targetUserId = AppConfig.DefaultTargetUserIdForAvatarCopy;
                Console.WriteLine($"   Checking current avatar for {acc.Username} against target {targetUserId}...");

                AvatarDetails? targetAvatarDetails = await GetOrFetchTargetAvatarDetailsAsync(targetUserId);
                if (targetAvatarDetails == null)
                {
                    Console.WriteLine($"   [-] Critical Error: Could not get target avatar details for {targetUserId}. Cannot perform pre-check or set avatar.");
                    return (false, false);
                }

                Console.WriteLine($"   Fetching current avatar details for {acc.Username}...");
                AvatarDetails? currentAvatarDetails = await _avatarService.FetchAvatarDetailsAsync(acc.UserId);

                if (currentAvatarDetails == null)
                {
                    Console.WriteLine($"   [-] Failed to fetch current avatar details for {acc.Username}. Proceeding with set attempt...");
                    bool setResult = await _avatarService.SetAvatarAsync(acc, targetUserId);
                    if (setResult) Console.WriteLine($"      [+] Avatar set successfully.");
                    else Console.WriteLine($"      [-] Avatar set failed.");
                    return (setResult, false);
                }
                else
                {
                    bool match = _avatarService.CompareAvatarDetails(currentAvatarDetails, targetAvatarDetails);
                    if (match)
                    {
                        Console.WriteLine($"   [*] Skipping SetAvatar: Current avatar already matches target {targetUserId}.");
                        return (true, true);
                    }
                    else
                    {
                        Console.WriteLine($"   Current avatar differs from target. Attempting update...");
                        bool setResult = await _avatarService.SetAvatarAsync(acc, targetUserId);
                        if (setResult) Console.WriteLine($"      [+] Avatar set successfully.");
                        else Console.WriteLine($"      [-] Avatar set failed.");
                        return (setResult, false);
                    }
                }
            }, "SetAvatar");

        public Task JoinGroupOnSelectedAsync() =>
            PerformActionOnSelectedAsync(async acc => {
                bool success = await _groupService.JoinGroupAsync(acc, AppConfig.DefaultGroupId);
                if (success) Console.WriteLine($"      [+] Join group request sent/processed.");
                else Console.WriteLine($"      [-] Join group request failed.");
                return (success, false);
            }, "JoinGroup");

        public Task GetBadgesOnSelectedAsync(int badgeGoal = AppConfig.DefaultBadgeGoal) =>
             PerformActionOnSelectedAsync(async acc =>
             {
                 Console.WriteLine($"   Checking current badge count for {acc.Username} (Goal: >= {badgeGoal})...");
                 int currentBadgeCount = await _badgeService.GetBadgeCountAsync(acc, 100);

                 if (currentBadgeCount == -1)
                 {
                     Console.WriteLine($"   [-] Failed to fetch current badge count. Proceeding with game launch attempt anyway...");
                 }
                 else if (currentBadgeCount >= badgeGoal)
                 {
                     Console.WriteLine($"   [*] Skipping GetBadges: Account already has {currentBadgeCount} (>= {badgeGoal}) recent badges.");
                     return (true, true);
                 }
                 else
                 {
                     Console.WriteLine($"   Current badge count is {currentBadgeCount} (< {badgeGoal}). Needs game launch.");
                 }

                 Console.WriteLine($"   Attempting to launch game {AppConfig.DefaultBadgeGameId}...");
                 await _gameLauncher.LaunchGameForBadgesAsync(acc, AppConfig.DefaultBadgeGameId, badgeGoal);
                 Console.WriteLine($"      [+] Game launch sequence initiated.");
                 return (true, false);

             }, $"GetBadges (Goal: {badgeGoal})", requireInteraction: true);

        public Task OpenInBrowserOnSelectedAsync() =>
           PerformActionOnSelectedAsync(acc =>
           {
               var driver = _webDriverManager.StartBrowserWithCookie(acc, AppConfig.HomePageUrl, headless: false);
               if (driver == null)
               {
                   Console.WriteLine($"   [-] Failed to launch browser session.");
                   return Task.FromResult((Success: false, Skipped: false));
               }
               else
               {
                   Console.WriteLine($"   [+] Browser session initiated for {acc.Username}. Close the browser window manually when done.");
                   return Task.FromResult((Success: true, Skipped: false));
               }
           }, "OpenInBrowser", requireInteraction: true, requireValidToken: false);

        public async Task HandleLimitedFriendRequestsAsync()
        {
            List<Account> selectedAccountsRaw = _accountManager.GetSelectedAccounts();
            int friendGoal = AppConfig.DefaultFriendGoal;

            Console.WriteLine($"\n[*] Executing Action: Limited Friend Actions (Goal: >= {friendGoal} friends)");
            Console.WriteLine($"[*] Phase 0: Pre-checking XCSRF and Friend Counts for {selectedAccountsRaw.Count} selected accounts...");

            List<Account> selectedValidAccounts = new List<Account>();
            List<Account> accountsNeedingFriends = new List<Account>();
            int preCheckFailures = 0; int preCheckRefreshed = 0;
            int alreadyMetGoal = 0; int friendCheckErrors = 0;

            for (int i = 0; i < selectedAccountsRaw.Count; i++)
            {
                Account acc = selectedAccountsRaw[i];
                Console.Write($"\n[{i + 1}/{selectedAccountsRaw.Count}] Checking {acc.Username}... ");

                if (!acc.IsValid || string.IsNullOrEmpty(acc.Cookie)) { Console.WriteLine("Skipped (Marked Invalid or No Cookie)."); preCheckFailures++; continue; }
                string oldToken = acc.XcsrfToken;
                bool tokenOk = await _authService.RefreshXCSRFTokenIfNeededAsync(acc);

                if (!tokenOk) { Console.WriteLine("   -> Token Refresh/Validation FAILED."); preCheckFailures++; acc.IsValid = false; continue; }

                if (acc.XcsrfToken != oldToken && !string.IsNullOrEmpty(oldToken)) { Console.WriteLine($"   -> Token Refreshed."); preCheckRefreshed++; }
                else if (string.IsNullOrEmpty(oldToken) && !string.IsNullOrEmpty(acc.XcsrfToken)) { Console.WriteLine($"   -> Token Initialized."); preCheckRefreshed++; }
                else { Console.WriteLine($"   -> Token OK."); }

                if (!acc.IsValid || string.IsNullOrEmpty(acc.XcsrfToken)) { Console.WriteLine($"   -> Account marked invalid after token check."); preCheckFailures++; continue; }

                selectedValidAccounts.Add(acc);

                Console.Write($"   Checking friend count... ");
                int friendCount = await _friendService.GetFriendCountAsync(acc);
                await Task.Delay(AppConfig.CurrentApiDelayMs / 3);

                if (friendCount == -1)
                {
                    Console.WriteLine($"Failed!");
                    friendCheckErrors++;
                }
                else
                {
                    Console.Write($"{friendCount} found. ");
                    if (friendCount >= friendGoal)
                    {
                        Console.WriteLine($"Goal Met (>= {friendGoal}). Skipping friend actions for this account.");
                        alreadyMetGoal++;
                    }
                    else
                    {
                        Console.WriteLine($"Needs friends (< {friendGoal}). Adding to action list.");
                        accountsNeedingFriends.Add(acc);
                    }
                }
            }

            Console.WriteLine($"\n[*] Pre-check Summary:");
            if (preCheckRefreshed > 0) Console.WriteLine($"   {preCheckRefreshed} tokens were refreshed/initialized.");
            if (preCheckFailures > 0) Console.WriteLine($"   {preCheckFailures} accounts skipped/failed (Invalid state or token issues).");
            if (friendCheckErrors > 0) Console.WriteLine($"   {friendCheckErrors} accounts had errors checking friend count (excluded from actions).");
            if (alreadyMetGoal > 0) Console.WriteLine($"   {alreadyMetGoal} valid accounts already met the friend goal ({friendGoal}) and were skipped.");
            Console.WriteLine($"   {accountsNeedingFriends.Count} valid accounts need friends and will proceed.");

            accountsNeedingFriends = [.. accountsNeedingFriends.OrderBy(a => a.UserId)];
            int count = accountsNeedingFriends.Count;

            if (count < 2)
            {
                Console.WriteLine($"[!] Need at least 2 valid accounts *below the friend goal* for limited friend actions. Found {count}. Aborting friend cycle.");
                return;
            }
            if (count > 15 && !Environment.UserInteractive) { Console.WriteLine($"[!] Warning: Running limited friends for a large number ({count}) of accounts non-interactively may take a very long time and is prone to rate limits or captchas."); }

            Console.WriteLine($"\n[*] Phase 1: Sending Friend Requests among {count} accounts needing friends...");
            int attemptedSends = 0, successSends = 0, failedSends = 0;
            var stopwatchSend = Stopwatch.StartNew();
            var sendAttempts = new List<Tuple<long, long>>();
            int baseSendDelay = AppConfig.CurrentFriendActionDelayMs;
            int sendRandomness = 500;

            for (int i = 0; i < count; i++)
            {
                Account receiver = accountsNeedingFriends[i];
                Account sender1 = accountsNeedingFriends[(i + 1) % count];
                Account? sender2 = null;
                if (count > 2) { sender2 = accountsNeedingFriends[(i + 2) % count]; if (sender1.UserId == sender2?.UserId) { sender2 = accountsNeedingFriends[(i + 3) % count]; if (sender1.UserId == sender2?.UserId) { sender2 = null; } } }

                Console.WriteLine($"\n  Sending requests targeting: {receiver.Username} (Index {i})");

                Console.WriteLine($"    Processing Send: {sender1.Username} -> {receiver.Username}"); attemptedSends++;
                try
                {
                    bool sendOk1 = await _friendService.SendFriendRequestAsync(sender1, receiver.UserId, receiver.Username);
                    await Task.Delay(Random.Shared.Next(Math.Max(AppConfig.MinAllowedDelayMs, baseSendDelay - sendRandomness), baseSendDelay + sendRandomness));
                    if (sendOk1) { Console.WriteLine($"    -> Send OK"); successSends++; sendAttempts.Add(Tuple.Create(sender1.UserId, receiver.UserId)); }
                    else { Console.WriteLine($"    -> Send Fail (API Error/Limit/Already Sent/Etc)"); failedSends++; }
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
                        else { Console.WriteLine($"    -> Send Fail (API Error/Limit/Already Sent/Etc)"); failedSends++; }
                    }
                    catch (Exception ex) { Console.WriteLine($"    -> Error Sending S2: {ex.GetType().Name}"); failedSends++; }
                }

                if (i < count - 1) await Task.Delay(Random.Shared.Next(AppConfig.CurrentApiDelayMs / 2, AppConfig.CurrentApiDelayMs));
            }
            stopwatchSend.Stop();
            Console.WriteLine($"[*] Phase 1 Complete: Sending Friend Requests.");
            Console.WriteLine($"   Attempted Sends: {attemptedSends}, Successful: {successSends}, Failed: {failedSends}");
            Console.WriteLine($"   Time: {stopwatchSend.ElapsedMilliseconds}ms ({stopwatchSend.Elapsed.TotalSeconds:F1}s)");

            if (successSends == 0)
            {
                Console.WriteLine("\n[!] No friend requests were successfully sent in Phase 1. Skipping Phase 2.");
                return;
            }

            int waitSeconds = 60;
            Console.WriteLine($"\n[*] Waiting {waitSeconds} seconds before starting Phase 2 (Accepting Requests)...");
            await Task.Delay(waitSeconds * 1000);

            Console.WriteLine($"\n[*] Phase 2: Fetching and Accepting Pending Friend Requests...");
            int attemptedAccepts = 0, successAccepts = 0, failedAccepts = 0;
            var stopwatchAccept = Stopwatch.StartNew();
            var acceptedPairs = new HashSet<Tuple<long, long>>();
            int baseAcceptDelay = AppConfig.CurrentFriendActionDelayMs;
            int acceptRandomness = 500;

            var needingFriendsIds = accountsNeedingFriends.Select(a => a.UserId).ToHashSet();

            foreach (Account receiverAccount in accountsNeedingFriends)
            {
                Console.WriteLine($"\n  Processing receiver: {receiverAccount.Username}");

                if (!receiverAccount.IsValid || string.IsNullOrEmpty(receiverAccount.XcsrfToken))
                {
                    Console.WriteLine($"    -> Skipping receiver (became invalid).");
                    continue;
                }

                Console.Write($"    Fetching pending requests...");
                List<long> pendingSenderIds = await _friendService.GetPendingFriendRequestSendersAsync(receiverAccount);
                await Task.Delay(AppConfig.CurrentApiDelayMs / 2);

                if (pendingSenderIds.Count == 0)
                {
                    Console.WriteLine($" No pending requests found via API.");
                    continue;
                }
                Console.WriteLine($" Found {pendingSenderIds.Count} total pending.");

                List<long> relevantSenderIds = pendingSenderIds
                    .Where(id => needingFriendsIds.Contains(id) && id != receiverAccount.UserId)
                    .ToList();

                if (relevantSenderIds.Count == 0)
                {
                    Console.WriteLine($"    None of the pending requests are from the other accounts in this friend cycle.");
                    continue;
                }

                Console.WriteLine($"    Found {relevantSenderIds.Count} relevant pending request(s) to accept.");

                foreach (long senderId in relevantSenderIds)
                {
                    string senderUsername = accountsNeedingFriends.FirstOrDefault(a => a.UserId == senderId)?.Username ?? $"ID {senderId}";

                    Console.WriteLine($"    Processing Accept: {senderUsername} -> {receiverAccount.Username}");
                    attemptedAccepts++;
                    var currentPair = Tuple.Create(senderId, receiverAccount.UserId);
                    if (acceptedPairs.Contains(currentPair)) { Console.WriteLine($"    -> Skipped (Already accepted in this run)"); continue; }

                    Console.Write($"    Refreshing token for receiver {receiverAccount.Username}... ");
                    bool receiverTokenOk = await _authService.RefreshXCSRFTokenIfNeededAsync(receiverAccount);
                    if (!receiverTokenOk || !receiverAccount.IsValid || string.IsNullOrEmpty(receiverAccount.XcsrfToken))
                    {
                        Console.WriteLine($"FAILED."); Console.WriteLine($"    -> Skipped (Receiver account {receiverAccount.Username} became invalid or token refresh failed before accept)."); failedAccepts++; continue;
                    }
                    Console.WriteLine("OK.");

                    try
                    {
                        bool acceptOk = await _friendService.AcceptFriendRequestAsync(receiverAccount, senderId, senderUsername);
                        await Task.Delay(Random.Shared.Next(Math.Max(AppConfig.MinAllowedDelayMs, baseAcceptDelay - acceptRandomness), baseAcceptDelay + acceptRandomness));
                        if (acceptOk) { Console.WriteLine($"    -> Accept OK"); successAccepts++; acceptedPairs.Add(currentPair); }
                        else { Console.WriteLine($"    -> Accept Fail (API Error/Already Friends?)"); failedAccepts++; }
                    }
                    catch (Exception ex) { Console.WriteLine($"    -> Error Accepting: {ex.GetType().Name}"); failedAccepts++; }

                    if (relevantSenderIds.IndexOf(senderId) < relevantSenderIds.Count - 1) await Task.Delay(Random.Shared.Next(AppConfig.CurrentApiDelayMs / 3, AppConfig.CurrentApiDelayMs / 2));
                }

                if (accountsNeedingFriends.IndexOf(receiverAccount) < accountsNeedingFriends.Count - 1) await Task.Delay(Random.Shared.Next(AppConfig.CurrentApiDelayMs / 2, AppConfig.CurrentApiDelayMs));

            }

            stopwatchAccept.Stop();
            Console.WriteLine($"\n[*] Phase 2 Complete: Accepting Friend Requests.");
            Console.WriteLine($"   Attempted Accepts (based on fetched pending): {attemptedAccepts}, Successful: {successAccepts} ({acceptedPairs.Count} unique pairs confirmed), Failed: {failedAccepts}");
            Console.WriteLine($"   Time: {stopwatchAccept.ElapsedMilliseconds}ms ({stopwatchAccept.Elapsed.TotalSeconds:F1}s)");
            Console.WriteLine($"[*] Limited friend action cycle finished for accounts needing friends.");
            Console.WriteLine($"   Reminder: Check accounts with Verify action to confirm final friend counts.");
        }

        public async Task<bool> VerifyAccountStatusOnSelectedAsync(
            int requiredFriends = AppConfig.DefaultFriendGoal,
            int requiredBadges = AppConfig.DefaultBadgeGoal,
            string expectedDisplayName = AppConfig.DefaultDisplayName,
            long expectedAvatarSourceId = AppConfig.DefaultTargetUserIdForAvatarCopy)
        {
            _accountManager.ClearVerificationResults();
            var accountsToProcess = _accountManager.GetSelectedAccounts().Where(acc => acc != null && acc.IsValid).ToList();
            int totalSelected = _accountManager.GetSelectedAccounts().Count; int validCount = accountsToProcess.Count; int skippedCount = totalSelected - validCount;
            Console.WriteLine($"\n[>>] Executing Action: Verify Account Status for {validCount} valid account(s)...");
            Console.WriteLine($"   Requirements: Friends >= {requiredFriends}, Badges >= {requiredBadges} (Top 10), Name == '{expectedDisplayName}', Avatar Source == {expectedAvatarSourceId}");
            if (skippedCount > 0) Console.WriteLine($"   ({skippedCount} selected accounts were invalid and will be skipped)");
            if (validCount == 0) { Console.WriteLine($"[!] No valid accounts selected for verification."); return false; }

            AvatarDetails? targetAvatarDetails = await GetOrFetchTargetAvatarDetailsAsync(expectedAvatarSourceId);
            int passedCount = 0, failedReqCount = 0, failedErrCount = 0; var stopwatch = Stopwatch.StartNew();

            for (int i = 0; i < accountsToProcess.Count; i++)
            {
                Account acc = accountsToProcess[i];
                Console.WriteLine($"\n[{i + 1}/{validCount}] Verifying: {acc.Username} (ID: {acc.UserId})");
                int friendCount = -1, badgeCount = -1; string? currentDisplayName = null; AvatarDetails? currentAvatarDetails = null;
                bool errorOccurred = false; VerificationStatus currentStatus = VerificationStatus.NotChecked;
                try
                {
                    Console.Write("   Checking Friends... "); friendCount = await _friendService.GetFriendCountAsync(acc);
                    if (friendCount == -1) { Console.WriteLine("Failed."); errorOccurred = true; } else { Console.WriteLine($"{friendCount} found."); }
                    if (!errorOccurred) await Task.Delay(AppConfig.CurrentApiDelayMs / 4);

                    if (!errorOccurred)
                    {
                        Console.Write("   Checking Badges... "); badgeCount = await _badgeService.GetBadgeCountAsync(acc, limit: 10);
                        if (badgeCount == -1) { Console.WriteLine("Failed."); errorOccurred = true; } else { Console.WriteLine($"{badgeCount} found (Top 10)."); }
                        if (!errorOccurred) await Task.Delay(AppConfig.CurrentApiDelayMs / 4);
                    }
                    if (!errorOccurred)
                    {
                        Console.Write("   Checking Display Name... "); currentDisplayName = await _userService.GetCurrentDisplayNameAsync(acc);
                        if (currentDisplayName == null) { Console.WriteLine("Failed."); errorOccurred = true; } else { Console.WriteLine($"'{currentDisplayName}' found."); }
                        if (!errorOccurred) await Task.Delay(AppConfig.CurrentApiDelayMs / 4);
                    }
                    if (!errorOccurred && targetAvatarDetails != null)
                    {
                        Console.Write("   Checking Avatar... "); currentAvatarDetails = await _avatarService.FetchAvatarDetailsAsync(acc.UserId);
                        if (currentAvatarDetails == null) { Console.WriteLine("Failed."); errorOccurred = true; } else { Console.WriteLine($"Details retrieved."); }
                    }
                    else if (targetAvatarDetails == null) { Console.WriteLine("   Skipping Avatar Check (Target details unavailable)."); }

                    if (errorOccurred)
                    {
                        currentStatus = VerificationStatus.Error; Console.WriteLine($"   -> Status: ERROR (Data Fetch Failed)"); failedErrCount++;
                    }
                    else
                    {
                        bool friendsOk = friendCount >= requiredFriends; bool badgesOk = badgeCount >= requiredBadges;
                        bool displayNameOk = string.Equals(currentDisplayName, expectedDisplayName, StringComparison.OrdinalIgnoreCase);
                        bool avatarOk = targetAvatarDetails == null || (currentAvatarDetails != null && _avatarService.CompareAvatarDetails(currentAvatarDetails, targetAvatarDetails));
                        string fStat = friendsOk ? "OK" : "FAIL"; string bStat = badgesOk ? "OK" : "FAIL"; string nStat = displayNameOk ? "OK" : "FAIL";
                        string aStat = targetAvatarDetails == null ? "SKIP" : (currentAvatarDetails == null ? "ERR" : (avatarOk ? "OK" : "FAIL"));
                        Console.WriteLine($"   -> Friends:       {friendCount} (Req: {requiredFriends}) [{fStat}]");
                        Console.WriteLine($"   -> Badges:        {badgeCount} (Req: {requiredBadges}) [{bStat}]");
                        Console.WriteLine($"   -> Display Name:  '{currentDisplayName ?? "N/A"}' (Exp: '{expectedDisplayName}') [{nStat}]");
                        Console.WriteLine($"   -> Avatar Match:  (Src: {expectedAvatarSourceId}) [{aStat}]");
                        if (friendsOk && badgesOk && displayNameOk && avatarOk) { currentStatus = VerificationStatus.Passed; Console.WriteLine($"   -> Overall Status: PASS"); passedCount++; }
                        else { currentStatus = VerificationStatus.Failed; Console.WriteLine($"   -> Overall Status: FAIL (Reqs Not Met)"); failedReqCount++; }
                    }
                }
                catch (Exception ex) { Console.WriteLine($"[!] Runtime Error: {ex.GetType().Name} - {ex.Message}"); currentStatus = VerificationStatus.Error; failedErrCount++; }
                finally { _accountManager.SetVerificationStatus(acc.UserId, currentStatus); if (i < accountsToProcess.Count - 1) await Task.Delay(AppConfig.MinAllowedDelayMs / 2); }
            }
            stopwatch.Stop();
            Console.WriteLine($"\n[<<] Action 'Verify Account Status' Finished.");
            Console.WriteLine($"   Passed: {passedCount}, Failed (Reqs): {failedReqCount}, Failed (Error): {failedErrCount}, Skipped (Invalid): {skippedCount}");
            Console.WriteLine($"   Total Time: {stopwatch.ElapsedMilliseconds}ms ({stopwatch.Elapsed.TotalSeconds:F1}s)");
            return failedReqCount > 0 || failedErrCount > 0;
        }

        public async Task ExecuteAllAutoAsync()
        {
            Console.WriteLine($"\n[*] Executing Action: Execute All Auto (Uses Defaults)");
            Console.WriteLine($"   Sequence: Set Name -> Set Avatar -> Limited Friends -> Get Badges (if interactive)");
            Console.WriteLine($"   Defaults: Name='{AppConfig.DefaultDisplayName}', AvatarSrc={AppConfig.DefaultTargetUserIdForAvatarCopy}, FriendGoal={AppConfig.DefaultFriendGoal}, BadgeGoal={AppConfig.DefaultBadgeGoal}, BadgeGame={AppConfig.DefaultBadgeGameId}");
            Console.WriteLine($"   (Actions will be skipped if prerequisites are already met)");

            await SetDisplayNameOnSelectedAsync();
            await SetAvatarOnSelectedAsync();
            await HandleLimitedFriendRequestsAsync();

            if (!Environment.UserInteractive)
            {
                Console.WriteLine("[!] Skipping 'Get Badges' step in non-interactive environment for 'Execute All Auto'.");
            }
            else
            {
                await GetBadgesOnSelectedAsync(AppConfig.DefaultBadgeGoal);
            }

            Console.WriteLine($"\n[*] Multi-Action Sequence Complete.");
        }
    }
}
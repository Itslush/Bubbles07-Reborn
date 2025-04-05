using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using _Csharpified;
using Core;
using Models;
using Newtonsoft.Json;
using Roblox.Automation;
using Roblox.Services;
using UI;

namespace Actions
{
    internal enum AcceptAttemptResult
    {
        Accepted,
        Failed,
        Skipped_AlreadyDone,
        Skipped_InvalidSender,
        Skipped_InvalidReceiver
    }

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
        private static readonly Lock _avatarCacheLock = new();

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

        private async Task<AvatarDetails?> GetOrFetchTargetAvatarDetailsAsync(long sourceUserId)
        {
            lock (_avatarCacheLock)
            {
                if (_targetAvatarDetailsCache != null && _targetAvatarCacheSourceId == sourceUserId)
                {
                    return _targetAvatarDetailsCache;
                }
            }

            ConsoleUI.WriteInfoLine($"Fetching target avatar details from User ID {sourceUserId} for comparison/cache...");
            var fetchedDetails = await _avatarService.FetchAvatarDetailsAsync(sourceUserId);

            if (fetchedDetails != null)
            {
                lock (_avatarCacheLock)
                {
                    _targetAvatarDetailsCache = fetchedDetails;
                    _targetAvatarCacheSourceId = sourceUserId;
                    ConsoleUI.WriteSuccessLine($"Target avatar details cached successfully for {sourceUserId}.");
                }
                return fetchedDetails;
            }
            else
            {
                ConsoleUI.WriteErrorLine($"Failed to fetch target avatar details for comparison ({sourceUserId}). Cannot perform pre-check.");
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
                ConsoleUI.WriteErrorLine($"Skipping interactive action '{actionName}' in non-interactive environment.");
                return;
            }

            var selectedAccountsRaw = _accountManager.GetSelectedAccounts();
            var accountsToProcess = selectedAccountsRaw
                .Where(acc => acc != null && acc.IsValid && (!requireValidToken || !string.IsNullOrEmpty(acc.XcsrfToken)))
                .ToList();

            int totalSelected = selectedAccountsRaw.Count;
            int validCount = accountsToProcess.Count;
            int skippedInvalidCount = totalSelected - validCount;

            Console.WriteLine($"\n[>>] Executing Action: {actionName} for {validCount} valid account(s)...");
            if (skippedInvalidCount > 0)
            {
                string reason = requireValidToken ? "invalid / lacked XCSRF" : "marked invalid";
                Console.WriteLine($"   ({skippedInvalidCount} selected accounts were {reason} and will be skipped)");
            }
            if (validCount == 0 && totalSelected > 0)
            {
                ConsoleUI.WriteErrorLine($"All selected accounts were skipped due to being invalid or lacking required tokens.");
                return;
            }
            else if (validCount == 0 && totalSelected == 0)
            {
                ConsoleUI.WriteErrorLine($"No accounts selected for this action.");
                return;
            }

            int successCount = 0, failCount = 0, skippedPreCheckCount = 0;
            var stopwatch = Stopwatch.StartNew();
            int maxRetries = AppConfig.CurrentMaxApiRetries;
            int retryDelayMs = AppConfig.CurrentApiRetryDelayMs;
            int baseDelayMs = AppConfig.CurrentApiDelayMs;

            for (int i = 0; i < accountsToProcess.Count; i++)
            {
                Account acc = accountsToProcess[i];
                Console.WriteLine($"\n[{i + 1}/{validCount}] Processing: {acc.Username} (ID: {acc.UserId}) for '{actionName}'");

                bool finalSuccess = false;
                bool finalSkipped = false;
                Exception? lastException = null;

                for (int attempt = 0; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        if (attempt > 0)
                        {
                            Console.WriteLine($"   [~] Retrying action '{actionName}' for {acc.Username} (Attempt {attempt}/{maxRetries})...");
                        }

                        var (currentSuccess, currentSkipped) = await action(acc);
                        finalSuccess = currentSuccess;
                        finalSkipped = currentSkipped;
                        lastException = null;

                        if (finalSuccess || finalSkipped)
                        {
                            break;
                        }
                        else if (attempt < maxRetries)
                        {
                            Console.WriteLine($"   [-] Action '{actionName}' failed for {acc.Username}. Waiting {retryDelayMs}ms before retry...");
                            await Task.Delay(retryDelayMs);
                        }
                        else
                        {
                            Console.WriteLine($"   [-] Action '{actionName}' failed for {acc.Username} after {maxRetries + 1} attempts (including initial).");
                        }
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        finalSuccess = false;
                        finalSkipped = false;

                        string errorType = ex switch
                        {
                            InvalidOperationException _ => "Config/State Error",
                            HttpRequestException hrex => $"Network Error ({(int?)hrex.StatusCode})",
                            JsonException _ => "JSON Error",
                            TaskCanceledException _ => "Timeout/Cancelled",
                            NullReferenceException _ => "Null Reference Error",
                            _ => $"Runtime Error ({ex.GetType().Name})"
                        };
                        ConsoleUI.WriteErrorLine($"{errorType} during '{actionName}' for {acc.Username}: {ConsoleUI.Truncate(ex.Message)}");

                        if (attempt < maxRetries)
                        {
                            if (!acc.IsValid)
                            {
                                ConsoleUI.WriteErrorLine($"Account {acc.Username} marked invalid after error. Stopping retries for this action.");
                                break;
                            }
                            ConsoleUI.WriteErrorLine($"Exception caught during '{actionName}'. Waiting {retryDelayMs}ms before retry (Attempt {attempt + 1}/{maxRetries})...");
                            await Task.Delay(retryDelayMs);
                        }
                        else
                        {
                            ConsoleUI.WriteErrorLine($"Exception caught on final attempt ({maxRetries + 1}) for '{actionName}'. Action failed.");
                            if (!acc.IsValid) { ConsoleUI.WriteErrorLine($"Account {acc.Username} was marked invalid during the process."); }
                        }
                    }
                }

                if (finalSkipped)
                {
                    skippedPreCheckCount++;
                }
                else if (finalSuccess)
                {
                    successCount++;
                }
                else
                {
                    failCount++;
                    if (lastException != null)
                    {
                        Console.WriteLine($"   [-] Final failure for {acc.Username} likely due to {lastException.GetType().Name}.");
                    }
                }

                if (i < accountsToProcess.Count - 1)
                {
                    await Task.Delay(baseDelayMs / 2);
                }
            }

            stopwatch.Stop();

            Console.WriteLine($"\n[<<] Action '{actionName}' Finished.");
            Console.WriteLine($"   Success (Action Performed): {successCount}");
            if (skippedPreCheckCount > 0) Console.WriteLine($"   Skipped (Pre-Check Met): {skippedPreCheckCount}");
            Console.WriteLine($"   Failed (After Retries/Errors): {failCount}");
            if (skippedInvalidCount > 0) Console.WriteLine($"   Skipped (Invalid/Token): {skippedInvalidCount}");
            Console.WriteLine($"   --------------------------------");
            Console.WriteLine($"   Total Processed/Attempted: {validCount}");
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
                   if (setResult) { ConsoleUI.WriteSuccessLine("Display name set successfully (blind attempt)."); }
                   else { ConsoleUI.WriteErrorLine("Display name set failed (blind attempt)."); }
                   return (setResult, false);
               }
               else if (string.Equals(currentName, targetName, StringComparison.OrdinalIgnoreCase))
               {
                   ConsoleUI.WriteInfoLine($"Skipping SetDisplayName: Already set to '{targetName}'.");
                   return (true, true);
               }
               else
               {
                   Console.WriteLine($"   Current name is '{currentName}'. Attempting update to '{targetName}'...");
                   bool setResult = await _userService.SetDisplayNameAsync(acc, targetName);
                   if (setResult) { ConsoleUI.WriteSuccessLine("Display name set successfully."); }
                   else { ConsoleUI.WriteErrorLine("Display name set failed."); }
                   return (setResult, false);
               }
           }, "SetDisplayName");

        public Task SetAvatarOnSelectedAsync() =>
            PerformActionOnSelectedAsync(async acc =>
            {
                long targetUserId = AppConfig.DefaultTargetUserIdForAvatarCopy;
                if (targetUserId <= 0)
                {
                    ConsoleUI.WriteErrorLine($"Skipping SetAvatar: No valid DefaultTargetUserIdForAvatarCopy ({targetUserId}) configured.");
                    return (false, true);
                }
                Console.WriteLine($"   Checking current avatar for {acc.Username} against target {targetUserId}...");

                AvatarDetails? targetAvatarDetails = await GetOrFetchTargetAvatarDetailsAsync(targetUserId);
                if (targetAvatarDetails == null)
                {
                    ConsoleUI.WriteErrorLine($"Critical Error: Could not get target avatar details for {targetUserId}. Cannot perform check or set avatar.");
                    return (false, false);
                }

                Console.WriteLine($"   Fetching current avatar details for {acc.Username}...");
                AvatarDetails? currentAvatarDetails = await _avatarService.FetchAvatarDetailsAsync(acc.UserId);

                if (currentAvatarDetails == null)
                {
                    Console.WriteLine($"   [-] Failed to fetch current avatar details for {acc.Username}. Proceeding with set attempt...");
                    bool setResult = await _avatarService.SetAvatarAsync(acc, targetUserId);
                    if (setResult) { ConsoleUI.WriteSuccessLine("Avatar set successfully (blind attempt)."); }
                    else { ConsoleUI.WriteErrorLine("Avatar set failed (blind attempt)."); }
                    return (setResult, false);
                }
                else
                {
                    bool match = _avatarService.CompareAvatarDetails(currentAvatarDetails, targetAvatarDetails);
                    if (match)
                    {
                        ConsoleUI.WriteInfoLine($"Skipping SetAvatar: Current avatar already matches target {targetUserId}.");
                        return (true, true);
                    }
                    else
                    {
                        Console.WriteLine($"   Current avatar differs from target. Attempting update...");
                        bool setResult = await _avatarService.SetAvatarAsync(acc, targetUserId);
                        if (setResult) { ConsoleUI.WriteSuccessLine("Avatar set successfully."); }
                        else { ConsoleUI.WriteErrorLine("Avatar set failed."); }
                        return (setResult, false);
                    }
                }
            }, "SetAvatar");

        public Task JoinGroupOnSelectedAsync() =>
            PerformActionOnSelectedAsync(async acc => {
                long targetGroupId = AppConfig.DefaultGroupId;
                if (targetGroupId <= 0)
                {
                    ConsoleUI.WriteErrorLine($"Skipping JoinGroup: No valid DefaultGroupId ({targetGroupId}) configured.");
                    return (false, true);
                }
                Console.WriteLine($"   Attempting to join group {targetGroupId} for {acc.Username}...");
                bool success = await _groupService.JoinGroupAsync(acc, targetGroupId);

                if (success) { ConsoleUI.WriteSuccessLine("Join group request sent/processed (Result: OK)."); }
                else { ConsoleUI.WriteErrorLine("Join group request failed (Result: Error)."); }
                return (success, false);
            }, "JoinGroup");

        public Task GetBadgesOnSelectedAsync(int badgeGoal = AppConfig.DefaultBadgeGoal) =>
             PerformActionOnSelectedAsync(async acc =>
             {
                 Console.WriteLine($"   Checking current badge count for {acc.Username} (Goal: >= {badgeGoal})...");
                 int currentBadgeCount = await _badgeService.GetBadgeCountAsync(acc, limit: 100);

                 if (currentBadgeCount == -1)
                 {
                     Console.WriteLine($"   [-] Failed to fetch current badge count. Proceeding with game launch attempt anyway...");
                 }
                 else if (currentBadgeCount >= badgeGoal)
                 {
                     ConsoleUI.WriteInfoLine($"Skipping GetBadges: Account already has {currentBadgeCount} (>= {badgeGoal}) recent badges.");
                     return (true, true);
                 }
                 else
                 {
                     Console.WriteLine($"   Current badge count is {currentBadgeCount} (< {badgeGoal}). Needs game launch.");
                 }

                 Console.WriteLine($"   Attempting to launch game {AppConfig.DefaultBadgeGameId}...");
                 await _gameLauncher.LaunchGameForBadgesAsync(acc, AppConfig.DefaultBadgeGameId, badgeGoal);

                 ConsoleUI.WriteSuccessLine($"Game launch sequence initiated/completed for {acc.Username}.");
                 return (true, false);

             }, $"GetBadges (Goal: {badgeGoal})", requireInteraction: true);

        public Task OpenInBrowserOnSelectedAsync() =>
           PerformActionOnSelectedAsync(async acc =>
           {
               Console.WriteLine($"   Initiating browser session for {acc.Username}...");
               var driver = WebDriverManager.StartBrowserWithCookie(acc, AppConfig.HomePageUrl, headless: false);

               if (driver == null)
               {
                   ConsoleUI.WriteErrorLine($"Failed to launch browser session.");
                   return (false, false);
               }
               else
               {
                   ConsoleUI.WriteSuccessLine($"Browser session initiated for {acc.Username}. Close the browser window manually when done.");
                   await Task.CompletedTask;
                   return (true, false);
               }
           }, "OpenInBrowser", requireInteraction: true, requireValidToken: false);

        public async Task HandleLimitedFriendRequestsAsync()
        {
            List<Account> selectedAccountsRaw = _accountManager.GetSelectedAccounts();
            int friendGoal = AppConfig.DefaultFriendGoal;

            ConsoleUI.WriteInfoLine($"Executing Action: Limited Friend Actions (Goal: >= {friendGoal} friends)");

            if (selectedAccountsRaw.Count < 2)
            {
                ConsoleUI.WriteErrorLine("Need at least 2 selected accounts for this action. Aborting.");
                return;
            }

            ConsoleUI.WriteInfoLine($"Phase 0: Pre-checking XCSRF and Friend Counts for {selectedAccountsRaw.Count} selected accounts...");

            List<Account> selectedValidAccounts = [];
            List<Account> accountsNeedingFriends = [];
            int preCheckFailures = 0; int preCheckRefreshed = 0;
            int alreadyMetGoal = 0; int friendCheckErrors = 0;
            Stopwatch preCheckStopwatch = Stopwatch.StartNew();

            for (int i = 0; i < selectedAccountsRaw.Count; i++)
            {
                Account acc = selectedAccountsRaw[i];
                Console.Write($"\n[{i + 1}/{selectedAccountsRaw.Count}] Checking {acc.Username}... ");

                if (!acc.IsValid || string.IsNullOrEmpty(acc.Cookie))
                {
                    Console.WriteLine("Skipped (Marked Invalid or No Cookie)."); preCheckFailures++; continue;
                }

                string oldToken = acc.XcsrfToken;
                bool tokenOk = await _authService.RefreshXCSRFTokenIfNeededAsync(acc);

                if (!tokenOk)
                {
                    Console.WriteLine($"   -> Token Refresh/Validation FAILED. Account likely marked invalid.");
                    preCheckFailures++;
                    continue;
                }

                if (acc.XcsrfToken != oldToken && !string.IsNullOrEmpty(oldToken)) { Console.Write($"   -> Token Refreshed. "); preCheckRefreshed++; }
                else if (string.IsNullOrEmpty(oldToken) && !string.IsNullOrEmpty(acc.XcsrfToken)) { Console.Write($"   -> Token Initialized. "); preCheckRefreshed++; }
                else { Console.Write($"   -> Token OK. "); }

                if (!acc.IsValid || string.IsNullOrEmpty(acc.XcsrfToken))
                {
                    Console.WriteLine($"   -> Account invalid after token check. Skipping further checks."); preCheckFailures++; continue;
                }

                selectedValidAccounts.Add(acc);

                Console.Write($"Checking friend count... ");
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

            preCheckStopwatch.Stop();
            Console.WriteLine($"\n[*] Pre-check Summary (Took {preCheckStopwatch.ElapsedMilliseconds}ms):");
            if (preCheckRefreshed > 0) Console.WriteLine($"   {preCheckRefreshed} tokens were refreshed/initialized.");
            if (preCheckFailures > 0) Console.WriteLine($"   {preCheckFailures} accounts skipped/failed (Invalid state or token issues).");
            if (friendCheckErrors > 0) Console.WriteLine($"   {friendCheckErrors} accounts had errors checking friend count (excluded from actions).");
            if (alreadyMetGoal > 0) Console.WriteLine($"   {alreadyMetGoal} valid accounts already met the friend goal ({friendGoal}) and were skipped.");
            Console.WriteLine($"   {accountsNeedingFriends.Count} valid accounts need friends and will proceed.");

            accountsNeedingFriends = [.. accountsNeedingFriends.OrderBy(a => a.UserId)];
            int count = accountsNeedingFriends.Count;

            if (count < 2)
            {
                ConsoleUI.WriteErrorLine($"Need at least 2 valid accounts *below the friend goal* for limited friend actions. Found {count}. Aborting friend cycle.");
                return;
            }

            if (count > 15 && !Environment.UserInteractive)
            {
                ConsoleUI.WriteErrorLine($"Warning: Running limited friends for a large number ({count}) of accounts non-interactively.");
                ConsoleUI.WriteErrorLine($"This may take a very long time and is prone to rate limits or captchas.");
            }
            else if (count > 15)
            {
                ConsoleUI.WriteInfoLine($"Note: Running limited friends for a larger number ({count}) of accounts. This might take some time.");
            }

            ConsoleUI.WriteInfoLine($"Phase 1: Sending Friend Requests among {count} accounts needing friends...");
            int attemptedSends = 0, successSends = 0, failedSends = 0;
            bool canProceedToPhase2 = false;
            var stopwatchSend = Stopwatch.StartNew();
            int baseSendDelay = AppConfig.CurrentFriendActionDelayMs;
            int sendRandomness = Math.Min(baseSendDelay / 2, 1500);

            for (int i = 0; i < count; i++)
            {
                Account receiver = accountsNeedingFriends[i];

                int sender1Index = (i + 1) % count;
                Account sender1 = accountsNeedingFriends[sender1Index];

                Account? sender2 = null;
                if (count > 2)
                {
                    int sender2Index = (i + 2) % count;
                    if (sender2Index != i && sender2Index != sender1Index)
                    {
                        sender2 = accountsNeedingFriends[sender2Index];
                    }
                }

                Console.WriteLine($"\n  Sending requests targeting: {receiver.Username} (ID: {receiver.UserId}, Index {i})");

                if (sender1.UserId != receiver.UserId)
                {
                    Console.WriteLine($"    Processing Send: {sender1.Username} (ID: {sender1.UserId}) -> {receiver.Username}"); attemptedSends++;
                    try
                    {
                        if (!sender1.IsValid || string.IsNullOrEmpty(sender1.XcsrfToken))
                        {
                            Console.WriteLine($"    -> Send Fail (Sender {sender1.Username} became invalid/lost token)."); failedSends++;
                        }
                        else
                        {
                            var (sendOk, isPending, failureReason) = await _friendService.SendFriendRequestAsync(sender1, receiver.UserId, receiver.Username);
                            await Task.Delay(Random.Shared.Next(Math.Max(AppConfig.MinAllowedDelayMs, baseSendDelay - sendRandomness), baseSendDelay + sendRandomness));

                            if (sendOk)
                            {
                                Console.WriteLine($"    -> Send OK"); successSends++; canProceedToPhase2 = true;
                            }
                            else if (isPending)
                            {
                                Console.WriteLine($"    -> Send Skipped/Pending (Reason: {failureReason})");
                                canProceedToPhase2 = true;
                            }
                            else
                            {
                                Console.WriteLine($"    -> Send Fail ({failureReason})"); failedSends++;
                            }
                        }
                    }
                    catch (Exception ex) { Console.WriteLine($"    -> Error Sending S1 ({sender1.Username}): {ex.GetType().Name}"); failedSends++; }
                }
                else { Console.WriteLine($"    -> Internal error: sender1 == receiver for index {i}. Skipping send."); }

                if (sender2 != null && sender2.UserId != receiver.UserId)
                {
                    Console.WriteLine($"    Processing Send: {sender2.Username} (ID: {sender2.UserId}) -> {receiver.Username}"); attemptedSends++;
                    try
                    {
                        if (!sender2.IsValid || string.IsNullOrEmpty(sender2.XcsrfToken))
                        {
                            Console.WriteLine($"    -> Send Fail (Sender {sender2.Username} became invalid/lost token)."); failedSends++;
                        }
                        else
                        {
                            var (sendOk, isPending, failureReason) = await _friendService.SendFriendRequestAsync(sender2, receiver.UserId, receiver.Username);
                            await Task.Delay(Random.Shared.Next(Math.Max(AppConfig.MinAllowedDelayMs, baseSendDelay - sendRandomness), baseSendDelay + sendRandomness));

                            if (sendOk)
                            {
                                Console.WriteLine($"    -> Send OK"); successSends++; canProceedToPhase2 = true;
                            }
                            else if (isPending)
                            {
                                Console.WriteLine($"    -> Send Skipped/Pending (Reason: {failureReason})");
                                canProceedToPhase2 = true;
                            }
                            else
                            {
                                Console.WriteLine($"    -> Send Fail ({failureReason})"); failedSends++;
                            }
                        }
                    }
                    catch (Exception ex) { Console.WriteLine($"    -> Error Sending S2 ({sender2.Username}): {ex.GetType().Name}"); failedSends++; }
                }
                else if (sender2 != null) { Console.WriteLine($"    -> Internal error: sender2 == receiver for index {i}. Skipping send."); }


                if (i < count - 1)
                {
                    await Task.Delay(Random.Shared.Next(AppConfig.CurrentApiDelayMs / 3, AppConfig.CurrentApiDelayMs / 2));
                }
            }

            stopwatchSend.Stop();
            Console.WriteLine($"[*] Phase 1 Complete: Sending Friend Requests.");
            Console.WriteLine($"   Attempted Sends (Pairs): {attemptedSends}, Successful API Sends: {successSends}, Failed Sends/Errors: {failedSends}");
            Console.WriteLine($"   (Skipped/Pending count not explicitly tracked but allows Phase 2 if detected)");
            Console.WriteLine($"   Time: {stopwatchSend.ElapsedMilliseconds}ms ({stopwatchSend.Elapsed.TotalSeconds:F1}s)");

            if (!canProceedToPhase2)
            {
                ConsoleUI.WriteErrorLine("\nNo friend requests were successfully sent OR detected as potentially pending in Phase 1. Skipping Phase 2.");
                return;
            }
            else if (successSends == 0 && canProceedToPhase2)
            {
                ConsoleUI.WriteInfoLine("\nNo *new* friend requests sent successfully, but proceeding to Phase 2 as existing/pending requests might be acceptable.");
            }

            ConsoleUI.WriteInfoLine($"\nWaiting {AppConfig.DefaultRequestTimeoutSec} seconds before starting Phase 2 (Accepting Requests)...");
            await Task.Delay(TimeSpan.FromSeconds(AppConfig.DefaultRequestTimeoutSec));

            ConsoleUI.WriteInfoLine($"\nPhase 2: Attempting to blindly accept expected friend requests...");
            int attemptedAccepts = 0;
            int successAccepts = 0;
            int failedAcceptsPhase2 = 0;
            int skippedAccepts = 0;
            var stopwatchAccept = Stopwatch.StartNew();
            var acceptedPairs = new HashSet<Tuple<long, long>>();
            int baseAcceptDelay = AppConfig.CurrentFriendActionDelayMs;
            int acceptRandomness = Math.Min(baseAcceptDelay / 2, 1500);

            for (int receiverIndex = 0; receiverIndex < count; receiverIndex++)
            {
                Account receiverAccount = accountsNeedingFriends[receiverIndex];
                Console.WriteLine($"\n  Processing receiver: {receiverAccount.Username} (ID: {receiverAccount.UserId}, Index {receiverIndex})");

                if (!receiverAccount.IsValid || string.IsNullOrEmpty(receiverAccount.XcsrfToken))
                {
                    Console.WriteLine($"    -> Skipping receiver (became invalid or lost token).");
                    skippedAccepts += (count > 2 ? 2 : 1);
                    continue;
                }

                int sender1Index = (receiverIndex + 1) % count;
                Account potentialSender1 = accountsNeedingFriends[sender1Index];

                Account? potentialSender2 = null;
                if (count > 2)
                {
                    int sender2Index = (receiverIndex + 2) % count;
                    if (sender2Index != receiverIndex && sender2Index != sender1Index)
                    {
                        potentialSender2 = accountsNeedingFriends[sender2Index];
                    }
                    else { Console.WriteLine($"    -> Internal state warning: Calculated sender2 index ({sender2Index}) conflicts with receiver ({receiverIndex}) or sender1 ({sender1Index}) for count={count}."); }
                }

                AcceptAttemptResult result1 = AcceptAttemptResult.Skipped_AlreadyDone;
                if (potentialSender1.UserId != receiverAccount.UserId)
                {
                    attemptedAccepts++;
                    result1 = await TryAcceptRequestAsync(receiverAccount, potentialSender1, acceptedPairs, baseAcceptDelay, acceptRandomness);
                    switch (result1)
                    {
                        case AcceptAttemptResult.Accepted: successAccepts++; break;
                        case AcceptAttemptResult.Failed: failedAcceptsPhase2++; break;
                        case AcceptAttemptResult.Skipped_AlreadyDone:
                        case AcceptAttemptResult.Skipped_InvalidSender:
                        case AcceptAttemptResult.Skipped_InvalidReceiver: skippedAccepts++; break;
                    }
                }
                else { Console.WriteLine($"    -> Internal error: potentialSender1 ({potentialSender1.Username}) is the same as receiver ({receiverAccount.Username}). Skipping accept attempt."); skippedAccepts++; }


                AcceptAttemptResult result2 = AcceptAttemptResult.Skipped_AlreadyDone;
                if (potentialSender2 != null && potentialSender2.UserId != receiverAccount.UserId)
                {
                    if (potentialSender2.UserId != potentialSender1.UserId)
                    {
                        attemptedAccepts++;
                        result2 = await TryAcceptRequestAsync(receiverAccount, potentialSender2, acceptedPairs, baseAcceptDelay, acceptRandomness);
                        switch (result2)
                        {
                            case AcceptAttemptResult.Accepted: successAccepts++; break;
                            case AcceptAttemptResult.Failed: failedAcceptsPhase2++; break;
                            case AcceptAttemptResult.Skipped_AlreadyDone:
                            case AcceptAttemptResult.Skipped_InvalidSender:
                            case AcceptAttemptResult.Skipped_InvalidReceiver: skippedAccepts++; break;
                        }
                        if (result1 != AcceptAttemptResult.Skipped_AlreadyDone || result2 != AcceptAttemptResult.Skipped_AlreadyDone)
                            await Task.Delay(Random.Shared.Next(AppConfig.CurrentApiDelayMs / 4, AppConfig.CurrentApiDelayMs / 3));
                    }
                    else { Console.WriteLine($"    -> Internal error: potentialSender2 ({potentialSender2.Username}) is the same as potentialSender1 ({potentialSender1.Username}). Skipping second accept attempt."); skippedAccepts++; }
                }
                else if (potentialSender2 != null) { Console.WriteLine($"    -> Internal error: potentialSender2 ({potentialSender2.Username}) is the same as receiver ({receiverAccount.Username}). Skipping second accept attempt."); skippedAccepts++; }


                if (receiverIndex < count - 1)
                {
                    await Task.Delay(Random.Shared.Next(AppConfig.CurrentApiDelayMs / 2, AppConfig.CurrentApiDelayMs));
                }
            }

            stopwatchAccept.Stop();
            Console.WriteLine($"\n[*] Phase 2 Complete: Attempted Blind Accepts.");
            Console.WriteLine($"   Attempted Accepts (Expected Pairs): {attemptedAccepts}, Successful API Accepts: {successAccepts} ({acceptedPairs.Count} unique pairs confirmed), Failed API Accepts/Errors: {failedAcceptsPhase2}, Skipped (Pre-checks/Invalid): {skippedAccepts}");
            Console.WriteLine($"   Time: {stopwatchAccept.ElapsedMilliseconds}ms ({stopwatchAccept.Elapsed.TotalSeconds:F1}s)");
            ConsoleUI.WriteInfoLine($"Limited friend action cycle finished for accounts needing friends.");
            ConsoleUI.WriteInfoLine($"Reminder: Check accounts with Verify action to confirm final friend counts.");
        }

        private async Task<AcceptAttemptResult> TryAcceptRequestAsync(
            Account receiver,
            Account sender,
            HashSet<Tuple<long, long>> acceptedPairs,
            int baseDelay,
            int randomness)
        {
            Console.WriteLine($"    Attempting Accept: {sender.Username} (ID: {sender.UserId}) -> {receiver.Username} (ID: {receiver.UserId})");

            var currentPair = Tuple.Create(sender.UserId, receiver.UserId);
            if (acceptedPairs.Contains(currentPair))
            {
                Console.WriteLine($"    -> Skipped (Already accepted in this run)");
                return AcceptAttemptResult.Skipped_AlreadyDone;
            }

            if (!sender.IsValid)
            {
                Console.WriteLine($"    -> Accept Skip (Sender {sender.Username} is marked invalid).");
                return AcceptAttemptResult.Skipped_InvalidSender;
            }

            if (!receiver.IsValid || string.IsNullOrEmpty(receiver.XcsrfToken))
            {
                Console.WriteLine($"    -> Accept Fail (Receiver account {receiver.Username} is invalid or missing token before accept).");
                return AcceptAttemptResult.Skipped_InvalidReceiver;
            }

            try
            {
                bool acceptOk = await _friendService.AcceptFriendRequestAsync(receiver, sender.UserId, sender.Username);
                await Task.Delay(Random.Shared.Next(Math.Max(AppConfig.MinAllowedDelayMs, baseDelay - randomness), baseDelay + randomness));

                if (acceptOk)
                {
                    Console.WriteLine($"    -> Accept OK");
                    acceptedPairs.Add(currentPair);
                    return AcceptAttemptResult.Accepted;
                }
                else
                {
                    Console.WriteLine($"    -> Accept Fail (API Error/Not Pending/Already Friends?)");
                    return AcceptAttemptResult.Failed;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    -> Error Accepting ({sender.Username} -> {receiver.Username}): {ex.GetType().Name} - {ConsoleUI.Truncate(ex.Message)}");
                return AcceptAttemptResult.Failed;
            }
        }

        public async Task<bool> VerifyAccountStatusOnSelectedAsync(
            int requiredFriends = AppConfig.DefaultFriendGoal,
            int requiredBadges = AppConfig.DefaultBadgeGoal,
            string expectedDisplayName = AppConfig.DefaultDisplayName,
            long expectedAvatarSourceId = AppConfig.DefaultTargetUserIdForAvatarCopy)
        {
            _accountManager.ClearVerificationResults();
            var selectedAccountsRaw = _accountManager.GetSelectedAccounts();
            var accountsToProcess = selectedAccountsRaw.Where(acc => acc != null && acc.IsValid).ToList();

            int totalSelected = selectedAccountsRaw.Count;
            int validCount = accountsToProcess.Count;
            int skippedInvalidCount = totalSelected - validCount;

            Console.WriteLine($"\n[>>] Executing Action: Verify Account Status for {validCount} valid account(s)...");
            Console.WriteLine($"   Requirements: Friends >= {requiredFriends}, Badges >= {requiredBadges} (Recent), Name == '{expectedDisplayName}', Avatar Source == {expectedAvatarSourceId}");
            if (skippedInvalidCount > 0) Console.WriteLine($"   ({skippedInvalidCount} selected accounts were invalid and will be skipped)");
            if (validCount == 0) { ConsoleUI.WriteErrorLine($"No valid accounts selected for verification."); return false; }

            AvatarDetails? targetAvatarDetails = null;
            if (expectedAvatarSourceId > 0)
            {
                targetAvatarDetails = await GetOrFetchTargetAvatarDetailsAsync(expectedAvatarSourceId);
                if (targetAvatarDetails == null)
                {
                    ConsoleUI.WriteWarningLine($"Could not fetch target avatar details for ID {expectedAvatarSourceId}. Avatar check will be skipped for all accounts.");
                }
            }
            else { ConsoleUI.WriteInfoLine("Skipping avatar check: No target avatar source ID specified (or <= 0)."); }

            int passedCount = 0, failedReqCount = 0, failedErrCount = 0;
            var stopwatch = Stopwatch.StartNew();
            int checkDelay = AppConfig.CurrentApiDelayMs / 4;

            for (int i = 0; i < accountsToProcess.Count; i++)
            {
                Account acc = accountsToProcess[i];
                Console.WriteLine($"\n[{i + 1}/{validCount}] Verifying: {acc.Username} (ID: {acc.UserId})");
                int friendCount = -1, badgeCount = -1;
                string? currentDisplayName = null;
                AvatarDetails? currentAvatarDetails = null;
                bool errorOccurred = false;
                VerificationStatus currentStatus = VerificationStatus.NotChecked;
                List<string> failureReasons = [];

                try
                {
                    Console.Write("   Checking Friends... ");
                    friendCount = await _friendService.GetFriendCountAsync(acc);
                    if (friendCount == -1) { Console.WriteLine("Failed."); errorOccurred = true; failureReasons.Add("Friend check API failed"); }
                    else { Console.WriteLine($"{friendCount} found."); if (friendCount < requiredFriends) failureReasons.Add($"Friends {friendCount} < {requiredFriends}"); }
                    if (!errorOccurred) await Task.Delay(checkDelay); else goto EndVerificationCheck;

                    Console.Write("   Checking Badges... ");
                    badgeCount = await _badgeService.GetBadgeCountAsync(acc, limit: 100);
                    if (badgeCount == -1) { Console.WriteLine("Failed."); errorOccurred = true; failureReasons.Add("Badge check API failed"); }
                    else { Console.WriteLine($"{badgeCount} found (Recent)."); if (badgeCount < requiredBadges) failureReasons.Add($"Badges {badgeCount} < {requiredBadges}"); }
                    if (!errorOccurred) await Task.Delay(checkDelay); else goto EndVerificationCheck;

                    Console.Write("   Checking Display Name... ");
                    (currentDisplayName, _) = await _userService.GetUsernamesAsync(acc);
                    if (currentDisplayName == null) { Console.WriteLine("Failed."); errorOccurred = true; failureReasons.Add("Display name check API failed"); }
                    else { Console.WriteLine($"'{currentDisplayName}' found."); if (!string.Equals(currentDisplayName, expectedDisplayName, StringComparison.OrdinalIgnoreCase)) failureReasons.Add($"Name '{currentDisplayName}' != '{expectedDisplayName}'"); }
                    if (!errorOccurred) await Task.Delay(checkDelay); else goto EndVerificationCheck;

                    if (targetAvatarDetails != null)
                    {
                        Console.Write("   Checking Avatar... ");
                        currentAvatarDetails = await _avatarService.FetchAvatarDetailsAsync(acc.UserId);
                        if (currentAvatarDetails == null) { Console.WriteLine("Failed."); errorOccurred = true; failureReasons.Add("Avatar fetch API failed"); }
                        else
                        {
                            Console.WriteLine($"Details retrieved.");
                            if (!_avatarService.CompareAvatarDetails(currentAvatarDetails, targetAvatarDetails)) failureReasons.Add("Avatar mismatch");
                        }
                    }
                    else { Console.WriteLine("   Skipping Avatar Check (Target details unavailable/not specified)."); }

                EndVerificationCheck:;

                    if (errorOccurred)
                    {
                        currentStatus = VerificationStatus.Error;
                        Console.WriteLine($"   -> Status: ERROR ({string.Join(", ", failureReasons)})");
                        failedErrCount++;
                    }
                    else
                    {
                        bool friendsOk = friendCount >= requiredFriends;
                        bool badgesOk = badgeCount >= requiredBadges;
                        bool displayNameOk = string.Equals(currentDisplayName, expectedDisplayName, StringComparison.OrdinalIgnoreCase);
                        bool avatarOk = targetAvatarDetails == null ||
                                        (currentAvatarDetails != null && _avatarService.CompareAvatarDetails(currentAvatarDetails, targetAvatarDetails));

                        string fStat = friendsOk ? "OK" : $"FAIL ({friendCount}/{requiredFriends})";
                        string bStat = badgesOk ? "OK" : $"FAIL ({badgeCount}/{requiredBadges})";
                        string nStat = displayNameOk ? "OK" : $"FAIL ('{currentDisplayName ?? "N/A"}' != '{expectedDisplayName}')";
                        string aStat;
                        if (targetAvatarDetails == null) aStat = "SKIP (No Target)";
                        else if (currentAvatarDetails == null) aStat = "ERR (Fetch Failed)";
                        else aStat = avatarOk ? "OK" : "FAIL (Mismatch)";

                        Console.WriteLine($"   -> Friends:       {fStat}");
                        Console.WriteLine($"   -> Badges:        {bStat}");
                        Console.WriteLine($"   -> Display Name:  {nStat}");
                        Console.WriteLine($"   -> Avatar Match:  {aStat} (Source: {expectedAvatarSourceId})");

                        if (friendsOk && badgesOk && displayNameOk && avatarOk)
                        {
                            currentStatus = VerificationStatus.Passed;
                            Console.WriteLine($"   -> Overall Status: PASS");
                            passedCount++;
                        }
                        else
                        {
                            currentStatus = VerificationStatus.Failed;
                            Console.WriteLine($"   -> Overall Status: FAIL ({string.Join("; ", failureReasons)})");
                            failedReqCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    ConsoleUI.WriteErrorLine($"Runtime Error during verification for {acc.Username}: {ex.GetType().Name} - {ex.Message}");
                    currentStatus = VerificationStatus.Error;
                    failureReasons.Add($"Runtime Error: {ex.GetType().Name}");
                    failedErrCount++;
                }
                finally
                {
                    _accountManager.SetVerificationStatus(acc.UserId, currentStatus, string.Join("; ", failureReasons));
                    if (i < accountsToProcess.Count - 1) await Task.Delay(AppConfig.MinAllowedDelayMs / 2);
                }
            }

            stopwatch.Stop();

            Console.WriteLine($"\n[<<] Action 'Verify Account Status' Finished.");
            Console.WriteLine($"   Passed: {passedCount}, Failed (Reqs): {failedReqCount}, Failed (Error): {failedErrCount}");
            if (skippedInvalidCount > 0) Console.WriteLine($"   Skipped (Invalid): {skippedInvalidCount}");
            Console.WriteLine($"   --------------------------------");
            Console.WriteLine($"   Total Verified: {validCount}");
            Console.WriteLine($"   Total Time: {stopwatch.ElapsedMilliseconds}ms ({stopwatch.Elapsed.TotalSeconds:F1}s)");

            return failedReqCount > 0 || failedErrCount > 0;
        }

        public async Task ExecuteAllAutoAsync()
        {
            ConsoleUI.WriteInfoLine($"Executing Action: Execute All Auto (Uses Defaults)");
            Console.WriteLine($"   Sequence: Set Name -> Set Avatar -> Limited Friends -> Get Badges (if interactive/configured)");
            Console.WriteLine($"   Defaults: Name='{AppConfig.DefaultDisplayName}', AvatarSrc={AppConfig.DefaultTargetUserIdForAvatarCopy}, FriendGoal={AppConfig.DefaultFriendGoal}, BadgeGoal={AppConfig.DefaultBadgeGoal}, BadgeGame={AppConfig.DefaultBadgeGameId}");
            Console.WriteLine($"   (Actions will be skipped per-account if prerequisites are already met)");
            Console.WriteLine($"   (Failed actions may retry based on config: Retries={AppConfig.CurrentMaxApiRetries}, Delay={AppConfig.CurrentApiRetryDelayMs}ms)");

            Console.WriteLine("\n--- Starting: Set Display Name ---");
            await SetDisplayNameOnSelectedAsync();

            Console.WriteLine("\n--- Starting: Set Avatar ---");
            await SetAvatarOnSelectedAsync();

            Console.WriteLine("\n--- Starting: Limited Friend Actions ---");
            await HandleLimitedFriendRequestsAsync();

            Console.WriteLine("\n--- Starting: Get Badges ---");

            if (!Environment.UserInteractive)
            {
                ConsoleUI.WriteErrorLine("Skipping 'Get Badges' step in non-interactive environment for 'Execute All Auto'.");
            }
            else
            {
                await GetBadgesOnSelectedAsync(AppConfig.DefaultBadgeGoal);
            }

            ConsoleUI.WriteInfoLine("\nMulti-Action Sequence 'Execute All Auto' Complete.");
            ConsoleUI.WriteInfoLine("Suggestion: Run 'Verify Account Status' (Action 7) to check final state.");
        }
    }
}
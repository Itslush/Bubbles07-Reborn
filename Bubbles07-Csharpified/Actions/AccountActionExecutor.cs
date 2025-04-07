using System.Diagnostics;
using Newtonsoft.Json;
using Continuance;
using Continuance.Core;
using Continuance.Models;
using Continuance.Roblox.Automation;
using Continuance.Roblox.Services;
using Continuance.UI;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Runtime.Intrinsics.X86;
using System.Text.RegularExpressions;


namespace Actions
{
    internal enum AcceptAttemptResult
    {
        Accepted,
        Failed,
        Skipped_AlreadyDone,
        Skipped_InvalidSender,
        Skipped_InvalidReceiver,
        Skipped_SendNotSuccessful
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

            Console.WriteLine($"{ConsoleUI.T_Vertical}   [*] Fetching target avatar details from User ID {sourceUserId} for comparison/cache...");
            var fetchedDetails = await _avatarService.FetchAvatarDetailsAsync(sourceUserId);

            if (fetchedDetails != null)
            {
                lock (_avatarCacheLock)
                {
                    _targetAvatarDetailsCache = fetchedDetails;
                    _targetAvatarCacheSourceId = sourceUserId;
                    Console.WriteLine($"{ConsoleUI.T_Vertical}   [+] Target avatar details cached successfully for {sourceUserId}.");
                }
                return fetchedDetails;
            }
            else
            {
                Console.WriteLine($"{ConsoleUI.T_Vertical}   [!] Failed to fetch target avatar details for comparison ({sourceUserId}). Cannot perform pre-check.");
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
                Console.WriteLine($"{ConsoleUI.T_Vertical}   [!] Skipping interactive action '{actionName}' in non-interactive environment.");
                return;
            }

            var selectedAccountsRaw = _accountManager.GetSelectedAccounts();
            var accountsToProcess = selectedAccountsRaw
                .Where(acc => acc != null && acc.IsValid && (!requireValidToken || !string.IsNullOrEmpty(acc.XcsrfToken)))
                .ToList();

            int totalSelected = selectedAccountsRaw.Count;
            int validCount = accountsToProcess.Count;
            int skippedInvalidCount = totalSelected - validCount;

            if (validCount >= AppConfig.CurrentActionConfirmationThreshold)
            {
                Console.WriteLine($"{ConsoleUI.T_Vertical}   [?] You are about to run action '{actionName}' on {validCount} accounts.");
                Console.Write($"{ConsoleUI.T_Vertical}   Proceed? (y/n): ");
                if (Console.ReadLine()?.Trim().ToLower() != "y")
                {
                    Console.WriteLine($"{ConsoleUI.T_Vertical}   [!] Action cancelled by user.");
                    return;
                }
                Console.WriteLine($"{ConsoleUI.T_Vertical}   [*] Confirmation received. Proceeding...");
            }

            Console.WriteLine($"\n[>>] Executing Action: {actionName} for {validCount} valid account(s)...");
            if (skippedInvalidCount > 0)
            {
                string reason = requireValidToken ? "invalid / lacked XCSRF" : "marked invalid";
                Console.WriteLine($"   ({skippedInvalidCount} selected accounts were {reason} and will be skipped)");
            }
            if (validCount == 0 && totalSelected > 0)
            {
                Console.WriteLine($"{ConsoleUI.T_Vertical}   [!] All selected accounts were skipped due to being invalid or lacking required tokens.");
                return;
            }
            else if (validCount == 0 && totalSelected == 0)
            {
                Console.WriteLine($"{ConsoleUI.T_Vertical}   [!] No accounts selected for this action.");
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

                Console.WriteLine($"\n[{i + 1}/{validCount}] Starting action '{actionName}' for: {acc.Username} (ID: {acc.UserId})...");

                bool finalSuccess = false;
                bool finalSkipped = false;
                Exception? lastException = null;

                for (int attempt = 0; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        if (attempt > 0)
                        {
                            Console.WriteLine($"{ConsoleUI.T_Vertical}      [*] Retrying action... (Attempt {attempt + 1}/{maxRetries + 1})");
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
                            Console.WriteLine($"{ConsoleUI.T_Vertical}      [*] Action failed on attempt {attempt + 1}. Retrying after {retryDelayMs}ms...");
                            await Task.Delay(retryDelayMs);
                        }
                        else
                        {
                            Console.WriteLine($"{ConsoleUI.T_Vertical}      [!] Action failed after {maxRetries + 1} attempts.");
                        }
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        finalSuccess = false;
                        finalSkipped = false;
                        Console.WriteLine($"{ConsoleUI.T_Vertical}      [!] Exception on attempt {attempt + 1}: {ex.GetType().Name}");

                        if (attempt < maxRetries)
                        {
                            if (!acc.IsValid)
                            {
                                Console.WriteLine($"{ConsoleUI.T_Vertical}         [!] Account marked invalid during operation. Stopping retries.");
                                break;
                            }
                            Console.WriteLine($"{ConsoleUI.T_Vertical}      [*] Retrying after exception ({retryDelayMs}ms)...");
                            await Task.Delay(retryDelayMs);
                        }
                        else
                        {
                            Console.WriteLine($"{ConsoleUI.T_Vertical}      [!] Action failed due to exception after {maxRetries + 1} attempts.");
                            if (!acc.IsValid) { Console.WriteLine($"{ConsoleUI.T_Vertical}         [!] Account was marked invalid during the process."); }
                        }
                    }
                }

                string resultIndicator;
                if (finalSkipped) { resultIndicator = "Skipped (Pre-Check Met / Not Applicable)"; skippedPreCheckCount++; }
                else if (finalSuccess) { resultIndicator = "Success"; successCount++; }
                else { resultIndicator = "Failed"; failCount++; }

                string finalResultLine = $"[{i + 1}/{validCount}] Result for {acc.Username}: {resultIndicator}";
                if (finalSkipped) { Console.WriteLine($"{ConsoleUI.T_Vertical}   [*] {finalResultLine}"); }
                else if (finalSuccess) { Console.WriteLine($"{ConsoleUI.T_Vertical}   [+] {finalResultLine}"); }
                else { Console.WriteLine($"{ConsoleUI.T_Vertical}   [!] {finalResultLine}"); }


                if (!finalSuccess && !finalSkipped && lastException != null)
                {
                    string errorType = lastException switch
                    {
                        InvalidOperationException _ => "Config/State Error",
                        HttpRequestException hrex => $"Network Error ({(int?)hrex.StatusCode})",
                        JsonException _ => "JSON Error",
                        TaskCanceledException _ => "Timeout/Cancelled",
                        NullReferenceException _ => "Null Reference Error",
                        _ => $"Runtime Error ({lastException.GetType().Name})"
                    };
                    Console.WriteLine($"{ConsoleUI.T_Vertical}      -> Failure Details: {errorType} - {ConsoleUI.Truncate(lastException.Message)}");
                }
                else if (!finalSuccess && !finalSkipped && lastException == null)
                {
                    Console.WriteLine($"{ConsoleUI.T_Vertical}      -> Failure Details: Action reported failure after {maxRetries + 1} attempts (API likely returned error status).");
                }

                if (i < accountsToProcess.Count - 1)
                {
                    await Task.Delay(baseDelayMs / 2);
                }
            }

            stopwatch.Stop();

            Console.WriteLine($"\n[<<] Action '{actionName}' Finished.");
            Console.WriteLine($"   Success (Action Performed): {successCount}");
            if (skippedPreCheckCount > 0) Console.WriteLine($"   Skipped (Pre-Check Met / NA): {skippedPreCheckCount}");
            Console.WriteLine($"   Failed (After Retries/Errors): {failCount}");
            if (skippedInvalidCount > 0) Console.WriteLine($"   Skipped (Invalid/Token): {skippedInvalidCount}");
            Console.WriteLine($"   --------------------------------");
            Console.WriteLine($"   Total Processed/Attempted: {validCount}");
            Console.WriteLine($"   Total Time: {stopwatch.ElapsedMilliseconds}ms ({stopwatch.Elapsed.TotalSeconds:F1}s)");
        }

        public Task SetDisplayNameOnSelectedAsync(string targetName) =>
           PerformActionOnSelectedAsync(async acc =>
           {
               Console.WriteLine($"   Checking current display name for {acc.Username}...");

               string? currentName = await _userService.GetCurrentDisplayNameAsync(acc);

               if (currentName == null)
               {
                   Console.WriteLine($"   [-] Failed to fetch current display name. Proceeding with set attempt...");
                   bool setResult = await _userService.SetDisplayNameAsync(acc, targetName);
                   if (setResult) { Console.WriteLine($"{ConsoleUI.T_Vertical}      [+] Display name set successfully (blind attempt)."); }
                   else { Console.WriteLine($"{ConsoleUI.T_Vertical}      [!] Display name set failed (blind attempt)."); }
                   return (setResult, false);
               }
               else if (string.Equals(currentName, targetName, StringComparison.OrdinalIgnoreCase))
               {
                   Console.WriteLine($"{ConsoleUI.T_Vertical}      [*] Skipping SetDisplayName: Already set to '{targetName}'.");
                   return (true, true);
               }
               else
               {
                   Console.WriteLine($"   Current name is '{currentName}'. Attempting update to '{targetName}'...");
                   bool setResult = await _userService.SetDisplayNameAsync(acc, targetName);
                   if (setResult) { Console.WriteLine($"{ConsoleUI.T_Vertical}      [+] Display name set successfully."); }
                   else { Console.WriteLine($"{ConsoleUI.T_Vertical}      [!] Display name set failed."); }
                   return (setResult, false);
               }
           }, $"SetDisplayName to '{targetName}'");

        public Task SetAvatarOnSelectedAsync(long targetUserId) =>
            PerformActionOnSelectedAsync(async acc =>
            {
                if (targetUserId <= 0)
                {
                    Console.WriteLine($"{ConsoleUI.T_Vertical}      [!] Skipping SetAvatar: No valid targetUserId ({targetUserId}) provided.");
                    return (false, true);
                }
                Console.WriteLine($"   Checking current avatar for {acc.Username} against target {targetUserId}...");

                AvatarDetails? targetAvatarDetails = await GetOrFetchTargetAvatarDetailsAsync(targetUserId);
                if (targetAvatarDetails == null)
                {
                    Console.WriteLine($"{ConsoleUI.T_Vertical}      [!] Critical Error: Could not get target avatar details for {targetUserId}. Cannot perform check or set avatar.");
                    return (false, false);
                }

                Console.WriteLine($"   Fetching current avatar details for {acc.Username}...");
                AvatarDetails? currentAvatarDetails = await _avatarService.FetchAvatarDetailsAsync(acc.UserId);

                if (currentAvatarDetails == null)
                {
                    Console.WriteLine($"   [-] Failed to fetch current avatar details for {acc.Username}. Proceeding with set attempt...");
                    bool setResult = await _avatarService.SetAvatarAsync(acc, targetUserId);
                    if (setResult) { Console.WriteLine($"{ConsoleUI.T_Vertical}      [+] Avatar set successfully (blind attempt)."); }
                    else { Console.WriteLine($"{ConsoleUI.T_Vertical}      [!] Avatar set failed (blind attempt)."); }
                    return (setResult, false);
                }
                else
                {
                    bool match = _avatarService.CompareAvatarDetails(currentAvatarDetails, targetAvatarDetails);
                    if (match)
                    {
                        Console.WriteLine($"{ConsoleUI.T_Vertical}      [*] Skipping SetAvatar: Current avatar already matches target {targetUserId}.");
                        return (true, true);
                    }
                    else
                    {
                        Console.WriteLine($"   Current avatar differs from target. Attempting update...");
                        bool setResult = await _avatarService.SetAvatarAsync(acc, targetUserId);
                        if (setResult) { Console.WriteLine($"{ConsoleUI.T_Vertical}      [+] Avatar set successfully."); }
                        else { Console.WriteLine($"{ConsoleUI.T_Vertical}      [!] Avatar set failed."); }
                        return (setResult, false);
                    }
                }
            }, $"SetAvatar from UserID {targetUserId}");

        public Task JoinGroupOnSelectedAsync(long targetGroupId) =>
            PerformActionOnSelectedAsync(async acc => {
                if (targetGroupId <= 0)
                {
                    Console.WriteLine($"{ConsoleUI.T_Vertical}      [!] Skipping JoinGroup: No valid targetGroupId ({targetGroupId}) provided.");
                    return (false, true);
                }
                Console.WriteLine($"   Attempting to join group {targetGroupId} for {acc.Username}...");
                bool success = await _groupService.JoinGroupAsync(acc, targetGroupId);

                if (success) { Console.WriteLine($"{ConsoleUI.T_Vertical}      [+] Join group request sent/processed (Result: OK)."); }
                else { Console.WriteLine($"{ConsoleUI.T_Vertical}      [!] Join group request failed (Result: Error)."); }
                return (success, false);
            }, $"JoinGroup ID {targetGroupId}");

        public Task GetBadgesOnSelectedAsync(int badgeGoal, string gameId) =>
             PerformActionOnSelectedAsync(async acc =>
             {
                 if (badgeGoal <= 0)
                 {
                     Console.WriteLine($"{ConsoleUI.T_Vertical}      [*] Skipping GetBadges: Badge goal is zero or negative.");
                     return (true, true);
                 }
                 if (string.IsNullOrWhiteSpace(gameId))
                 {
                     Console.WriteLine($"{ConsoleUI.T_Vertical}      [!] Skipping GetBadges: No valid Game ID provided.");
                     return (false, true);
                 }

                 Console.WriteLine($"   Checking current badge count for {acc.Username} (Goal: >= {badgeGoal})...");

                 int apiLimitForCheck;
                 if (badgeGoal <= 10) apiLimitForCheck = 10;
                 else if (badgeGoal <= 25) apiLimitForCheck = 25;
                 else if (badgeGoal <= 50) apiLimitForCheck = 50;
                 else apiLimitForCheck = 100;

                 int currentBadgeCount = await _badgeService.GetBadgeCountAsync(acc, limit: apiLimitForCheck);

                 if (currentBadgeCount == -1)
                 {
                     Console.WriteLine($"   [-] Failed to fetch current badge count. Will proceed with game launch attempt anyway...");
                 }
                 else if (currentBadgeCount >= badgeGoal)
                 {
                     Console.WriteLine($"{ConsoleUI.T_Vertical}      [*] Skipping GetBadges: Account already has {currentBadgeCount} (>= {badgeGoal}) recent badges (checked up to {apiLimitForCheck}).");
                     return (true, true);
                 }
                 else
                 {
                     Console.WriteLine($"   Current badge count is {currentBadgeCount} (< {badgeGoal}). Needs game launch (checked up to {apiLimitForCheck}).");
                 }

                 Console.WriteLine($"   Attempting to launch game {gameId}...");
                 bool launchInitiatedSuccessfully = await _gameLauncher.LaunchGameForBadgesAsync(acc, gameId, badgeGoal);

                 if (launchInitiatedSuccessfully)
                 {
                     Console.WriteLine($"{ConsoleUI.T_Vertical}      [+] Game launch sequence reported as initiated successfully for {acc.Username}.");
                     return (true, false);
                 }
                 else
                 {
                     Console.WriteLine($"{ConsoleUI.T_Vertical}      [!] Game launch sequence failed to initiate for {acc.Username} (e.g., auth ticket failed).");
                     return (false, false);
                 }

             }, $"GetBadges (Goal: {badgeGoal}, Game: {gameId})", requireInteraction: true);

        public Task OpenInBrowserOnSelectedAsync() =>
           PerformActionOnSelectedAsync(async acc =>
           {
               Console.WriteLine($"   Initiating browser session for {acc.Username}...");
               var driver = WebDriverManager.StartBrowserWithCookie(acc, AppConfig.HomePageUrl, headless: false);

               if (driver == null)
               {
                   Console.WriteLine($"{ConsoleUI.T_Vertical}      [!] Failed to launch browser session.");
                   return (false, false);
               }
               else
               {
                   Console.WriteLine($"{ConsoleUI.T_Vertical}      [+] Browser session initiated for {acc.Username}. Close the browser window manually when done.");
                   await Task.CompletedTask;
                   return (true, false);
               }
           }, "OpenInBrowser", requireInteraction: true, requireValidToken: false);

        public async Task HandleLimitedFriendRequestsAsync(int friendGoal)
        {
            List<Account> selectedAccountsRaw = _accountManager.GetSelectedAccounts();
            var overallStopwatch = Stopwatch.StartNew();
            int totalAttemptedSends = 0, totalSuccessSends = 0, totalFailedSends = 0, totalPendingSends = 0;
            int totalAttemptedAccepts = 0, totalSuccessAccepts = 0, totalFailedAccepts = 0, totalSkippedAccepts = 0;


            Console.WriteLine($"{ConsoleUI.T_Vertical}   [*] Executing Action: Limited Friend Actions (Goal: >= {friendGoal} friends)");

            if (selectedAccountsRaw.Count < 2)
            {
                Console.WriteLine($"{ConsoleUI.T_Vertical}   [!] Need at least 2 selected accounts for this action. Aborting.");
                return;
            }

            Console.WriteLine($"{ConsoleUI.T_Vertical}   [*] Phase 0: Pre-checking XCSRF and Friend Counts for {selectedAccountsRaw.Count} selected accounts...");

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
            int totalNeedingFriends = accountsNeedingFriends.Count;

            if (totalNeedingFriends < 2)
            {
                Console.WriteLine($"{ConsoleUI.T_Vertical}   [!] Need at least 2 valid accounts *below the friend goal* for limited friend actions. Found {totalNeedingFriends}. Aborting friend cycle.");
                return;
            }

            if (totalNeedingFriends >= AppConfig.CurrentActionConfirmationThreshold)
            {
                Console.WriteLine($"{ConsoleUI.T_Vertical}   [?] You are about to run friend actions between {totalNeedingFriends} accounts.");
                Console.Write($"{ConsoleUI.T_Vertical}   Proceed? (y/n): ");
                if (Console.ReadLine()?.Trim().ToLower() != "y")
                {
                    Console.WriteLine($"{ConsoleUI.T_Vertical}   [!] Friend action cancelled by user.");
                    return;
                }
                Console.WriteLine($"{ConsoleUI.T_Vertical}   [*] Confirmation received. Proceeding with friend actions...");
            }

            int batchSize = totalNeedingFriends;
            bool useBatching = false;
            const int batchPromptThreshold = 20;
            const int defaultBatchSize = 10;
            const int minBatchSize = 5;
            int batchDelaySeconds = 60;

            if (totalNeedingFriends >= batchPromptThreshold)
            {
                Console.Write($"\n{ConsoleUI.T_Vertical}   Process {totalNeedingFriends} accounts in batches to reduce rate limit risk? (y/n, default y): ");
                string batchChoice = Console.ReadLine()?.Trim().ToLower() ?? "y";
                if (batchChoice == "y" || string.IsNullOrEmpty(batchChoice))
                {
                    useBatching = true;
                    Console.Write($"{ConsoleUI.T_Vertical}   Enter batch size (e.g., 10, min {minBatchSize}) or blank for default ({defaultBatchSize}): ");
                    string sizeInput = Console.ReadLine()?.Trim() ?? "";
                    if (!int.TryParse(sizeInput, out batchSize) || batchSize < minBatchSize)
                    {
                        batchSize = defaultBatchSize;
                        Console.WriteLine($"{ConsoleUI.T_Vertical}      [*] Using default batch size: {batchSize}");
                    }
                    else
                    {
                        Console.WriteLine($"{ConsoleUI.T_Vertical}      [*] Using batch size: {batchSize}");
                    }
                    Console.Write($"{ConsoleUI.T_Vertical}   Enter delay between batches in seconds (e.g., 60) or blank for default ({batchDelaySeconds}s): ");
                    string delayInput = Console.ReadLine()?.Trim() ?? "";
                    if (!int.TryParse(delayInput, out batchDelaySeconds) || batchDelaySeconds < 10)
                    {
                        batchDelaySeconds = 60;
                        Console.WriteLine($"{ConsoleUI.T_Vertical}      [*] Using default batch delay: {batchDelaySeconds} seconds");
                    }
                    else
                    {
                        Console.WriteLine($"{ConsoleUI.T_Vertical}      [*] Using batch delay: {batchDelaySeconds} seconds");
                    }
                }
                else
                {
                    Console.WriteLine($"{ConsoleUI.T_Vertical}      [*] Processing all accounts at once.");
                }
            }

            int numBatches = (int)Math.Ceiling((double)totalNeedingFriends / batchSize);

            for (int batchNum = 0; batchNum < numBatches; batchNum++)
            {
                int skipCount = batchNum * batchSize;
                var currentBatchAccounts = accountsNeedingFriends.Skip(skipCount).Take(batchSize).ToList();
                int batchAccountCount = currentBatchAccounts.Count;

                if (batchAccountCount < 2 && totalNeedingFriends >= 2)
                {
                    Console.WriteLine($"{ConsoleUI.T_Vertical}   [?] Skipping Batch {batchNum + 1}/{numBatches}: Contains fewer than 2 accounts ({batchAccountCount}).");
                    continue;
                }
                else if (batchAccountCount < 2)
                {
                    Console.WriteLine($"{ConsoleUI.T_Vertical}   [?] Skipping Batch {batchNum + 1}/{numBatches}: Not enough accounts ({batchAccountCount}).");
                    continue;
                }

                Console.WriteLine($"{ConsoleUI.T_Vertical}   [*] --- Processing Batch {batchNum + 1}/{numBatches} ({batchAccountCount} accounts) ---");

                Console.WriteLine($"{ConsoleUI.T_Vertical}      [*] Phase 1 (Batch {batchNum + 1}): Sending Friend Requests...");
                int batchAttemptedSends = 0, batchSuccessSends = 0, batchFailedSends = 0, batchPendingSends = 0;
                bool batchCanProceedToPhase2 = false;
                var batchSuccessfulSendPairs = new HashSet<Tuple<long, long>>();
                var batchStopwatchSend = Stopwatch.StartNew();
                int baseSendDelay = AppConfig.CurrentFriendActionDelayMs;
                int sendRandomness = Math.Min(baseSendDelay / 2, 1500);


                for (int i = 0; i < batchAccountCount; i++)
                {
                    Account receiver = currentBatchAccounts[i];

                    int sender1Index = (i + 1) % batchAccountCount;
                    Account sender1 = currentBatchAccounts[sender1Index];

                    Account? sender2 = null;
                    if (batchAccountCount > 2)
                    {
                        int sender2Index = (i + 2) % batchAccountCount;
                        if (sender2Index != i && sender2Index != sender1Index)
                        {
                            sender2 = currentBatchAccounts[sender2Index];
                        }
                    }

                    Console.WriteLine($"\n{ConsoleUI.T_Vertical}         [*] Batch {batchNum + 1}, Send Target: {receiver.Username} (ID: {receiver.UserId}, Batch Index {i})");

                    if (sender1.UserId != receiver.UserId)
                    {
                        Console.WriteLine($"         Processing Send: {sender1.Username} (ID: {sender1.UserId}) -> {receiver.Username}"); batchAttemptedSends++;
                        try
                        {
                            if (!sender1.IsValid || string.IsNullOrEmpty(sender1.XcsrfToken))
                            {
                                Console.WriteLine($"{ConsoleUI.T_Vertical}            [?] -> Send Fail (Sender {sender1.Username} became invalid/lost token)."); batchFailedSends++;
                            }
                            else
                            {
                                var (sendOk, isPending, failureReason) = await _friendService.SendFriendRequestAsync(sender1, receiver.UserId, receiver.Username);
                                await Task.Delay(Random.Shared.Next(Math.Max(AppConfig.MinAllowedDelayMs, baseSendDelay - sendRandomness), baseSendDelay + sendRandomness));

                                if (sendOk)
                                {
                                    Console.WriteLine($"{ConsoleUI.T_Vertical}            [+] -> Send OK");
                                    batchSuccessSends++;
                                    batchSuccessfulSendPairs.Add(Tuple.Create(sender1.UserId, receiver.UserId));
                                    batchCanProceedToPhase2 = true;
                                }
                                else if (isPending)
                                {
                                    Console.WriteLine($"{ConsoleUI.T_Vertical}            [?] -> Send Skipped/Pending (Reason: {failureReason})");
                                    batchPendingSends++;
                                    batchSuccessfulSendPairs.Add(Tuple.Create(sender1.UserId, receiver.UserId));
                                    batchCanProceedToPhase2 = true;
                                }
                                else
                                {
                                    Console.WriteLine($"{ConsoleUI.T_Vertical}            [!] -> Send Fail ({failureReason})"); batchFailedSends++;
                                }
                            }
                        }
                        catch (Exception ex) { Console.WriteLine($"         -> Error Sending S1 ({sender1.Username}): {ex.GetType().Name}"); batchFailedSends++; }
                    }
                    else { Console.WriteLine($"         -> Internal error: sender1 == receiver for batch index {i}. Skipping send."); }

                    if (sender2 != null && sender2.UserId != receiver.UserId)
                    {
                        Console.WriteLine($"         Processing Send: {sender2.Username} (ID: {sender2.UserId}) -> {receiver.Username}"); batchAttemptedSends++;
                        try
                        {
                            if (!sender2.IsValid || string.IsNullOrEmpty(sender2.XcsrfToken))
                            {
                                Console.WriteLine($"         -> Send Fail (Sender {sender2.Username} became invalid/lost token)."); batchFailedSends++;
                            }
                            else
                            {
                                var (sendOk, isPending, failureReason) = await _friendService.SendFriendRequestAsync(sender2, receiver.UserId, receiver.Username);
                                await Task.Delay(Random.Shared.Next(Math.Max(AppConfig.MinAllowedDelayMs, baseSendDelay - sendRandomness), baseSendDelay + sendRandomness));

                                if (sendOk)
                                {
                                    Console.WriteLine($"         -> Send OK");
                                    batchSuccessSends++;
                                    batchSuccessfulSendPairs.Add(Tuple.Create(sender2.UserId, receiver.UserId));
                                    batchCanProceedToPhase2 = true;
                                }
                                else if (isPending)
                                {
                                    Console.WriteLine($"         -> Send Skipped/Pending (Reason: {failureReason})");
                                    batchPendingSends++;
                                    batchSuccessfulSendPairs.Add(Tuple.Create(sender2.UserId, receiver.UserId));
                                    batchCanProceedToPhase2 = true;
                                }
                                else
                                {
                                    Console.WriteLine($"         -> Send Fail ({failureReason})"); batchFailedSends++;
                                }
                            }
                        }
                        catch (Exception ex) { Console.WriteLine($"         -> Error Sending S2 ({sender2.Username}): {ex.GetType().Name}"); batchFailedSends++; }
                    }
                    else if (sender2 != null) { Console.WriteLine($"         -> Internal error: sender2 == receiver for batch index {i}. Skipping send."); }


                    if (i < batchAccountCount - 1)
                    {
                        await Task.Delay(Random.Shared.Next(AppConfig.CurrentApiDelayMs / 3, AppConfig.CurrentApiDelayMs / 2));
                    }
                }

                batchStopwatchSend.Stop();
                Console.WriteLine($"{ConsoleUI.T_Vertical}      [*] Batch {batchNum + 1} Phase 1 Complete.");
                Console.WriteLine($"         Batch Sends: Attempted={batchAttemptedSends}, Success={batchSuccessSends}, Pending/Skip={batchPendingSends}, Failed={batchFailedSends}");
                Console.WriteLine($"         Batch Time: {batchStopwatchSend.ElapsedMilliseconds}ms");

                totalAttemptedSends += batchAttemptedSends;
                totalSuccessSends += batchSuccessSends;
                totalPendingSends += batchPendingSends;
                totalFailedSends += batchFailedSends;


                if (!batchCanProceedToPhase2 || batchSuccessfulSendPairs.Count == 0)
                {
                    Console.WriteLine($"{ConsoleUI.T_Vertical}      [!] Batch {batchNum + 1}: No sends were successful or pending. Skipping Phase 2 for this batch.");
                }
                else
                {
                    const int phase2DelaySeconds = 75; // Increased fixed delay
                    Console.WriteLine($"{ConsoleUI.T_Vertical}      [*] Batch {batchNum + 1}: Phase 1 Sends complete. Waiting {phase2DelaySeconds} seconds before starting Phase 2 acceptances to allow server processing...");
                    await Task.Delay(TimeSpan.FromSeconds(phase2DelaySeconds));

                    Console.WriteLine($"{ConsoleUI.T_Vertical}      [*] Phase 2 (Batch {batchNum + 1}): Attempting to accept requests...");
                    int batchAttemptedAccepts = 0;
                    int batchSuccessAccepts = 0;
                    int batchFailedAccepts = 0;
                    int batchSkippedAccepts = 0;
                    var batchStopwatchAccept = Stopwatch.StartNew();
                    var batchAcceptedPairs = new HashSet<Tuple<long, long>>();
                    int baseAcceptDelay = AppConfig.CurrentFriendActionDelayMs;
                    int acceptRandomness = Math.Min(baseAcceptDelay / 2, 1500);


                    for (int receiverIndex = 0; receiverIndex < batchAccountCount; receiverIndex++)
                    {
                        Account receiverAccount = currentBatchAccounts[receiverIndex];
                        Console.WriteLine($"\n{ConsoleUI.T_Vertical}         [*] Batch {batchNum + 1}, Accept Receiver: {receiverAccount.Username} (ID: {receiverAccount.UserId}, Batch Index {receiverIndex})");

                        if (!receiverAccount.IsValid || string.IsNullOrEmpty(receiverAccount.XcsrfToken))
                        {
                            Console.WriteLine($"            -> Skipping receiver (became invalid or lost token).");
                            batchSkippedAccepts += (batchAccountCount > 2 ? 2 : 1);
                            continue;
                        }

                        int sender1Index = (receiverIndex + 1) % batchAccountCount;
                        Account potentialSender1 = currentBatchAccounts[sender1Index];

                        Account? potentialSender2 = null;
                        if (batchAccountCount > 2)
                        {
                            int sender2Index = (receiverIndex + 2) % batchAccountCount;
                            if (sender2Index != receiverIndex && sender2Index != sender1Index)
                            {
                                potentialSender2 = currentBatchAccounts[sender2Index];
                            }
                        }

                        AcceptAttemptResult result1 = AcceptAttemptResult.Skipped_SendNotSuccessful;
                        if (potentialSender1.UserId != receiverAccount.UserId)
                        {
                            var expectedPair1 = Tuple.Create(potentialSender1.UserId, receiverAccount.UserId);
                            if (batchSuccessfulSendPairs.Contains(expectedPair1))
                            {
                                batchAttemptedAccepts++;
                                result1 = await TryAcceptRequestAsync(receiverAccount, potentialSender1, batchAcceptedPairs, baseAcceptDelay, acceptRandomness);
                                switch (result1)
                                {
                                    case AcceptAttemptResult.Accepted: batchSuccessAccepts++; break;
                                    case AcceptAttemptResult.Failed: batchFailedAccepts++; break;
                                    case AcceptAttemptResult.Skipped_AlreadyDone:
                                    case AcceptAttemptResult.Skipped_InvalidSender:
                                    case AcceptAttemptResult.Skipped_InvalidReceiver: batchSkippedAccepts++; break;
                                }
                            }
                            else
                            {
                                Console.WriteLine($"            -> Skipping Accept {potentialSender1.Username} -> {receiverAccount.Username} (Send did not succeed/was not pending in Phase 1)");
                                batchSkippedAccepts++;
                                result1 = AcceptAttemptResult.Skipped_SendNotSuccessful;
                            }
                        }
                        else { Console.WriteLine($"            -> Internal error: potentialSender1 == receiver for batch index {receiverIndex}. Skipping accept attempt."); batchSkippedAccepts++; }


                        AcceptAttemptResult result2 = AcceptAttemptResult.Skipped_SendNotSuccessful;
                        if (potentialSender2 != null && potentialSender2.UserId != receiverAccount.UserId)
                        {
                            if (potentialSender2.UserId != potentialSender1.UserId)
                            {
                                var expectedPair2 = Tuple.Create(potentialSender2.UserId, receiverAccount.UserId);
                                if (batchSuccessfulSendPairs.Contains(expectedPair2))
                                {
                                    batchAttemptedAccepts++;
                                    result2 = await TryAcceptRequestAsync(receiverAccount, potentialSender2, batchAcceptedPairs, baseAcceptDelay, acceptRandomness);
                                    switch (result2)
                                    {
                                        case AcceptAttemptResult.Accepted: batchSuccessAccepts++; break;
                                        case AcceptAttemptResult.Failed: batchFailedAccepts++; break;
                                        case AcceptAttemptResult.Skipped_AlreadyDone:
                                        case AcceptAttemptResult.Skipped_InvalidSender:
                                        case AcceptAttemptResult.Skipped_InvalidReceiver: batchSkippedAccepts++; break;
                                    }
                                    if (result1 != AcceptAttemptResult.Skipped_AlreadyDone && result1 != AcceptAttemptResult.Skipped_SendNotSuccessful ||
                                       result2 != AcceptAttemptResult.Skipped_AlreadyDone && result2 != AcceptAttemptResult.Skipped_SendNotSuccessful)
                                    {
                                        await Task.Delay(Random.Shared.Next(AppConfig.CurrentApiDelayMs / 4, AppConfig.CurrentApiDelayMs / 3));
                                    }
                                }
                                else
                                {
                                    Console.WriteLine($"            -> Skipping Accept {potentialSender2.Username} -> {receiverAccount.Username} (Send did not succeed/was not pending in Phase 1)");
                                    batchSkippedAccepts++;
                                    result2 = AcceptAttemptResult.Skipped_SendNotSuccessful;
                                }
                            }
                            else { Console.WriteLine($"            -> Internal error: potentialSender2 == receiver for batch index {receiverIndex}. Skipping second accept attempt."); batchSkippedAccepts++; }
                        }
                        else if (potentialSender2 != null) { Console.WriteLine($"            -> Internal error: potentialSender2 == receiver for batch index {receiverIndex}. Skipping second accept attempt."); batchSkippedAccepts++; }


                        if (receiverIndex < batchAccountCount - 1)
                        {
                            await Task.Delay(Random.Shared.Next(AppConfig.CurrentApiDelayMs / 2, AppConfig.CurrentApiDelayMs));
                        }
                    }

                    batchStopwatchAccept.Stop();
                    Console.WriteLine($"\n{ConsoleUI.T_Vertical}      [*] Batch {batchNum + 1} Phase 2 Complete.");
                    Console.WriteLine($"         Batch Accepts: Attempted={batchAttemptedAccepts}, Success={batchSuccessAccepts}, Failed={batchFailedAccepts}, Skipped={batchSkippedAccepts}");
                    Console.WriteLine($"         Batch Time: {batchStopwatchAccept.ElapsedMilliseconds}ms");

                    totalAttemptedAccepts += batchAttemptedAccepts;
                    totalSuccessAccepts += batchSuccessAccepts;
                    totalFailedAccepts += batchFailedAccepts;
                    totalSkippedAccepts += batchSkippedAccepts;
                }

                if (useBatching && batchNum < numBatches - 1)
                {
                    Console.WriteLine($"{ConsoleUI.T_Vertical}   [*] --- Batch {batchNum + 1} finished. Waiting {batchDelaySeconds} seconds before next batch... ---");
                    await Task.Delay(TimeSpan.FromSeconds(batchDelaySeconds));
                }

            }

            overallStopwatch.Stop();
            Console.WriteLine($"\n\n[***] Overall Friend Action Summary [***]");
            Console.WriteLine($"--- Phase 1 (Sending) ---");
            Console.WriteLine($"   Total Attempted Sends: {totalAttemptedSends}, Total Successful Sends: {totalSuccessSends}");
            Console.WriteLine($"   Total Skipped (Pending/Friends): {totalPendingSends}, Total Failed Sends/Errors: {totalFailedSends}");
            Console.WriteLine($"--- Phase 2 (Accepting) ---");
            Console.WriteLine($"   Total Attempted Accepts (Send OK/Pending): {totalAttemptedAccepts}, Total Successful Accepts: {totalSuccessAccepts}");
            Console.WriteLine($"   Total Failed Accepts/Errors: {totalFailedAccepts}, Total Skipped Accepts (Other): {totalSkippedAccepts}");
            Console.WriteLine($"---------------------------");
            Console.WriteLine($"{ConsoleUI.T_Vertical}   [*] Total Time for Friend Actions: {overallStopwatch.ElapsedMilliseconds}ms ({overallStopwatch.Elapsed.TotalSeconds:F1}s)");
            Console.WriteLine($"{ConsoleUI.T_Vertical}   [*] Reminder: Check accounts with Verify action to confirm final friend counts.");
        }


        private async Task<AcceptAttemptResult> TryAcceptRequestAsync(
            Account receiver,
            Account sender,
            HashSet<Tuple<long, long>> acceptedPairs,
            int baseDelay,
            int randomness)
        {
            Console.WriteLine($"            Attempting Accept: {sender.Username} (ID: {sender.UserId}) -> {receiver.Username} (ID: {receiver.UserId})");

            var currentPair = Tuple.Create(sender.UserId, receiver.UserId);
            var reversePair = Tuple.Create(receiver.UserId, sender.UserId);
            if (acceptedPairs.Contains(currentPair) || acceptedPairs.Contains(reversePair))
            {
                Console.WriteLine($"            -> Skipped (Pair {sender.UserId}<->{receiver.UserId} already accepted in this run)");
                return AcceptAttemptResult.Skipped_AlreadyDone;
            }

            if (!sender.IsValid)
            {
                Console.WriteLine($"            -> Accept Skip (Sender {sender.Username} is marked invalid).");
                return AcceptAttemptResult.Skipped_InvalidSender;
            }

            if (!receiver.IsValid || string.IsNullOrEmpty(receiver.XcsrfToken))
            {
                Console.WriteLine($"            -> Accept Fail (Receiver account {receiver.Username} is invalid or missing token before accept).");
                return AcceptAttemptResult.Skipped_InvalidReceiver;
            }

            try
            {
                bool acceptOk = await _friendService.AcceptFriendRequestAsync(receiver, sender.UserId, sender.Username);
                await Task.Delay(Random.Shared.Next(Math.Max(AppConfig.MinAllowedDelayMs, baseDelay - randomness), baseDelay + randomness));

                if (acceptOk)
                {
                    Console.WriteLine($"{ConsoleUI.T_Vertical}               [+] -> Accept OK");
                    acceptedPairs.Add(currentPair);
                    acceptedPairs.Add(reversePair);
                    return AcceptAttemptResult.Accepted;
                }
                else
                {
                    Console.WriteLine($"{ConsoleUI.T_Vertical}               [!] -> Accept Fail (API Error/Not Pending/Already Friends?)");
                    return AcceptAttemptResult.Failed;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ConsoleUI.T_Vertical}               [!] -> Error Accepting ({sender.Username} -> {receiver.Username}): {ex.GetType().Name} - {ConsoleUI.Truncate(ex.Message)}");
                return AcceptAttemptResult.Failed;
            }
        }

        public async Task<bool> VerifyAccountStatusOnSelectedAsync(
            int requiredFriends,
            int requiredBadges,
            string expectedDisplayName,
            long expectedAvatarSourceId)
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
            if (validCount == 0) { Console.WriteLine($"{ConsoleUI.T_Vertical}   [!] No valid accounts selected for verification."); return false; }

            AvatarDetails? targetAvatarDetails = null;
            if (expectedAvatarSourceId > 0)
            {
                targetAvatarDetails = await GetOrFetchTargetAvatarDetailsAsync(expectedAvatarSourceId);
                if (targetAvatarDetails == null)
                {
                    Console.WriteLine($"{ConsoleUI.T_Vertical}   [?] Could not fetch target avatar details for ID {expectedAvatarSourceId}. Avatar check will be skipped for all accounts.");
                }
            }
            else { Console.WriteLine($"{ConsoleUI.T_Vertical}   [*] Skipping avatar check: No target avatar source ID specified (or <= 0)."); }

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
                    int apiLimitForBadges;
                    if (requiredBadges <= 0)
                    {
                        apiLimitForBadges = 10;
                    }
                    else if (requiredBadges <= 10)
                    {
                        apiLimitForBadges = 10;
                    }
                    else if (requiredBadges <= 25)
                    {
                        apiLimitForBadges = 25;
                    }
                    else if (requiredBadges <= 50)
                    {
                        apiLimitForBadges = 50;
                    }
                    else
                    {
                        apiLimitForBadges = 100;
                    }

                    if (requiredBadges <= 0)
                    {
                        badgeCount = 0;
                        Console.WriteLine("Skipped (Requirement is 0 or less).");
                    }
                    else
                    {
                        badgeCount = await _badgeService.GetBadgeCountAsync(acc, limit: apiLimitForBadges);

                        if (badgeCount == -1)
                        {
                            Console.WriteLine("Failed.");
                            errorOccurred = true;
                            failureReasons.Add("Badge check API failed");
                        }
                        else
                        {
                            Console.WriteLine($"{badgeCount} found (Recent, checked up to {apiLimitForBadges}).");
                            if (badgeCount < requiredBadges)
                            {
                                failureReasons.Add($"Badges {badgeCount} < {requiredBadges}");
                            }
                        }
                    }

                    if (!errorOccurred && requiredBadges > 0)
                    {
                        await Task.Delay(checkDelay);
                    }
                    else if (errorOccurred)
                    {
                        goto EndVerificationCheck;
                    }

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
                        Console.WriteLine($"{ConsoleUI.T_Vertical}   -> Status: ERROR ({string.Join(", ", failureReasons)})");
                        failedErrCount++;
                    }
                    else
                    {
                        bool friendsOk = friendCount >= requiredFriends;
                        bool badgesOk = requiredBadges <= 0 || badgeCount >= requiredBadges;
                        bool displayNameOk = string.Equals(currentDisplayName, expectedDisplayName, StringComparison.OrdinalIgnoreCase);
                        bool avatarOk = targetAvatarDetails == null ||
                                        (currentAvatarDetails != null && _avatarService.CompareAvatarDetails(currentAvatarDetails, targetAvatarDetails));

                        string fStat = friendsOk ? "OK" : $"FAIL ({friendCount}/{requiredFriends})";
                        string bStat;
                        if (requiredBadges <= 0) bStat = "OK (Req <= 0)";
                        else bStat = badgesOk ? "OK" : $"FAIL ({badgeCount}/{requiredBadges})";
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
                            Console.WriteLine($"{ConsoleUI.T_Vertical}      [+] -> Overall Status: PASS");
                            passedCount++;
                        }
                        else
                        {
                            currentStatus = VerificationStatus.Failed;
                            Console.WriteLine($"{ConsoleUI.T_Vertical}      [!] -> Overall Status: FAIL ({string.Join("; ", failureReasons)})");
                            failedReqCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{ConsoleUI.T_Vertical}      [!] Runtime Error during verification for {acc.Username}: {ex.GetType().Name} - {ex.Message}");
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
            Console.WriteLine($"{ConsoleUI.T_Vertical}      [+] Total Verified: {validCount}");
            Console.WriteLine($"{ConsoleUI.T_Vertical}      [*] Total Time: {stopwatch.ElapsedMilliseconds}ms ({stopwatch.Elapsed.TotalSeconds:F1}s)");

            return failedReqCount > 0 || failedErrCount > 0;
        }

        public async Task ExecuteAllAutoAsync()
        {
            string currentDisplayName = AppConfig.RuntimeDefaultDisplayName;
            long currentAvatarSourceId = AppConfig.RuntimeDefaultTargetUserIdForAvatarCopy;
            int currentFriendGoal = AppConfig.RuntimeDefaultFriendGoal;
            int currentBadgeGoal = AppConfig.RuntimeDefaultBadgeGoal;
            string currentBadgeGameId = AppConfig.RuntimeDefaultBadgeGameId;

            Console.WriteLine($"{ConsoleUI.T_Vertical}   [*] Executing Action: Execute All Auto (Uses Current Settings)");
            Console.WriteLine($"   Sequence: Set Name -> Set Avatar -> Limited Friends -> Get Badges (if interactive)");
            Console.WriteLine($"   Settings: Name='{currentDisplayName}', AvatarSrc={currentAvatarSourceId}, FriendGoal={currentFriendGoal}, BadgeGoal={currentBadgeGoal}, BadgeGame={currentBadgeGameId}");
            Console.WriteLine($"   (Actions will be skipped per-account if prerequisites are already met)");
            Console.WriteLine($"   (Failed actions may retry based on config: Retries={AppConfig.CurrentMaxApiRetries}, Delay={AppConfig.CurrentApiRetryDelayMs}ms)");

            Console.WriteLine("\n--- Starting: Set Display Name ---");
            await SetDisplayNameOnSelectedAsync(currentDisplayName);

            Console.WriteLine("\n--- Starting: Set Avatar ---");
            await SetAvatarOnSelectedAsync(currentAvatarSourceId);

            Console.WriteLine("\n--- Starting: Limited Friend Actions ---");
            await HandleLimitedFriendRequestsAsync(currentFriendGoal);

            Console.WriteLine("\n--- Starting: Get Badges ---");

            if (!Environment.UserInteractive)
            {
                Console.WriteLine($"{ConsoleUI.T_Vertical}   [!] Skipping 'Get Badges' step in non-interactive environment for 'Execute All Auto'.");
            }
            else if (string.IsNullOrWhiteSpace(currentBadgeGameId))
            {
                Console.WriteLine($"{ConsoleUI.T_Vertical}   [!] Skipping 'Get Badges' step: No valid Badge Game ID is configured.");
            }
            else if (currentBadgeGoal <= 0)
            {
                Console.WriteLine($"{ConsoleUI.T_Vertical}   [*] Skipping 'Get Badges' step: Badge Goal is zero or negative.");
            }
            else
            {
                await GetBadgesOnSelectedAsync(currentBadgeGoal, currentBadgeGameId);
            }

            Console.WriteLine($"{ConsoleUI.T_Vertical}   [*] Multi-Action Sequence 'Execute All Auto' Complete.");
            Console.WriteLine($"{ConsoleUI.T_Vertical}   [*] Suggestion: Run 'Verify Account Status' (Action 7) to check final state.");
        }
    }
}
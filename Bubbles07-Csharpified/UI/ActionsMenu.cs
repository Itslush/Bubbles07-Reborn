using Continuance.Actions;
using Continuance.Core;

namespace Continuance.UI
{
    public class ActionsMenu(AccountManager accountManager, AccountActionExecutor actionExecutor)
    {
        private readonly AccountManager _accountManager = accountManager ?? throw new ArgumentNullException(nameof(accountManager));
        private readonly AccountActionExecutor _actionExecutor = actionExecutor ?? throw new ArgumentNullException(nameof(actionExecutor));

        private static TimeSpan EstimateActionTimePerAccount(int actionChoice)
        {
            long apiDelayMs = AppConfig.CurrentApiDelayMs;
            long friendDelayMs = AppConfig.CurrentFriendActionDelayMs;
            long retryDelayMs = AppConfig.CurrentApiRetryDelayMs;

            _ = AppConfig.CurrentMaxApiRetries;

            double retryChance = 0.1;

            long baseApiCallCost = 500;
            long retryOverhead = (long)(retryChance * (retryDelayMs + baseApiCallCost));

            switch (actionChoice)
            {
                case 1:
                    return TimeSpan.FromMilliseconds(baseApiCallCost * 2 + retryOverhead + apiDelayMs);
                case 2:
                    return TimeSpan.FromMilliseconds(baseApiCallCost * 7 + retryOverhead * 5 + apiDelayMs * 5);
                case 3:
                    return TimeSpan.Zero;
                case 4:
                    return TimeSpan.Zero;
                case 5:
                    long sendEstimate = (baseApiCallCost + retryOverhead + friendDelayMs) * 2;
                    long acceptEstimate = (baseApiCallCost + retryOverhead + friendDelayMs) * 2;
                    return TimeSpan.FromMilliseconds(sendEstimate + acceptEstimate + apiDelayMs);
                case 6:
                    return TimeSpan.Zero;
                case 7:
                    return TimeSpan.FromMilliseconds(baseApiCallCost * 4 + apiDelayMs / 4 * 3 + apiDelayMs);
                case 8:
                    return EstimateActionTimePerAccount(1) + EstimateActionTimePerAccount(2) + EstimateActionTimePerAccount(5);
                default:
                    return TimeSpan.Zero;
            }
        }

        private static string FormatTimeEstimate(TimeSpan totalEstimate)
        {
            if (totalEstimate.TotalMilliseconds <= 0) return "";
            if (totalEstimate.TotalSeconds < 1) return "(< 1s)";
            if (totalEstimate.TotalSeconds < 60) return $"(~{totalEstimate.TotalSeconds:F0}s)";
            if (totalEstimate.TotalMinutes < 2) return $"(~{totalEstimate.TotalSeconds:F0}s)";
            if (totalEstimate.TotalMinutes < 60) return $"(~{totalEstimate.TotalMinutes:F1}m)";
            if (totalEstimate.TotalHours < 2) return $"(~{totalEstimate.TotalMinutes:F0}m)";

            return $"(~{totalEstimate.TotalHours:F1}h)";
        }

        private static string GetOverallEstimateString(int actionChoice, int validAccountCount)
        {
            if (validAccountCount == 0 && actionChoice != 0) return "(No valid accounts)";

            switch (actionChoice)
            {
                case 3: return "(Interactive)";
                case 4: return "(Interactive)";
                case 6: return "(Interactive)";
                case 5:
                    if (validAccountCount < 2) return "(Needs >= 2 valid)";
                    long preCheckMs = (long)(AppConfig.CurrentApiDelayMs * 1.5 * validAccountCount);
                    TimeSpan friendLoopTime = EstimateActionTimePerAccount(5) * validAccountCount;

                    long fixedWaitMs = (long)(AppConfig.DefaultRequestTimeoutSec / 2.0 * 1000);
                    TimeSpan totalEstimate = friendLoopTime + TimeSpan.FromMilliseconds(preCheckMs + fixedWaitMs);
                    return $"{FormatTimeEstimate(totalEstimate)}";
                case 8:
                    if (validAccountCount == 0) return "(No valid accounts)";
                    TimeSpan baseAutoTime = EstimateActionTimePerAccount(8) * validAccountCount;
                    long friendPreCheckMs = (long)(AppConfig.CurrentApiDelayMs * 1.5 * validAccountCount);

                    long friendFixedWaitMs = (long)(AppConfig.DefaultRequestTimeoutSec / 2.0 * 1000);
                    TimeSpan totalAutoEstimate = baseAutoTime + TimeSpan.FromMilliseconds(friendPreCheckMs + friendFixedWaitMs);
                    return $"{FormatTimeEstimate(totalAutoEstimate)} + Interactive Badges";
                default:
                    if (validAccountCount == 0) return "";
                    TimeSpan perAccount = EstimateActionTimePerAccount(actionChoice);
                    TimeSpan total = perAccount * validAccountCount;
                    return FormatTimeEstimate(total);
            }
        }

        public async Task Show()
        {
            bool back = false;
            while (!back)
            {
                var selectedAccounts = _accountManager.GetSelectedAccounts();
                int totalSelectedCount = selectedAccounts.Count;
                int validSelectedCount = selectedAccounts.Count(a => a != null && a.IsValid);
                int invalidSelectedCount = totalSelectedCount - validSelectedCount;

                Console.Clear();
                ConsoleUI.PrintMenuTitle($"Actions Menu ({totalSelectedCount} Selected)");

                if (totalSelectedCount == 0)
                {
                    ConsoleUI.WriteLineInsideBox("(No accounts selected - Use Main Menu Option 5)");
                }
                else if (invalidSelectedCount > 0)
                {
                    ConsoleUI.WriteLineInsideBox($"({validSelectedCount} valid, {invalidSelectedCount} invalid selected)");
                    ConsoleUI.WriteLineInsideBox("(Invalid accounts will be skipped for most actions)");
                }
                else
                {
                    ConsoleUI.WriteLineInsideBox($"({validSelectedCount} valid accounts selected)");
                }
                ConsoleUI.WriteLineInsideBox("");

                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"1. Set Display Name (Default: '{AppConfig.RuntimeDefaultDisplayName}') {GetOverallEstimateString(1, validSelectedCount)}"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"2. Set Avatar (Default Src: {AppConfig.RuntimeDefaultTargetUserIdForAvatarCopy}) {GetOverallEstimateString(2, validSelectedCount)}"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"3. Join Group (Interactive - Opens Browser for CAPTCHA) (Default ID: {AppConfig.RuntimeDefaultGroupId}) {GetOverallEstimateString(3, validSelectedCount)}"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"4. Get Badges (Game: {AppConfig.RuntimeDefaultBadgeGameId}, Goal: {AppConfig.RuntimeDefaultBadgeGoal}) {GetOverallEstimateString(4, validSelectedCount)}"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"5. Friend Accounts (Goal: ~{AppConfig.RuntimeDefaultFriendGoal}) {GetOverallEstimateString(5, validSelectedCount)}"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"6. Open Accounts In Browser - OVERALL USELESS ATM {GetOverallEstimateString(6, validSelectedCount)}"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"7. Verify Check (Uses Current Defaults) {GetOverallEstimateString(7, validSelectedCount)}"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"8. Execute All Auto (1->2->5->4, Uses Defaults) {GetOverallEstimateString(8, validSelectedCount)}"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_End, $"0. Back to Main Menu"));

                ConsoleUI.PrintMenuFooter("Choose action");
                string? choice = Console.ReadLine();

                if (totalSelectedCount == 0 && choice != "0")
                {
                    ConsoleUI.WriteErrorLine("No accounts selected. Please select accounts first (Main Menu Option 5).");

                    await Task.Delay(1500);
                    continue;
                }

                switch (choice)
                {
                    case "1":
                        {
                            string currentDefaultName = AppConfig.RuntimeDefaultDisplayName;

                            ConsoleUI.WriteInfoLine($"Current default display name: '{currentDefaultName}'");
                            Console.Write($"{ConsoleUI.T_Vertical}   Enter new name for this run (or press Enter to use default): ");

                            string? nameOverride = Console.ReadLine();
                            string nameToUse = string.IsNullOrWhiteSpace(nameOverride) ? currentDefaultName : nameOverride.Trim();

                            if (string.IsNullOrWhiteSpace(nameToUse) || nameToUse.Length < 3 || nameToUse.Length > 20)
                            {
                                ConsoleUI.WriteErrorLine($"Invalid name '{nameToUse}'. Must be 3-20 characters. Action cancelled.");
                            }
                            else
                            {
                                ConsoleUI.WriteInfoLine($"Executing SetDisplayName with target: '{nameToUse}'");
                                await _actionExecutor.SetDisplayNameOnSelectedAsync(nameToUse);
                            }
                        }
                        break;
                    case "2":
                        {
                            long currentDefaultSourceId = AppConfig.RuntimeDefaultTargetUserIdForAvatarCopy;
                            ConsoleUI.WriteInfoLine($"Current default avatar source User ID: {currentDefaultSourceId}");

                            long sourceIdToUse = GetLongInput($"[?] Enter source User ID for this run (or blank for default):", currentDefaultSourceId, 1);
                            if (sourceIdToUse <= 0)
                            {
                                ConsoleUI.WriteErrorLine($"Invalid source User ID '{sourceIdToUse}'. Action cancelled.");
                            }
                            else
                            {
                                ConsoleUI.WriteInfoLine($"Executing SetAvatar with source User ID: {sourceIdToUse}");
                                await _actionExecutor.SetAvatarOnSelectedAsync(sourceIdToUse);
                            }
                        }
                        break;
                    case "3":
                        {
                            if (!Environment.UserInteractive)
                            {
                                ConsoleUI.WriteErrorLine("Join Group (Interactive) action requires an interactive environment.");
                                break;
                            }

                            long currentDefaultGroupId = AppConfig.RuntimeDefaultGroupId;
                            ConsoleUI.WriteInfoLine($"Current default Group ID: {currentDefaultGroupId}");

                            long groupIdToUse = GetLongInput($"[?] Enter Group ID for this run (or blank for default):", currentDefaultGroupId, 1);

                            if (groupIdToUse <= 0)
                            {
                                ConsoleUI.WriteErrorLine($"Invalid Group ID '{groupIdToUse}'. Action cancelled.");
                            }
                            else
                            {
                                ConsoleUI.WriteInfoLine($"Executing Interactive JoinGroup with Group ID: {groupIdToUse}");
                                await _actionExecutor.JoinGroupInteractiveOnSelectedAsync(groupIdToUse);
                            }
                        }
                        break;
                    case "4":
                        {
                            if (!Environment.UserInteractive)
                            {
                                ConsoleUI.WriteErrorLine("Get Badges action requires an interactive environment.");
                                break;
                            }
                            int currentDefaultBadgeGoal = AppConfig.RuntimeDefaultBadgeGoal;
                            string currentGameId = AppConfig.RuntimeDefaultBadgeGameId;

                            ConsoleUI.WriteInfoLine($"Current Badge Game ID: {currentGameId}");
                            ConsoleUI.WriteInfoLine($"Current default badge goal: {currentDefaultBadgeGoal}");

                            string gameIdToUse = GetStringInput($"[?] Enter Game ID (Place ID) for this run (or blank for default '{currentGameId}'): ", currentGameId);
                            if (string.IsNullOrWhiteSpace(gameIdToUse) || !long.TryParse(gameIdToUse, out _))
                            {
                                ConsoleUI.WriteErrorLine($"Invalid Game ID format: '{gameIdToUse}'. Action cancelled.");
                                break;
                            }

                            int badgeGoalToUse = GetIntInput($"[?] Enter target badge count (or blank for default {currentDefaultBadgeGoal}): ", currentDefaultBadgeGoal, 0);

                            if (badgeGoalToUse <= 0)
                            {
                                ConsoleUI.WriteInfoLine("Badge goal is zero or negative, skipping badge acquisition.");
                            }
                            else
                            {
                                ConsoleUI.WriteInfoLine($"Executing GetBadges with Goal: {badgeGoalToUse}, Game ID: {gameIdToUse}");
                                await _actionExecutor.GetBadgesOnSelectedAsync(badgeGoalToUse, gameIdToUse);
                            }
                        }
                        break;
                    case "5":
                        {
                            int currentFriendGoal = AppConfig.RuntimeDefaultFriendGoal;
                            ConsoleUI.WriteInfoLine($"Current default friend goal: {currentFriendGoal}");

                            int friendGoalToUse = GetIntInput($"[?] Enter target friend count for this run (or blank for default): ", currentFriendGoal, 0);

                            if (friendGoalToUse < 0)
                            {
                                ConsoleUI.WriteErrorLine("Friend goal cannot be negative. Action cancelled.");
                            }
                            else if (friendGoalToUse < 2 && validSelectedCount >= 2)
                            {
                                ConsoleUI.WriteWarningLine("Friend goal less than 2 might not be very effective for this action.");
                                await _actionExecutor.HandleFriendRequestsAsync(friendGoalToUse);
                            }
                            else if (validSelectedCount < 2)
                            {
                                ConsoleUI.WriteErrorLine("Need at least 2 selected accounts for friend actions.");
                            }
                            else
                            {
                                ConsoleUI.WriteInfoLine($"Executing Limited Friend Actions with goal: {friendGoalToUse}");
                                await _actionExecutor.HandleFriendRequestsAsync(friendGoalToUse);
                            }
                        }
                        break;
                    case "6":
                        if (!Environment.UserInteractive)
                        {
                            ConsoleUI.WriteErrorLine("Open in Browser action requires an interactive environment.");
                            break;
                        }
                        await _actionExecutor.OpenInBrowserOnSelectedAsync();
                        break;
                    case "7":
                        {
                            int requiredFriends = AppConfig.RuntimeDefaultFriendGoal;
                            int requiredBadges = AppConfig.RuntimeDefaultBadgeGoal;
                            string expectedDisplayName = AppConfig.RuntimeDefaultDisplayName;
                            long expectedAvatarSource = AppConfig.RuntimeDefaultTargetUserIdForAvatarCopy;

                            ConsoleUI.WriteInfoLine($"Running Verification Check with Current Defaults:");
                            Console.WriteLine($"    Friends >= {requiredFriends}, Badges >= {requiredBadges}");
                            Console.WriteLine($"    Name == '{expectedDisplayName}', Avatar Source == {expectedAvatarSource}");
                            ConsoleUI.WriteWarningLine("You can customize these requirements by editing 'settings.json' or using other actions first.");

                            bool hadFailures = await _actionExecutor.VerifyAccountStatusOnSelectedAsync(
                                requiredFriends, requiredBadges, expectedDisplayName, expectedAvatarSource);

                            if (hadFailures)
                            {
                                Console.Write($"\n[?] Verification complete. Some accounts failed. Select failed accounts? (y/n): ");
                                string? selectChoice = Console.ReadLine()?.ToLower();
                                if (selectChoice == "y")
                                {
                                    _accountManager.SelectFailedVerification();
                                }
                            }
                            else
                            {
                                ConsoleUI.WriteInfoLine($"\nVerification complete. No failures detected based on requirements.");
                            }
                        }
                        break;
                    case "8": await _actionExecutor.ExecuteAllAutoAsync(); break;
                    case "0": back = true; break;
                    default: ConsoleUI.WriteErrorLine("Invalid choice."); break;
                }

                if (!back)
                {
                    Console.WriteLine("\nAction complete. Press Enter to return to Actions Menu...");
                    Console.ReadLine();
                }
            }
            Console.Clear();
        }

        private static int GetIntInput(string prompt, int defaultValue, int? minValue = null, int? maxValue = null)
        {
            Console.Write(prompt + " ");

            string? input = Console.ReadLine();
            bool parsed = int.TryParse(input, out int parsedValue);

            if (!string.IsNullOrWhiteSpace(input))
            {
                if (parsed)
                {
                    if (minValue.HasValue && parsedValue < minValue.Value)
                    {
                        ConsoleUI.WriteErrorLine($"Input ({parsedValue}) is below minimum allowed ({minValue.Value}). Using default ({defaultValue}).");
                        return defaultValue;
                    }
                    if (maxValue.HasValue && parsedValue > maxValue.Value)
                    {
                        ConsoleUI.WriteErrorLine($"Input ({parsedValue}) is above maximum allowed ({maxValue.Value}). Using default ({defaultValue}).");
                        return defaultValue;
                    }
                    return parsedValue;
                }
                else
                {
                    ConsoleUI.WriteErrorLine($"Invalid integer input. Using default value ({defaultValue}).");
                }
            }
            return defaultValue;
        }

        private static long GetLongInput(string prompt, long defaultValue, long? minValue = null, long? maxValue = null)
        {
            Console.Write(prompt + " ");

            string? input = Console.ReadLine();
            bool parsed = long.TryParse(input, out long parsedValue);

            if (!string.IsNullOrWhiteSpace(input))
            {
                if (parsed)
                {
                    if (minValue.HasValue && parsedValue < minValue.Value)
                    {
                        ConsoleUI.WriteErrorLine($"Input ({parsedValue}) is below minimum allowed ({minValue.Value}). Using default ({defaultValue}).");
                        return defaultValue;
                    }
                    if (maxValue.HasValue && parsedValue > maxValue.Value)
                    {
                        ConsoleUI.WriteErrorLine($"Input ({parsedValue}) is above maximum allowed ({maxValue.Value}). Using default ({defaultValue}).");
                        return defaultValue;
                    }
                    return parsedValue;
                }
                else
                {
                    ConsoleUI.WriteErrorLine($"Invalid long integer input. Using default value ({defaultValue}).");
                }
            }
            return defaultValue;
        }

        private static string GetStringInput(string prompt, string defaultValue)
        {
            Console.Write(prompt + " ");
            string? input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
            {
                return defaultValue;
            }
            return input.Trim();
        }

        public static void AdjustRateLimitsUI()
        {
            Console.Clear();
            ConsoleUI.PrintMenuTitle("Adjust Rate Limits, Timeout & Retries");
            ConsoleUI.WriteLineInsideBox("Setting delays too low increases risk of rate limiting or account flags.");
            ConsoleUI.WriteLineInsideBox($"Min API/Friend Delay: {AppConfig.MinAllowedDelayMs}ms | Min Retry Delay: {AppConfig.MinRetryDelayMs}ms");
            ConsoleUI.WriteLineInsideBox($"Changes apply until restart OR saved via Main Menu.");
            ConsoleUI.WriteLineInsideBox("");

            ConsoleUI.WriteLineInsideBox($"1. General API Delay (Between accounts/steps):");
            ConsoleUI.WriteLineInsideBox($"   Current: {AppConfig.CurrentApiDelayMs}ms / Default Const: {AppConfig.DefaultApiDelayMs}ms");

            int newApiDelay = GetIntInput($"[?] New delay (ms, >= {AppConfig.MinAllowedDelayMs}) or blank: ", AppConfig.CurrentApiDelayMs, AppConfig.MinAllowedDelayMs);

            if (newApiDelay != AppConfig.CurrentApiDelayMs)
            {
                AppConfig.CurrentApiDelayMs = newApiDelay;
                ConsoleUI.WriteSuccessLine($"General API Delay set to {AppConfig.CurrentApiDelayMs}ms for this session.");
            }
            else { ConsoleUI.WriteInfoLine($"General API Delay remains {AppConfig.CurrentApiDelayMs}ms."); }

            ConsoleUI.WriteLineInsideBox($"\n2. Friend Action Delay (Send/Accept):");
            ConsoleUI.WriteLineInsideBox($"   Current: {AppConfig.CurrentFriendActionDelayMs}ms / Default Const: {AppConfig.DefaultFriendActionDelayMs}ms");

            int newFriendDelay = GetIntInput($"[?] New delay (ms, >= {AppConfig.MinAllowedDelayMs}) or blank: ", AppConfig.CurrentFriendActionDelayMs, AppConfig.MinAllowedDelayMs);

            if (newFriendDelay != AppConfig.CurrentFriendActionDelayMs)
            {
                AppConfig.CurrentFriendActionDelayMs = newFriendDelay;
                ConsoleUI.WriteSuccessLine($"Friend Action Delay set to {AppConfig.CurrentFriendActionDelayMs}ms for this session.");
            }
            else { ConsoleUI.WriteInfoLine($"Friend Action Delay remains {AppConfig.CurrentFriendActionDelayMs}ms."); }

            ConsoleUI.WriteLineInsideBox($"\n3. Request Timeout (Max wait for API response):");
            ConsoleUI.WriteLineInsideBox($"   Current: {AppConfig.DefaultRequestTimeoutSec}s / Default Const: {AppConfig.DefaultRequestTimeoutSec}s");

            int newTimeoutSec = GetIntInput($"[?] New timeout (seconds, 5-120) or blank: ", AppConfig.DefaultRequestTimeoutSec, 5, 120);

            if (newTimeoutSec != AppConfig.DefaultRequestTimeoutSec)
            {
                AppConfig.DefaultRequestTimeoutSec = newTimeoutSec;
                ConsoleUI.WriteSuccessLine($"Request Timeout set to {AppConfig.DefaultRequestTimeoutSec}s for this session.");
            }
            else { ConsoleUI.WriteInfoLine($"Request Timeout remains {AppConfig.DefaultRequestTimeoutSec}s."); }

            ConsoleUI.WriteLineInsideBox($"\n4. Max Action Retries (Attempts after initial failure):");
            ConsoleUI.WriteLineInsideBox($"   Current: {AppConfig.CurrentMaxApiRetries} / Default Const: {AppConfig.DefaultMaxApiRetries}");

            int newMaxRetries = GetIntInput($"[?] New max retries (0+) or blank: ", AppConfig.CurrentMaxApiRetries, 0);

            if (newMaxRetries != AppConfig.CurrentMaxApiRetries)
            {
                AppConfig.CurrentMaxApiRetries = newMaxRetries;
                ConsoleUI.WriteSuccessLine($"Max Action Retries set to {AppConfig.CurrentMaxApiRetries} for this session.");
            }
            else { ConsoleUI.WriteInfoLine($"Max Action Retries remains {AppConfig.CurrentMaxApiRetries}."); }

            ConsoleUI.WriteLineInsideBox($"\n5. Action Retry Delay (Wait between retries):");
            ConsoleUI.WriteLineInsideBox($"   Current: {AppConfig.CurrentApiRetryDelayMs}ms / Default Const: {AppConfig.DefaultApiRetryDelayMs}ms");

            int newRetryDelay = GetIntInput($"[?] New retry delay (ms, >= {AppConfig.MinRetryDelayMs}) or blank: ", AppConfig.CurrentApiRetryDelayMs, AppConfig.MinRetryDelayMs);

            if (newRetryDelay != AppConfig.CurrentApiRetryDelayMs)
            {
                AppConfig.CurrentApiRetryDelayMs = newRetryDelay;
                ConsoleUI.WriteSuccessLine($"Action Retry Delay set to {AppConfig.CurrentApiRetryDelayMs}ms for this session.");
            }
            else { ConsoleUI.WriteInfoLine($"Action Retry Delay remains {AppConfig.CurrentApiRetryDelayMs}ms."); }

            ConsoleUI.WriteLineInsideBox($"\n6. Action Confirmation Threshold (Accounts count to ask 'Proceed?'):");
            ConsoleUI.WriteLineInsideBox($"   Current: {AppConfig.CurrentActionConfirmationThreshold} / Default: 15");

            int newThreshold = GetIntInput($"[?] New threshold (0 = always ask, high number = never ask) or blank: ", AppConfig.CurrentActionConfirmationThreshold, 0);

            if (newThreshold != AppConfig.CurrentActionConfirmationThreshold)
            {
                AppConfig.CurrentActionConfirmationThreshold = newThreshold;
                ConsoleUI.WriteSuccessLine($"Action Confirmation Threshold set to {AppConfig.CurrentActionConfirmationThreshold} for this session.");
            }
            else { ConsoleUI.WriteInfoLine($"Action Confirmation Threshold remains {AppConfig.CurrentActionConfirmationThreshold}."); }

            Console.WriteLine("\n" + ConsoleUI.T_BottomLeft + new string(ConsoleUI.T_HorzBar[0], 50) + ConsoleUI.T_BottomRight);
        }
    }
}
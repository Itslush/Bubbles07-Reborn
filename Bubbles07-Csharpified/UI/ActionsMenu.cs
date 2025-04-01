using _Csharpified;
using Actions;
using Core;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace UI
{
    public class ActionsMenu
    {
        private readonly AccountManager _accountManager;
        private readonly AccountActionExecutor _actionExecutor;

        public ActionsMenu(AccountManager accountManager, AccountActionExecutor actionExecutor)
        {
            _accountManager = accountManager ?? throw new ArgumentNullException(nameof(accountManager));
            _actionExecutor = actionExecutor ?? throw new ArgumentNullException(nameof(actionExecutor));
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
                    ConsoleUI.WriteLineInsideBox("(No accounts selected - Use Main Menu Option 4)");
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

                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"1. Set Display Name (-> '{AppConfig.DefaultDisplayName}')"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"2. Set Avatar (Copy from UserID: {AppConfig.DefaultTargetUserIdForAvatarCopy})"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"3. Join Group (ID: {AppConfig.DefaultGroupId})"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"4. Get Badges (Game: {AppConfig.DefaultBadgeGameId}, Launches Player, Interactive)"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"5. Friend Actions - Limited (Goal: ~{AppConfig.DefaultFriendGoal} friends/acc)"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"6. Open in Browser (Interactive, Non-Headless)"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"7. Verify Check (Friends, Badges, Name, Avatar - Uses Config Defaults)"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"8. Execute All Auto (1->2->5->4, Uses Defaults, Interactive for Badges)"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_End, $"0. Back to Main Menu"));

                ConsoleUI.PrintMenuFooter("Choose action");
                string? choice = Console.ReadLine();

                if (totalSelectedCount == 0 && choice != "0")
                {
                    ConsoleUI.WriteErrorLine("No accounts selected. Please select accounts first (Main Menu Option 4).");
                    await Task.Delay(1500);
                    continue;
                }

                switch (choice)
                {
                    case "1": await _actionExecutor.SetDisplayNameOnSelectedAsync(); break;
                    case "2": await _actionExecutor.SetAvatarOnSelectedAsync(); break;
                    case "3": await _actionExecutor.JoinGroupOnSelectedAsync(); break;
                    case "4":
                        {
                            if (!Environment.UserInteractive)
                            {
                                ConsoleUI.WriteErrorLine("Get Badges action requires an interactive environment.");
                                break;
                            }
                            Console.Write($"[?] Enter target badge count needed (default: {AppConfig.DefaultBadgeGoal}): ");
                            string? badgeInput = Console.ReadLine();
                            int badgeGoal = AppConfig.DefaultBadgeGoal;
                            if (!string.IsNullOrWhiteSpace(badgeInput) && int.TryParse(badgeInput, out int parsedBadgeGoal) && parsedBadgeGoal >= 0)
                            {
                                badgeGoal = parsedBadgeGoal;
                            }
                            else if (!string.IsNullOrWhiteSpace(badgeInput))
                            {
                                Console.WriteLine($"[!] Invalid input. Using default badge goal ({badgeGoal}).");
                            }
                            else { Console.WriteLine($"[*] Using default badge goal ({badgeGoal})."); }
                            await _actionExecutor.GetBadgesOnSelectedAsync(badgeGoal);
                        }
                        break;
                    case "5":
                        {
                            await _actionExecutor.HandleLimitedFriendRequestsAsync();
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
                            int requiredFriends = AppConfig.DefaultFriendGoal;
                            int requiredBadges = AppConfig.DefaultBadgeGoal;
                            string expectedDisplayName = AppConfig.DefaultDisplayName;
                            long expectedAvatarSource = AppConfig.DefaultTargetUserIdForAvatarCopy;

                            Console.WriteLine($"[*] Running Verification Check with Config Defaults:");
                            Console.WriteLine($"    Friends >= {requiredFriends}, Badges >= {requiredBadges}");
                            Console.WriteLine($"    Name == '{expectedDisplayName}', Avatar Source == {expectedAvatarSource}");

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
                                Console.WriteLine($"\n[*] Verification complete. No failures detected based on requirements.");
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

        public static void AdjustRateLimitsUI()
        {
            Console.Clear();
            ConsoleUI.PrintMenuTitle("Adjust Rate Limits, Timeout & Retries");
            ConsoleUI.WriteLineInsideBox("Setting delays too low increases risk of rate limiting or account flags.");
            ConsoleUI.WriteLineInsideBox($"Min API/Friend Delay: {AppConfig.MinAllowedDelayMs}ms | Min Retry Delay: {AppConfig.MinRetryDelayMs}ms");
            ConsoleUI.WriteLineInsideBox($"Changes apply until the application is restarted.");
            ConsoleUI.WriteLineInsideBox("");

            ConsoleUI.WriteLineInsideBox($"1. General API Delay (Between accounts/steps):");
            ConsoleUI.WriteLineInsideBox($"   Current: {AppConfig.CurrentApiDelayMs}ms / Default: {AppConfig.DefaultApiDelayMs}ms");
            int newApiDelay = GetIntInput($"[?] New delay (ms) or blank: ", AppConfig.CurrentApiDelayMs, AppConfig.MinAllowedDelayMs);
            if (newApiDelay != AppConfig.CurrentApiDelayMs)
            {
                AppConfig.CurrentApiDelayMs = newApiDelay;
                ConsoleUI.WriteSuccessLine($"General API Delay set to {AppConfig.CurrentApiDelayMs}ms.");
            }
            else { ConsoleUI.WriteInfoLine($"General API Delay remains {AppConfig.CurrentApiDelayMs}ms."); }

            ConsoleUI.WriteLineInsideBox($"\n2. Friend Action Delay (Send/Accept):");
            ConsoleUI.WriteLineInsideBox($"   Current: {AppConfig.CurrentFriendActionDelayMs}ms / Default: {AppConfig.DefaultFriendActionDelayMs}ms");
            int newFriendDelay = GetIntInput($"[?] New delay (ms) or blank: ", AppConfig.CurrentFriendActionDelayMs, AppConfig.MinAllowedDelayMs);
            if (newFriendDelay != AppConfig.CurrentFriendActionDelayMs)
            {
                AppConfig.CurrentFriendActionDelayMs = newFriendDelay;
                ConsoleUI.WriteSuccessLine($"Friend Action Delay set to {AppConfig.CurrentFriendActionDelayMs}ms.");
            }
            else { ConsoleUI.WriteInfoLine($"Friend Action Delay remains {AppConfig.CurrentFriendActionDelayMs}ms."); }

            ConsoleUI.WriteLineInsideBox($"\n3. Request Timeout (Max wait for API response):");
            ConsoleUI.WriteLineInsideBox($"   Current: {AppConfig.DefaultRequestTimeoutSec}s / Default: {AppConfig.DefaultRequestTimeoutSec}s");
            int newTimeoutSec = GetIntInput($"[?] New timeout (seconds, 5-120) or blank: ", AppConfig.DefaultRequestTimeoutSec, 5, 120);
            if (newTimeoutSec != AppConfig.DefaultRequestTimeoutSec)
            {
                AppConfig.DefaultRequestTimeoutSec = newTimeoutSec;
                ConsoleUI.WriteSuccessLine($"Request Timeout set to {AppConfig.DefaultRequestTimeoutSec}s.");
            }
            else { ConsoleUI.WriteInfoLine($"Request Timeout remains {AppConfig.DefaultRequestTimeoutSec}s."); }

            ConsoleUI.WriteLineInsideBox($"\n4. Max Action Retries (Attempts after initial failure):");
            ConsoleUI.WriteLineInsideBox($"   Current: {AppConfig.CurrentMaxApiRetries} / Default: {AppConfig.DefaultMaxApiRetries}");
            int newMaxRetries = GetIntInput($"[?] New max retries (0+) or blank: ", AppConfig.CurrentMaxApiRetries, 0);
            if (newMaxRetries != AppConfig.CurrentMaxApiRetries)
            {
                AppConfig.CurrentMaxApiRetries = newMaxRetries;
                ConsoleUI.WriteSuccessLine($"Max Action Retries set to {AppConfig.CurrentMaxApiRetries}.");
            }
            else { ConsoleUI.WriteInfoLine($"Max Action Retries remains {AppConfig.CurrentMaxApiRetries}."); }

            ConsoleUI.WriteLineInsideBox($"\n5. Action Retry Delay (Wait between retries):");
            ConsoleUI.WriteLineInsideBox($"   Current: {AppConfig.CurrentApiRetryDelayMs}ms / Default: {AppConfig.DefaultApiRetryDelayMs}ms");
            int newRetryDelay = GetIntInput($"[?] New retry delay (ms) or blank: ", AppConfig.CurrentApiRetryDelayMs, AppConfig.MinRetryDelayMs);
            if (newRetryDelay != AppConfig.CurrentApiRetryDelayMs)
            {
                AppConfig.CurrentApiRetryDelayMs = newRetryDelay;
                ConsoleUI.WriteSuccessLine($"Action Retry Delay set to {AppConfig.CurrentApiRetryDelayMs}ms.");
            }
            else { ConsoleUI.WriteInfoLine($"Action Retry Delay remains {AppConfig.CurrentApiRetryDelayMs}ms."); }

            Console.WriteLine("\n" + ConsoleUI.T_BottomLeft + new string(ConsoleUI.T_HorzBar[0], 50) + ConsoleUI.T_BottomRight);
        }
    }
}
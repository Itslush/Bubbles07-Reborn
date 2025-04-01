using System;
using System.Linq;
using System.Threading.Tasks;
using _Csharpified;
using Actions;
using Core;

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

        private static int GetIntInput(string prompt, int defaultValue)
        {
            Console.Write(prompt + " ");
            string? input = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(input) && int.TryParse(input, out int parsedValue) && parsedValue >= 0)
            {
                return parsedValue;
            }
            else if (!string.IsNullOrWhiteSpace(input))
            {
                Console.WriteLine($"[!] Invalid input. Using default value ({defaultValue}).");
            }
            return defaultValue;
        }

        public static void AdjustRateLimitsUI()
        {
            Console.Clear();
            ConsoleUI.PrintMenuTitle("Adjust Rate Limits & Timeout");
            ConsoleUI.WriteLineInsideBox("Setting delays too low increases risk of rate limiting or account flags.");
            ConsoleUI.WriteLineInsideBox($"Minimum allowed delay is {AppConfig.MinAllowedDelayMs}ms.");
            ConsoleUI.WriteLineInsideBox($"Changes apply until the application is restarted.");
            ConsoleUI.WriteLineInsideBox("");

            ConsoleUI.WriteLineInsideBox($"1. General API Delay (Non-critical actions):");
            ConsoleUI.WriteLineInsideBox($"   Current: {AppConfig.CurrentApiDelayMs}ms / Default: {AppConfig.DefaultApiDelayMs}ms / Safe Min: {AppConfig.SafeApiDelayMs}ms");
            int newApiDelay = GetIntInput($"[?] Enter new delay in ms (or leave blank/0 for current):", AppConfig.CurrentApiDelayMs);
            if (newApiDelay != AppConfig.CurrentApiDelayMs)
            {
                if (newApiDelay >= AppConfig.MinAllowedDelayMs)
                {
                    AppConfig.CurrentApiDelayMs = newApiDelay;
                    ConsoleUI.WriteSuccessLine($"General API Delay set to {AppConfig.CurrentApiDelayMs}ms.");
                    if (newApiDelay < AppConfig.SafeApiDelayMs) { ConsoleUI.WriteWarningLine($"Warning: Below suggested safe minimum ({AppConfig.SafeApiDelayMs}ms)."); }
                }
                else { ConsoleUI.WriteErrorLine($"Input ({newApiDelay}ms) is below minimum allowed ({AppConfig.MinAllowedDelayMs}ms). Value not changed."); }
            }
            else { ConsoleUI.WriteInfoLine($"General API Delay remains {AppConfig.CurrentApiDelayMs}ms."); }

            ConsoleUI.WriteLineInsideBox($"\n2. Friend Action Delay (Friend Send/Accept - Sensitive):");
            ConsoleUI.WriteLineInsideBox($"   Current: {AppConfig.CurrentFriendActionDelayMs}ms / Default: {AppConfig.DefaultFriendActionDelayMs}ms / Safe Min: {AppConfig.SafeFriendActionDelayMs}ms");
            int newFriendDelay = GetIntInput($"[?] Enter new delay in ms (or leave blank/0 for current):", AppConfig.CurrentFriendActionDelayMs);
            if (newFriendDelay != AppConfig.CurrentFriendActionDelayMs)
            {
                if (newFriendDelay >= AppConfig.MinAllowedDelayMs)
                {
                    AppConfig.CurrentFriendActionDelayMs = newFriendDelay;
                    ConsoleUI.WriteSuccessLine($"Friend Action Delay set to {AppConfig.CurrentFriendActionDelayMs}ms.");
                    if (newFriendDelay < AppConfig.SafeFriendActionDelayMs) { ConsoleUI.WriteWarningLine($"Warning: Below suggested safe minimum ({AppConfig.SafeFriendActionDelayMs}ms)."); }
                }
                else { ConsoleUI.WriteErrorLine($"Input ({newFriendDelay}ms) is below minimum allowed ({AppConfig.MinAllowedDelayMs}ms). Value not changed."); }
            }
            else { ConsoleUI.WriteInfoLine($"Friend Action Delay remains {AppConfig.CurrentFriendActionDelayMs}ms."); }

            ConsoleUI.WriteLineInsideBox($"\n3. Request Timeout (Max wait for API response):");
            ConsoleUI.WriteLineInsideBox($"   Current: {AppConfig.DefaultRequestTimeoutSec}s / Default: {AppConfig.DefaultRequestTimeoutSec}s");
            int newTimeoutSec = GetIntInput($"[?] Enter new timeout in seconds (e.g., 15, 30) (or leave blank/0 for current):", AppConfig.DefaultRequestTimeoutSec);
            if (newTimeoutSec != AppConfig.DefaultRequestTimeoutSec)
            {
                if (newTimeoutSec >= 5 && newTimeoutSec <= 120)
                {
                    AppConfig.DefaultRequestTimeoutSec = newTimeoutSec;
                    ConsoleUI.WriteSuccessLine($"Request Timeout set to {AppConfig.DefaultRequestTimeoutSec}s.");
                }
                else { ConsoleUI.WriteErrorLine($"Input ({newTimeoutSec}s) is outside reasonable bounds (5-120s). Value not changed."); }
            }
            else { ConsoleUI.WriteInfoLine($"Request Timeout remains {AppConfig.DefaultRequestTimeoutSec}s."); }

            Console.WriteLine("\n" + ConsoleUI.T_BottomLeft + new string(ConsoleUI.T_HorzBar[0], 50) + ConsoleUI.T_BottomRight);
        }
    }
}
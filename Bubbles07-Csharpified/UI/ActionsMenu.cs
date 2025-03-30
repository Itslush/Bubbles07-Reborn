using _Csharpified.Actions;
using _Csharpified.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace
    _Csharpified.UI
{
    public class ActionsMenu
    {
        private readonly AccountManager _accountManager;
        private readonly AccountActionExecutor _actionExecutor; // Dependency for executing actions

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
                int totalSelectedCount = selectedAccounts.Count; // Use count from manager's selected list
                int validSelectedCount = selectedAccounts.Count(a => a.IsValid);
                int invalidSelectedCount = totalSelectedCount - validSelectedCount;

                ConsoleUI.PrintMenuTitle($"Actions Menu ({totalSelectedCount} Selected)");

                if (totalSelectedCount == 0) { ConsoleUI.WriteLineInsideBox("(No accounts selected - Use Main Menu Option 4)"); return; }
                else if (invalidSelectedCount > 0) { ConsoleUI.WriteLineInsideBox($"({validSelectedCount} valid, {invalidSelectedCount} invalid - Invalid accounts will be skipped)"); }
                else { ConsoleUI.WriteLineInsideBox($"({validSelectedCount} valid accounts selected)"); }
                ConsoleUI.WriteLineInsideBox("");

                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"1. Set Display Name (-> '{AppConfig.DefaultDisplayName}')"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"2. Set Avatar (Copy from UserID: {AppConfig.DefaultTargetUserIdForAvatarCopy})"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"3. Join Group (ID: {AppConfig.DefaultGroupId})"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"4. Get Badges (Game: {AppConfig.DefaultBadgeGameId} - Launches Player, Interactive)"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"5. Friend Actions - Uncontrolled (Send/Accept All Selected <-> All Selected)"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"6. Friend Actions - Limited (Goal: ~2 friends per account)"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"7. Open in Browser (Interactive, Non-Headless)"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"8. Verify Check (Check Friends >= 2, Badges >= 5)"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"9. Execute All Auto (1, 2, 3, 5 - Non-Interactive, Uncontrolled Friends)"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_End, $"0. Back to Main Menu"));

                ConsoleUI.PrintMenuFooter("Choose action");
                string? choice = Console.ReadLine();

                switch (choice)
                {
                    case "1": await _actionExecutor.SetDisplayNameOnSelectedAsync(); break;
                    case "2": await _actionExecutor.SetAvatarOnSelectedAsync(); break;
                    case "3": await _actionExecutor.JoinGroupOnSelectedAsync(); break;
                    case "4": await _actionExecutor.GetBadgesOnSelectedAsync(); break;
                    case "5": await _actionExecutor.HandleUncontrolledFriendRequestsAsync(); break;
                    case "6": await _actionExecutor.HandleLimitedFriendRequestsAsync(); break;
                    case "7": await _actionExecutor.OpenInBrowserOnSelectedAsync(); break;
                    case "8": await _actionExecutor.VerifyAccountStatusOnSelectedAsync(); break;
                    case "9": await _actionExecutor.ExecuteAllAutoAsync(); break;
                    case "0": back = true; break;
                    default: ConsoleUI.WriteErrorLine("Invalid choice."); break;
                }
                if (!back) await Task.Delay(500);
            }
        }

        // Moved from Program originally
        public void AdjustRateLimitsUI()
        {
            Console.WriteLine($"\n---[ Adjust Rate Limits ]---");
            Console.WriteLine($"Setting delays too low increases risk of rate limiting or account flags.");
            Console.WriteLine($"Minimum allowed delay is {AppConfig.MinAllowedDelayMs}ms.");

            Console.WriteLine($"\n1. General API Delay (Used between non-friend actions):");
            Console.WriteLine($"   Current: {AppConfig.CurrentApiDelayMs}ms");
            Console.WriteLine($"   Default: {AppConfig.DefaultApiDelayMs}ms");
            Console.WriteLine($"   Suggested Safe Minimum: {AppConfig.SafeApiDelayMs}ms");
            Console.Write($"[?] Enter new delay in milliseconds (or leave blank to keep current): ");
            string? apiInput = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(apiInput) && int.TryParse(apiInput, out int newApiDelay))
            {
                if (newApiDelay >= AppConfig.MinAllowedDelayMs)
                {
                    AppConfig.CurrentApiDelayMs = newApiDelay;
                    Console.WriteLine($"[+] General API Delay set to {AppConfig.CurrentApiDelayMs}ms.");
                    if (newApiDelay < AppConfig.SafeApiDelayMs) { Console.WriteLine($"[!] Warning: Set below suggested safe minimum of {AppConfig.SafeApiDelayMs}ms."); }
                }
                else { Console.WriteLine($"[!] Input ({newApiDelay}ms) is below minimum allowed ({AppConfig.MinAllowedDelayMs}ms). Value not changed."); }
            }
            else if (!string.IsNullOrWhiteSpace(apiInput)) { Console.WriteLine($"[!] Invalid input. Value not changed."); }
            else { Console.WriteLine($"[-] No change made to General API Delay."); }

            Console.WriteLine($"\n2. Friend Action Delay (Base delay for friend Send/Accept):");
            Console.WriteLine($"   Current: {AppConfig.CurrentFriendActionDelayMs}ms");
            Console.WriteLine($"   Default: {AppConfig.DefaultFriendActionDelayMs}ms");
            Console.WriteLine($"   Suggested Safe Minimum: {AppConfig.SafeFriendActionDelayMs}ms");
            Console.Write($"[?] Enter new delay in milliseconds (or leave blank to keep current): ");
            string? friendInput = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(friendInput) && int.TryParse(friendInput, out int newFriendDelay))
            {
                if (newFriendDelay >= AppConfig.MinAllowedDelayMs)
                {
                    AppConfig.CurrentFriendActionDelayMs = newFriendDelay;
                    Console.WriteLine($"[+] Friend Action Delay set to {AppConfig.CurrentFriendActionDelayMs}ms.");
                    if (newFriendDelay < AppConfig.SafeFriendActionDelayMs) { Console.WriteLine($"[!] Warning: Set below suggested safe minimum of {AppConfig.SafeFriendActionDelayMs}ms."); }
                }
                else { Console.WriteLine($"[!] Input ({newFriendDelay}ms) is below minimum allowed ({AppConfig.MinAllowedDelayMs}ms). Value not changed."); }
            }
            else if (!string.IsNullOrWhiteSpace(friendInput)) { Console.WriteLine($"[!] Invalid input. Value not changed."); }
            else { Console.WriteLine($"[-] No change made to Friend Action Delay."); }

            Console.WriteLine($"----------------------------");
        }
    }
}
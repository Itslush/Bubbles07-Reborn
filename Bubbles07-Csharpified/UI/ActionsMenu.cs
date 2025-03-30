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
                int validSelectedCount = selectedAccounts.Count(a => a.IsValid);
                int invalidSelectedCount = totalSelectedCount - validSelectedCount;

                ConsoleUI.PrintMenuTitle($"Actions Menu ({totalSelectedCount} Selected)");

                if (totalSelectedCount == 0) { ConsoleUI.WriteLineInsideBox("(No accounts selected - Use Main Menu Option 4)"); return; }
                else if (invalidSelectedCount > 0) { ConsoleUI.WriteLineInsideBox($"({validSelectedCount} valid, {invalidSelectedCount} invalid - Invalid accounts will be skipped for most actions)"); }
                else { ConsoleUI.WriteLineInsideBox($"({validSelectedCount} valid accounts selected)"); }
                ConsoleUI.WriteLineInsideBox("");

                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"1. Set Display Name (-> '{AppConfig.DefaultDisplayName}')"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"2. Set Avatar (Copy from UserID: {AppConfig.DefaultTargetUserIdForAvatarCopy})"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"3. Join Group (ID: {AppConfig.DefaultGroupId})"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"4. Get Badges (Game: {AppConfig.DefaultBadgeGameId} - Launches Player, Interactive)"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"5. Friend Actions - Limited (Goal: ~{AppConfig.DefaultFriendGoal} friends per account)"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"6. Open in Browser (Interactive, Non-Headless)"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"7. Verify Check (Customizable Requirements)"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_Branch, $"8. Execute All Auto (1, 2, 5, 4 - Uses Defaults, Non-Interactive)"));
                ConsoleUI.WriteLineInsideBox(ConsoleUI.TreeLine(ConsoleUI.T_End, $"0. Back to Main Menu"));

                ConsoleUI.PrintMenuFooter("Choose action");
                string? choice = Console.ReadLine();

                switch (choice)
                {
                    case "1": await _actionExecutor.SetDisplayNameOnSelectedAsync(); break;
                    case "2": await _actionExecutor.SetAvatarOnSelectedAsync(); break;
                    case "3": await _actionExecutor.JoinGroupOnSelectedAsync(); break;
                    case "4":
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
                        await _actionExecutor.GetBadgesOnSelectedAsync(badgeGoal);
                        break;
                    case "5":
                        await _actionExecutor.HandleLimitedFriendRequestsAsync();
                        break;
                    case "6": await _actionExecutor.OpenInBrowserOnSelectedAsync(); break;
                    case "7":
                        Console.Write($"[?] Enter required friend count (default: {AppConfig.DefaultFriendGoal}): ");
                        string? friendReqInput = Console.ReadLine();
                        int requiredFriends = AppConfig.DefaultFriendGoal;
                        if (!string.IsNullOrWhiteSpace(friendReqInput) && int.TryParse(friendReqInput, out int parsedFriendReq) && parsedFriendReq >= 0)
                        {
                            requiredFriends = parsedFriendReq;
                        }
                        else if (!string.IsNullOrWhiteSpace(friendReqInput))
                        {
                            Console.WriteLine($"[!] Invalid input. Using default friend requirement ({requiredFriends}).");
                        }

                        Console.Write($"[?] Enter required badge count (default: {AppConfig.DefaultBadgeGoal}): ");
                        string? badgeReqInput = Console.ReadLine();
                        int requiredBadges = AppConfig.DefaultBadgeGoal;
                        if (!string.IsNullOrWhiteSpace(badgeReqInput) && int.TryParse(badgeReqInput, out int parsedBadgeReq) && parsedBadgeReq >= 0)
                        {
                            requiredBadges = parsedBadgeReq;
                        }
                        else if (!string.IsNullOrWhiteSpace(badgeReqInput))
                        {
                            Console.WriteLine($"[!] Invalid input. Using default badge requirement ({requiredBadges}).");
                        }

                        bool hadFailures = await _actionExecutor.VerifyAccountStatusOnSelectedAsync(requiredFriends, requiredBadges);

                        if (hadFailures)
                        {
                            Console.Write($"\n[?] Verification complete. Select accounts that failed the check? (y/n): ");
                            string? selectChoice = Console.ReadLine()?.ToLower();
                            if (selectChoice == "y")
                            {
                                _accountManager.SelectFailedVerification();
                                Console.WriteLine("[*] Failed accounts selected.");
                            }
                        }
                        break;
                    case "8": await _actionExecutor.ExecuteAllAutoAsync(); break;
                    case "0": back = true; break;
                    default: ConsoleUI.WriteErrorLine("Invalid choice."); break;
                }
                if (!back) await Task.Delay(500);
            }
        }

        public static void AdjustRateLimitsUI()
        {
            Console.WriteLine($"\n---[ Adjust Rate Limits ]---");
            Console.WriteLine($"Setting delays too low increases risk of rate limiting or account flags.");
            Console.WriteLine($"Minimum allowed delay is {AppConfig.MinAllowedDelayMs}ms.");

            Console.WriteLine($"\n1. General API Delay (Used between non-friend actions):");
            Console.WriteLine($"   Current: {AppConfig.CurrentApiDelayMs}ms / Default: {AppConfig.DefaultApiDelayMs}ms / Safe Min: {AppConfig.SafeApiDelayMs}ms");
            Console.Write($"[?] Enter new delay in ms (or leave blank to keep current): ");
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
            Console.WriteLine($"   Current: {AppConfig.CurrentFriendActionDelayMs}ms / Default: {AppConfig.DefaultFriendActionDelayMs}ms / Safe Min: {AppConfig.SafeFriendActionDelayMs}ms");
            Console.Write($"[?] Enter new delay in ms (or leave blank to keep current): ");
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
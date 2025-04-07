using System.Diagnostics;
using System.Web;
using System.Text;
using System.ComponentModel;
using Continuance.Models;
using Continuance.Roblox.Services;
using Continuance.UI;


namespace Continuance.Roblox.Automation
{
    public class GameLauncher(AuthenticationService authService, BadgeService badgeService)
    {
        private readonly AuthenticationService _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        private readonly BadgeService _badgeService = badgeService ?? throw new ArgumentNullException(nameof(badgeService));

        public async Task<bool> LaunchGameForBadgesAsync(Account account, string gameId, int badgeGoal)
        {
            if (string.IsNullOrWhiteSpace(account.Cookie))
            {
                ConsoleUI.WriteErrorLine($"   Cannot GetBadges for {account.Username}: Missing Cookie.");
                return false;
            }
            if (badgeGoal <= 0)
            {
                ConsoleUI.WriteInfoLine($"   Skipping game launch for {account.Username}: Badge goal is zero or negative.");
                return true;
            }
            if (string.IsNullOrWhiteSpace(gameId))
            {
                ConsoleUI.WriteErrorLine($"   Skipping game launch for {account.Username}: No Game ID provided.");
                return false;
            }

            ConsoleUI.WriteInfoLine($"   Refreshing XCSRF for {account.Username} before getting auth ticket...");
            bool tokenRefreshed = await _authService.RefreshXCSRFTokenIfNeededAsync(account);
            if (!tokenRefreshed || string.IsNullOrEmpty(account.XcsrfToken))
            {
                ConsoleUI.WriteErrorLine($"   Failed to refresh XCSRF token for {account.Username}. Cannot proceed with game launch.");
                return false;
            }

            ConsoleUI.WriteInfoLine($"   Action: GetBadges Target: {account.Username} Game: {gameId} (Goal: {badgeGoal})");

            if (!Environment.UserInteractive)
            {
                ConsoleUI.WriteErrorLine($"   Skipping GetBadges action in non-interactive environment.");
                return false;
            }

            string? authTicket = await _authService.GetAuthenticationTicketAsync(account, gameId);

            if (string.IsNullOrEmpty(authTicket))
            {
                ConsoleUI.WriteErrorLine($"   Auth ticket is missing or invalid. Cannot launch game.");
                return false;
            }

            long browserTrackerId = Random.Shared.NextInt64(10_000_000_000L, 100_000_000_000L);
            long launchTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string placeLauncherUrl = $"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGame&browserTrackerId={browserTrackerId}&placeId={gameId}&isPlayTogetherGame=false&joinAttemptId={Guid.NewGuid()}&joinAttemptOrigin=PlayButton";
            string encodedPlaceLauncherUrl = HttpUtility.UrlEncode(placeLauncherUrl);

            var launchUrlBuilder = new StringBuilder("roblox-player:1");
            launchUrlBuilder.Append("+launchmode:play");
            launchUrlBuilder.Append("+gameinfo:").Append(authTicket);
            launchUrlBuilder.Append("+launchtime:").Append(launchTime);
            launchUrlBuilder.Append("+placelauncherurl:").Append(encodedPlaceLauncherUrl);
            launchUrlBuilder.Append("+browsertrackerid:").Append(browserTrackerId);
            launchUrlBuilder.Append("+robloxLocale:en_us");
            launchUrlBuilder.Append("+gameLocale:en_us");
            string launchUrl = launchUrlBuilder.ToString();
            bool launchCommandSent;
            try
            {
                ConsoleUI.WriteInfoLine($"   Dispatching launch command for Roblox Player...");
                Console.WriteLine($"   URL: {ConsoleUI.Truncate(launchUrl, 150)}");
                Process.Start(new ProcessStartInfo(launchUrl) { UseShellExecute = true });
                ConsoleUI.WriteSuccessLine($"   Launch command sent. The Roblox Player should start shortly.");
                ConsoleUI.WriteWarningLine($"   Please complete any actions required in the game to earn badges (aiming for {badgeGoal}).");
                launchCommandSent = true;
                await Task.Delay(2000);
            }
            catch (Win32Exception ex)
            {
                ConsoleUI.WriteErrorLine($"   Failed to launch Roblox Player. Is Roblox installed and the protocol handler registered?");
                ConsoleUI.WriteErrorLine($"   Error: {ex.Message} (Code: {ex.NativeErrorCode})");
                if (ex.NativeErrorCode == 2)
                    ConsoleUI.WriteWarningLine($"   Hint: Try reinstalling Roblox or running it once manually.");
                return false;
            }
            catch (Exception ex)
            {
                ConsoleUI.WriteErrorLine($"   An unexpected error occurred launching Roblox Player for {account.Username}.");
                ConsoleUI.WriteErrorLine($"   Error: {ex.Message}");
                return false;
            }

            await _badgeService.MonitorBadgeAcquisitionAsync(account, badgeGoal);
            await TerminateRobloxProcessesAsync(account);

            ConsoleUI.WriteInfoLine($"   GetBadges action sequence finished monitoring/termination for {account.Username}.");
            return launchCommandSent;
        }

        private static async Task TerminateRobloxProcessesAsync(Account account)
        {
            if (!Environment.UserInteractive)
            {
                ConsoleUI.WriteInfoLine("   Skipping automatic termination of Roblox processes in non-interactive mode.");
                return;
            }

            ConsoleUI.WriteInfoLine($"   Attempting automatic termination of Roblox Player instances...");
            int closedCount = 0;
            try
            {
                string[] processNames = { "RobloxPlayerBeta", "RobloxPlayerLauncher", "RobloxPlayer" };
                List<Process> robloxProcesses = [];

                foreach (var name in processNames)
                {
                    try { robloxProcesses.AddRange(Process.GetProcessesByName(name)); }
                    catch (InvalidOperationException) { }
                    catch (Exception ex) { ConsoleUI.WriteErrorLine($"   Error getting process list for '{name}': {ex.Message}"); }
                }

                robloxProcesses = robloxProcesses.Where(p =>
                {
                    try { return !p.HasExited; }
                    catch { return false; }
                }).ToList();


                if (robloxProcesses.Count == 0) { Console.WriteLine($"   [-] No active Roblox Player processes found to terminate."); }
                else
                {
                    Console.WriteLine($"   [>] Found {robloxProcesses.Count} potential Roblox process(es). Attempting to close...");
                    foreach (var process in robloxProcesses)
                    {
                        try
                        {
                            if (!process.HasExited)
                            {
                                Console.Write($"      Killing {process.ProcessName} (PID: {process.Id})...");
                                process.Kill(entireProcessTree: true);

                                if (await Task.Run(() => process.WaitForExit(2000)))
                                {
                                    Console.WriteLine($" Terminated.");
                                    closedCount++;
                                }
                                else
                                {
                                    try { if (process.HasExited) { Console.WriteLine($" Terminated (late)."); closedCount++; } else { Console.WriteLine($" Still running?"); } } catch { Console.WriteLine(" Status Unknown."); }
                                }
                            }
                        }
                        catch (InvalidOperationException ex) { Console.WriteLine($" Error: {ex.Message} (Process already exited?)"); }
                        catch (Win32Exception ex) { Console.WriteLine($" Error: {ex.Message} (Access Denied? Permissions issue?)"); }
                        catch (NotSupportedException) { Console.WriteLine($" Error: Killing process tree not supported on this platform/process?"); }
                        catch (Exception ex) { Console.WriteLine($" Error interacting with process: {ex.Message}"); }
                        finally
                        {
                            process.Dispose();
                        }
                    }
                    ConsoleUI.WriteInfoLine($"   Attempted termination for {closedCount} process(es).");
                }
            }
            catch (Exception ex) { ConsoleUI.WriteErrorLine($"   Error finding/killing Roblox processes: {ex.Message}"); }
        }
    }
}
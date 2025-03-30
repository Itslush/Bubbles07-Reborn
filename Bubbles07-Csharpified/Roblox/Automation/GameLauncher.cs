using _Csharpified.Roblox.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using _Csharpified.Models;

namespace
    _Csharpified.Roblox.Automation
{
    public class GameLauncher
    {
        private readonly AuthenticationService _authService;
        private readonly BadgeService _badgeService;

        public GameLauncher(AuthenticationService authService, BadgeService badgeService)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _badgeService = badgeService ?? throw new ArgumentNullException(nameof(badgeService));
        }

        private static string TruncateForLog(string? value, int maxLength = 100)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
        }

        public async Task LaunchGameForBadgesAsync(Account account, string gameId)
        {
            if (string.IsNullOrEmpty(account.XcsrfToken) || string.IsNullOrWhiteSpace(account.Cookie))
            {
                Console.WriteLine($"[-] Cannot GetBadges for {account.Username}: Missing XCSRF token or Cookie.");
                return;
            }
            Console.WriteLine($"[*] Action: GetBadges Target: {account.Username} Game: {gameId}");

            if (!Environment.UserInteractive)
            {
                Console.WriteLine($"[!] Skipping GetBadges action in non-interactive environment.");
                return;
            }

            string? authTicket = await _authService.GetAuthenticationTicketAsync(account, gameId);

            if (string.IsNullOrEmpty(authTicket))
            {
                Console.WriteLine($"[-] Auth ticket is missing or invalid. Cannot launch game.");
                return;
            }

            long browserTrackerId = Random.Shared.NextInt64(10_000_000_000L, 100_000_000_000L);
            long launchTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string placeLauncherUrl = $"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGame&browserTrackerId={browserTrackerId}&placeId={gameId}&isPlayTogetherGame=false&joinAttemptId={Guid.NewGuid()}&joinAttemptOrigin=PlayButton";
            string encodedPlaceLauncherUrl = HttpUtility.UrlEncode(placeLauncherUrl);
            string launchUrl = $"roblox-player:1+launchmode:play+gameinfo:{authTicket}+launchtime:{launchTime}+placelauncherurl:{encodedPlaceLauncherUrl}+browsertrackerid:{browserTrackerId}+robloxLocale:en_us+gameLocale:en_us";

            try
            {
                Console.WriteLine($"[*] Dispatching launch command for Roblox Player...");
                Console.WriteLine($"   URL: {TruncateForLog(launchUrl, 150)}");
                Process.Start(new ProcessStartInfo(launchUrl) { UseShellExecute = true });
                Console.WriteLine($"[+] Launch command sent. The Roblox Player should start shortly.");
                Console.WriteLine($"[!] Please complete any actions required in the game to earn badges.");
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                Console.WriteLine($"[!] Failed to launch Roblox Player. Is Roblox installed and the protocol handler registered?");
                Console.WriteLine($"[!] Error: {ex.Message} (Code: {ex.NativeErrorCode})");
                if (ex.NativeErrorCode == 2)
                    Console.WriteLine($"[?] Hint: Try reinstalling Roblox or running it once manually.");
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] An unexpected error occurred launching Roblox Player for {account.Username}.");
                Console.WriteLine($"[!] Error: {ex.Message}");
                return;
            }

            await _badgeService.MonitorBadgeAcquisitionAsync(account); // Delegate monitoring

            await TerminateRobloxProcessesAsync(account);

            Console.WriteLine($"[*] GetBadges action sequence finished for {account.Username}.");
        }

        private async Task TerminateRobloxProcessesAsync(Account account)
        {
            Console.WriteLine($"[*] Attempting automatic termination of Roblox Player instances...");
            int closedCount = 0;
            try
            {
                string[] processNames = { "RobloxPlayerBeta", "RobloxPlayerLauncher", "RobloxPlayer" };
                List<Process> robloxProcesses = new List<Process>();
                foreach (var name in processNames)
                { try { robloxProcesses.AddRange(Process.GetProcessesByName(name)); } catch { } }

                robloxProcesses = robloxProcesses.Where(p => { try { return !p.HasExited; } catch { return false; } }).ToList();

                if (robloxProcesses.Count == 0) { Console.WriteLine($"[-] No active Roblox Player processes found to terminate."); }
                else
                {
                    Console.WriteLine($"[>] Found {robloxProcesses.Count} potential Roblox process(es). Attempting to close...");
                    foreach (var process in robloxProcesses)
                    {
                        try
                        {
                            if (!process.HasExited)
                            {
                                Console.Write($"   Killing {process.ProcessName} (PID: {process.Id})...");
                                process.Kill(entireProcessTree: true);
                                if (await Task.Run(() => process.WaitForExit(2000))) { Console.WriteLine($" Terminated."); closedCount++; }
                                else { Console.WriteLine($" Still running?"); }
                            }
                            process.Dispose();
                        }
                        catch (InvalidOperationException) { process.Dispose(); }
                        catch (System.ComponentModel.Win32Exception ex) { Console.WriteLine($" Error: {ex.Message} (Access Denied? Running as Admin?)"); process.Dispose(); }
                        catch (Exception ex) { Console.WriteLine($" Error interacting with process: {ex.Message}"); process.Dispose(); }
                    }
                    Console.WriteLine($"[*] Attempted termination for {closedCount} process(es).");
                }
            }
            catch (Exception ex) { Console.WriteLine($"[!] Error finding/killing Roblox processes: {ex.Message}"); }
        }
    }
}
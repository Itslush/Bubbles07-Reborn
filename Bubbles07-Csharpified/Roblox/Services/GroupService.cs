using System.Text;
using Continuance.Models;
using Continuance.Roblox.Http;
using Continuance.UI;

namespace Continuance.Roblox.Services
{
    public class GroupService(RobloxHttpClient robloxHttpClient)
    {
        private readonly RobloxHttpClient _robloxHttpClient = robloxHttpClient ?? throw new ArgumentNullException(nameof(robloxHttpClient));

        public async Task<bool> JoinGroupAsync(Account account, long groupId)
        {
            ConsoleUI.WriteWarningLine($"[OBSOLETE] Attempting API JoinGroup for {account.Username} Group {groupId}. This is likely to fail due to CAPTCHA.");
            ConsoleUI.WriteWarningLine($"            Consider using the interactive Join Group action (Menu Option 3).");
            await Task.Delay(2000);

            if (account == null) { ConsoleUI.WriteErrorLine($"Cannot JoinGroup: Account is null."); return false; }
            if (string.IsNullOrEmpty(account.XcsrfToken))
            {
                ConsoleUI.WriteWarningLine($"Cannot JoinGroup for {account.Username}: Missing XCSRF token (though CAPTCHA is the main issue).");
            }
            if (groupId <= 0) { ConsoleUI.WriteErrorLine($"Cannot JoinGroup for {account.Username}: Invalid Group ID ({groupId})."); return false; }

            ConsoleUI.WriteInfoLine($"Action: JoinGroup (API - Likely Obsolete) Target: {account.Username} Group: {groupId}");

            string url = $"{AppConfig.RobloxApiBaseUrl_Groups}/v1/groups/{groupId}/users";
            var content = new StringContent("{}", Encoding.UTF8, "application/json");

            var (statusCode, success, responseContent) = await RobloxHttpClient.SendRequest(
                HttpMethod.Post,
                url,
                account,
                content,
                $"Join Group {groupId} (API)",
                allowRetryOnXcsrf: true
                );

            if (!success && responseContent != null && responseContent.Contains("Captcha", StringComparison.OrdinalIgnoreCase))
            {
                ConsoleUI.WriteErrorLine($"   -> Join Group API Failed as expected: CAPTCHA required. Response: {ConsoleUI.Truncate(responseContent)}");
                return false;
            }
            else if (!success)
            {
                ConsoleUI.WriteErrorLine($"   -> Join Group API Failed. Status: {statusCode}. Response: {ConsoleUI.Truncate(responseContent)}");
            }

            return success;
        }
    }
}
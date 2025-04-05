using System.Text;
using _Csharpified;
using Models;
using Roblox.Http;
using UI;

namespace Roblox.Services
{
    public class GroupService(RobloxHttpClient robloxHttpClient)
    {
        private readonly RobloxHttpClient _robloxHttpClient = robloxHttpClient ?? throw new ArgumentNullException(nameof(robloxHttpClient));

        public async Task<bool> JoinGroupAsync(Account account, long groupId)
        {
            if (account == null) { ConsoleUI.WriteErrorLine($"Cannot JoinGroup: Account is null."); return false; }
            if (string.IsNullOrEmpty(account.XcsrfToken))
            {
                ConsoleUI.WriteErrorLine($"Cannot JoinGroup for {account.Username}: Missing XCSRF token.");
                return false;
            }
            if (groupId <= 0) { ConsoleUI.WriteErrorLine($"Cannot JoinGroup for {account.Username}: Invalid Group ID ({groupId})."); return false; }

            ConsoleUI.WriteInfoLine($"Action: JoinGroup Target: {account.Username} Group: {groupId}");

            string url = $"{AppConfig.RobloxApiBaseUrl_Groups}/v1/groups/{groupId}/users";
            var content = new StringContent("{}", Encoding.UTF8, "application/json");

            bool success = await _robloxHttpClient.SendRequestAsync(
                HttpMethod.Post,
                url,
                account,
                content,
                $"Join Group {groupId}",
                allowRetryOnXcsrf: true
                );

            return success;
        }
    }
}
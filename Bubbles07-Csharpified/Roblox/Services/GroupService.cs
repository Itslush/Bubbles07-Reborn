using _Csharpified;
using Models;
using Roblox.Http;
namespace Roblox.Services
{
    public class GroupService
    {
        private readonly RobloxHttpClient _robloxHttpClient;

        public GroupService(RobloxHttpClient robloxHttpClient)
        {
            _robloxHttpClient = robloxHttpClient ?? throw new ArgumentNullException(nameof(robloxHttpClient));
        }

        public async Task<bool> JoinGroupAsync(Account account, long groupId)
        {
            if (string.IsNullOrEmpty(account.XcsrfToken))
            {
                Console.WriteLine($"[-] Cannot JoinGroup for {account.Username}: Missing XCSRF token.");
                return false;
            }
            Console.WriteLine($"[*] Action: JoinGroup Target: {account.Username} Group: {groupId}");
            string url = $"https://groups.roblox.com/v1/groups/{groupId}/users";
            var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

            bool success = await _robloxHttpClient.SendRequestAsync(
                HttpMethod.Post, url, account, content, $"Join Group {groupId}"
                );

            await Task.Delay(AppConfig.CurrentApiDelayMs);
            return success;
        }
    }
}
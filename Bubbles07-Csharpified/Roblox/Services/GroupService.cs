using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
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
            if (account == null) { Console.WriteLine($"[-] Cannot JoinGroup: Account is null."); return false; }
            if (string.IsNullOrEmpty(account.XcsrfToken))
            {
                Console.WriteLine($"[-] Cannot JoinGroup for {account.Username}: Missing XCSRF token.");
                return false;
            }
            if (groupId <= 0) { Console.WriteLine($"[-] Cannot JoinGroup for {account.Username}: Invalid Group ID ({groupId})."); return false; }

            Console.WriteLine($"[*] Action: JoinGroup Target: {account.Username} Group: {groupId}");

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

            if (success)
            {
            }

            return success;
        }
    }
}
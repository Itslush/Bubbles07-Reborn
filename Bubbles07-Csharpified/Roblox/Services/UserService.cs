using _Csharpified.Roblox.Http;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using _Csharpified.Models;
namespace
    _Csharpified.Roblox.Services
{
    public class UserService
    {
        private readonly RobloxHttpClient _robloxHttpClient;

        public UserService(RobloxHttpClient robloxHttpClient)
        {
            _robloxHttpClient = robloxHttpClient ?? throw new ArgumentNullException(nameof(robloxHttpClient));
        }

        public async Task<bool> SetDisplayNameAsync(Account account, string newDisplayName)
        {
            if (string.IsNullOrEmpty(account.XcsrfToken))
            {
                Console.WriteLine($"[-] Cannot SetDisplayName for {account.Username}: Missing XCSRF token.");
                return false;
            }
            string url = $"https://users.roblox.com/v1/users/{account.UserId}/display-names";
            var payload = new JObject { ["newDisplayName"] = newDisplayName };
            var content = new StringContent(payload.ToString(), System.Text.Encoding.UTF8, "application/json");

            bool success = await _robloxHttpClient.SendRequestAsync(
                HttpMethod.Patch, url, account, content, $"Set Display Name to '{newDisplayName}'"
                );

            await Task.Delay(AppConfig.CurrentApiDelayMs);
            return success;
        }
    }
}
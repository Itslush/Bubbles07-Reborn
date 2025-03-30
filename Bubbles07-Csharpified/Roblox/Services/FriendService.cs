using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Models;
using Roblox.Http;
using _Csharpified;

namespace Roblox.Services
{
    public class FriendService
    {
        private readonly RobloxHttpClient _robloxHttpClient;

        public FriendService(RobloxHttpClient robloxHttpClient)
        {
            _robloxHttpClient = robloxHttpClient ?? throw new ArgumentNullException(nameof(robloxHttpClient));
        }

        private static string TruncateForLog(string? value, int maxLength = 100)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
        }

        public async Task<bool> SendFriendRequestAsync(Account account, long friendUserId, string friendUsername)
        {
            if (string.IsNullOrEmpty(account.XcsrfToken))
            {
                Console.WriteLine($"[-] Cannot SendFriendRequest from {account.Username}: Missing XCSRF token.");
                return false;
            }
            Console.WriteLine($"[->] Sending Friend Request: {account.Username} -> {friendUsername} ({friendUserId})");
            string url = $"https://friends.roblox.com/v1/users/{friendUserId}/request-friendship";
            var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

            bool success = await _robloxHttpClient.SendRequestAsync(
                HttpMethod.Post, url, account, content, $"Send Friend Request to {friendUsername}"
                );
            return success;
        }

        public async Task<bool> AcceptFriendRequestAsync(Account account, long friendUserId, string friendUsername)
        {
            if (string.IsNullOrEmpty(account.XcsrfToken))
            {
                Console.WriteLine($"[-] Cannot AcceptFriendRequest for {account.Username}: Missing XCSRF token.");
                return false;
            }
            Console.WriteLine($"[<-] Accepting Friend Request: {friendUsername} ({friendUserId}) -> {account.Username}");
            string url = $"https://friends.roblox.com/v1/users/{friendUserId}/accept-friend-request";
            var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

            bool success = await _robloxHttpClient.SendRequestAsync(
                HttpMethod.Post, url, account, content, $"Accept Friend Request from {friendUsername}"
                );
            return success;
        }

        public async Task<int> GetFriendCountAsync(Account account)
        {
            string url = $"https://friends.roblox.com/v1/users/{account.UserId}/friends/count";

            var (success, content) = await _robloxHttpClient.SendRequestAndReadAsync(
                HttpMethod.Get, url, account, null, "Get Friend Count", allowRetryOnXcsrf: false
                );

            if (success)
            {
                try
                {
                    var json = JObject.Parse(content);
                    int count = json["count"]?.Value<int>() ?? -1;
                    if (count != -1) { return count; }
                    else { Console.WriteLine($"[-] Could not parse friend count from response for {account.Username}: {TruncateForLog(content)}"); }
                }
                catch (JsonReaderException jex) { Console.WriteLine($"[-] Error parsing friend count JSON for {account.Username}: {jex.Message}"); }
                catch (Exception ex) { Console.WriteLine($"[-] Error processing friend count for {account.Username}: {ex.Message}"); }
            }
            await Task.Delay(AppConfig.CurrentApiDelayMs / 2);
            return -1;
        }
    }
}
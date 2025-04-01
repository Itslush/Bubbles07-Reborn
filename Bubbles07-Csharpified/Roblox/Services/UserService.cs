using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Models;
using Roblox.Http;
using _Csharpified;

namespace Roblox.Services
{
    public class UserService
    {
        private readonly RobloxHttpClient _robloxHttpClient;

        public UserService(RobloxHttpClient robloxHttpClient)
        {
            _robloxHttpClient = robloxHttpClient ?? throw new ArgumentNullException(nameof(robloxHttpClient));
        }

        private static string TruncateForLog(string? value, int maxLength = 50)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= maxLength ? value[..maxLength] + "..." : value;
        }

        public async Task<bool> SetDisplayNameAsync(Account account, string newDisplayName)
        {
            if (string.IsNullOrEmpty(account.XcsrfToken))
            {
                Console.WriteLine($"[-] Cannot SetDisplayName for {account.Username}: Missing XCSRF token.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(newDisplayName) || newDisplayName.Length < 3 || newDisplayName.Length > 20)
            {
                Console.WriteLine($"[-] Cannot SetDisplayName for {account.Username}: Invalid name '{newDisplayName}'. Must be 3-20 characters.");
                return false;
            }

            string url = $"{AppConfig.RobloxApiBaseUrl_Users}/v1/users/{account.UserId}/display-names";
            var payload = new JObject { ["newDisplayName"] = newDisplayName };
            var content = new StringContent(payload.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

            bool success = await _robloxHttpClient.SendRequestAsync(
                HttpMethod.Patch,
                url,
                account,
                content,
                $"Set Display Name to '{newDisplayName}'"
                );

            return success;
        }

        public async Task<(string? DisplayName, string? Username)> GetUsernamesAsync(Account account)
        {
            string url = $"{AppConfig.RobloxApiBaseUrl_Users}/v1/users/{account.UserId}";

            var (success, content) = await _robloxHttpClient.SendRequestAndReadAsync(
                HttpMethod.Get,
                url,
                account,
                null,
                $"Get User Info for {account.UserId}",
                allowRetryOnXcsrf: false
            );

            if (success && !string.IsNullOrEmpty(content))
            {
                try
                {
                    var json = JObject.Parse(content);
                    string? displayName = json["displayName"]?.Value<string>();
                    string? username = json["name"]?.Value<string>();

                    if (username != null && account.Username != username && account.Username != "N/A")
                    {
                        Console.WriteLine($"[*] Updated username cache for ID {account.UserId} from '{account.Username}' to '{username}'.");
                    }
                    if (username != null) account.Username = username;

                    if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(username))
                    {
                        Console.WriteLine($"[-] Warning: Fetched user info for {account.UserId} has missing/empty name or displayName.");
                        return (displayName, username);
                    }

                    return (displayName, username);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[-] Error parsing user info JSON for {account.UserId}: {ex.Message}");
                }
            }
            return (null, null);
        }

        public async Task<string?> GetCurrentDisplayNameAsync(Account account)
        {
            var (displayName, _) = await GetUsernamesAsync(account);
            return displayName;
        }
    }
}
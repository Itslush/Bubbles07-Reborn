using Newtonsoft.Json.Linq;
using Models;
using Roblox.Http;
using _Csharpified;
using System.Text;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using UI;
using Newtonsoft.Json;
using System.Net;

namespace Roblox.Services
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
            if (account == null) { Console.WriteLine($"[-] Cannot SetDisplayName: Account is null."); return false; }
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
                $"Set Display Name to '{newDisplayName}'",
                allowRetryOnXcsrf: true
                );

            return success;
        }

        public async Task<(string? DisplayName, string? Username)> GetUsernamesAsync(Account account)
        {
            if (account == null) { Console.WriteLine($"[-] Cannot GetUsernames: Account is null."); return (null, null); }
            if (account.UserId <= 0) { Console.WriteLine($"[-] Cannot GetUsernames: Invalid User ID ({account.UserId})."); return (null, null); }

            string url = $"{AppConfig.RobloxApiBaseUrl_Users}/v1/users/{account.UserId}";

            var (_, success, content) = await _robloxHttpClient.SendRequestAndReadAsync(
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

                    if (!string.IsNullOrWhiteSpace(username) && account.Username != username && account.Username != "N/A")
                    {
                        Console.WriteLine($"[*] Updated username cache for ID {account.UserId} from '{account.Username}' to '{username}'.");
                        account.Username = username;
                    }
                    else if (!string.IsNullOrWhiteSpace(username) && account.Username == "N/A")
                    {
                        account.Username = username;
                    }

                    if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(username))
                    {
                        Console.WriteLine($"[-] Warning: Fetched user info for {account.UserId} has missing/empty name or displayName. Display: '{displayName ?? "null"}', User: '{username ?? "null"}'");
                    }

                    return (displayName, username);
                }
                catch (JsonReaderException jex)
                {
                    Console.WriteLine($"[-] Error parsing user info JSON for {account.UserId}: {jex.Message}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[-] Error processing user info for {account.UserId}: {ex.Message}");
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
using Newtonsoft.Json.Linq;
using System.Text;
using Newtonsoft.Json;
using Continuance;
using Continuance.Models;
using Continuance.Roblox.Http;
using Continuance.UI;

namespace Continuance.Roblox.Services
{
    public class UserService(RobloxHttpClient robloxHttpClient)
    {
        private readonly RobloxHttpClient _robloxHttpClient = robloxHttpClient ?? throw new ArgumentNullException(nameof(robloxHttpClient));

        public async Task<bool> SetDisplayNameAsync(Account account, string newDisplayName)
        {
            if (account == null) { ConsoleUI.WriteErrorLine($"Cannot SetDisplayName: Account is null."); return false; }
            if (string.IsNullOrEmpty(account.XcsrfToken))
            {
                ConsoleUI.WriteWarningLine($"Cannot SetDisplayName for {account.Username}: Missing XCSRF token.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(newDisplayName) || newDisplayName.Length < 3 || newDisplayName.Length > 20)
            {
                ConsoleUI.WriteErrorLine($"Cannot SetDisplayName for {account.Username}: Invalid name '{newDisplayName}'. Must be 3-20 characters, non-empty.");
                return false;
            }

            string url = $"{AppConfig.RobloxApiBaseUrl_Users}/v1/users/{account.UserId}/display-names";
            var payload = new JObject { ["newDisplayName"] = newDisplayName };
            var content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");

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
            if (account == null) { ConsoleUI.WriteErrorLine($"Cannot GetUsernames: Account is null."); return (null, null); }
            if (account.UserId <= 0) { ConsoleUI.WriteErrorLine($"Cannot GetUsernames: Invalid User ID ({account.UserId})."); return (null, null); }

            string url = $"{AppConfig.RobloxApiBaseUrl_Users}/v1/users/{account.UserId}";

            var (statusCode, success, content) = await _robloxHttpClient.SendRequestAndReadAsync(
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
                        ConsoleUI.WriteInfoLine($"Updated username cache for ID {account.UserId} from '{account.Username}' to '{username}'.");
                        account.Username = username;
                    }
                    else if (!string.IsNullOrWhiteSpace(username) && account.Username == "N/A")
                    {
                        account.Username = username;
                    }

                    if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(username))
                    {
                        ConsoleUI.WriteWarningLine($"Fetched user info for {account.UserId} has missing/empty name or displayName. Display: '{displayName ?? "null"}', User: '{username ?? "null"}'");
                    }

                    return (displayName, username);
                }
                catch (JsonReaderException jex)
                {
                    ConsoleUI.WriteErrorLine($"Error parsing user info JSON for {account.UserId}: {jex.Message}");
                }
                catch (Exception ex)
                {
                    ConsoleUI.WriteErrorLine($"Error processing user info for {account.UserId}: {ex.Message}");
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
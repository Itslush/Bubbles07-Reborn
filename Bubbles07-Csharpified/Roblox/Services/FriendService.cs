using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
            maxLength = Math.Max(0, maxLength);
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
            string url = $"{AppConfig.RobloxApiBaseUrl_Friends}/v1/users/{friendUserId}/request-friendship";
            var content = new StringContent("{}", Encoding.UTF8, "application/json");
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
            string url = $"{AppConfig.RobloxApiBaseUrl_Friends}/v1/users/{friendUserId}/accept-friend-request";
            var content = new StringContent("{}", Encoding.UTF8, "application/json");
            bool success = await _robloxHttpClient.SendRequestAsync(
                HttpMethod.Post, url, account, content, $"Accept Friend Request from {friendUsername}");
            return success;
        }

        public async Task<int> GetFriendCountAsync(Account account)
        {
            string url = $"{AppConfig.RobloxApiBaseUrl_Friends}/v1/users/{account.UserId}/friends/count";
            var (success, content) = await _robloxHttpClient.SendRequestAndReadAsync(
                HttpMethod.Get, url, account, null, "Get Friend Count", allowRetryOnXcsrf: false
                );

            if (success && !string.IsNullOrEmpty(content))
            {
                try
                {
                    var json = JObject.Parse(content);
                    if (json.TryGetValue("count", StringComparison.OrdinalIgnoreCase, out JToken? countToken) && countToken.Type == JTokenType.Integer)
                    {
                        return countToken.Value<int>();
                    }
                    else { Console.WriteLine($"[-] Could not parse friend count (missing/invalid 'count' property) from response for {account.Username}: {TruncateForLog(content)}"); }
                }
                catch (JsonReaderException jex) { Console.WriteLine($"[-] Error parsing friend count JSON for {account.Username}: {jex.Message}"); }
                catch (Exception ex) { Console.WriteLine($"[-] Error processing friend count response for {account.Username}: {ex.Message}"); }
            }
            return -1;
        }

        public async Task<List<long>> GetPendingFriendRequestSendersAsync(Account account, int limit = 100)
        {
            var senderIds = new List<long>();
            limit = Math.Clamp(limit, 1, 100);
            string url = $"{AppConfig.RobloxApiBaseUrl_Friends}/v1/my/friend-requests?limit={limit}&sortOrder=Desc";

            var (success, content) = await _robloxHttpClient.SendRequestAndReadAsync(
                HttpMethod.Get,
                url,
                account,
                null,
                $"Get Pending Friend Requests for {account.Username}",
                allowRetryOnXcsrf: true
            );

            if (success && !string.IsNullOrEmpty(content))
            {
                try
                {
                    var json = JObject.Parse(content);
                    if (json["data"] is JArray dataArray)
                    {
                        foreach (var req in dataArray)
                        {
                            long senderId = req?["requester"]?["id"]?.Value<long>() ?? 0;
                            if (senderId <= 0)
                            {
                                senderId = req?["id"]?.Value<long>() ?? 0;
                            }
                            if (senderId <= 0)
                            {
                                senderId = req?["senderId"]?.Value<long>() ?? 0;
                            }

                            if (senderId > 0)
                            {
                                senderIds.Add(senderId);
                            }
                            else
                            {
                                string reqString = req?.ToString(Formatting.None) ?? "[null request object]";
                                Console.WriteLine($"[!] Warning: Could not extract sender ID from pending request object for {account.Username}: {reqString}");
                            }
                        }
                        return senderIds;
                    }
                    else { Console.WriteLine($"[-] Could not parse pending requests (missing 'data' array) for {account.Username}."); }
                }
                catch (JsonReaderException jex) { Console.WriteLine($"[-] Error parsing pending requests JSON for {account.Username}: {jex.Message}"); }
                catch (Exception ex) { Console.WriteLine($"[-] Error processing pending requests for {account.Username}: {ex.Message}"); }
            }
            return senderIds;
        }
    }
}
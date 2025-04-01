using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Models;
using Roblox.Http;
using _Csharpified;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using UI;
using System.Net;

namespace Roblox.Services
{
    public class FriendService
    {
        private readonly RobloxHttpClient _robloxHttpClient;

        public FriendService(RobloxHttpClient robloxHttpClient)
        {
            _robloxHttpClient = robloxHttpClient ?? throw new ArgumentNullException(nameof(robloxHttpClient));
        }

        public async Task<(bool Success, bool IsPendingOrFriends, string FailureReason)> SendFriendRequestAsync(Account account, long friendUserId, string friendUsername)
        {
            if (account == null) return (false, false, "Account is null.");
            if (string.IsNullOrEmpty(account.XcsrfToken)) return (false, false, "Missing XCSRF token.");
            if (friendUserId <= 0) return (false, false, $"Invalid friend User ID ({friendUserId}).");
            if (account.UserId == friendUserId) return (false, false, "Cannot friend yourself.");

            string url = $"{AppConfig.RobloxApiBaseUrl_Friends}/v1/users/{friendUserId}/request-friendship";
            var content = new StringContent("{}", Encoding.UTF8, "application/json");

            var (statusCode, isSuccess, responseContent) = await _robloxHttpClient.SendRequestAndReadAsync(
                HttpMethod.Post,
                url,
                account,
                content,
                $"Send Friend Request to {friendUsername} ({friendUserId})",
                allowRetryOnXcsrf: true
            );

            if (isSuccess)
            {
                return (true, false, string.Empty);
            }
            else
            {
                if (statusCode == HttpStatusCode.BadRequest && !string.IsNullOrEmpty(responseContent))
                {
                    try
                    {
                        var errorJson = JObject.Parse(responseContent);
                        if (errorJson["errors"] is JArray errors &&
                            errors.Any(err => err["code"]?.Value<int>() == 5))
                        {
                            Console.WriteLine($"    -> Send Fail (Code 5: Request likely pending or already friends with {friendUsername}).");
                            return (false, true, "Request pending or already friends (Code 5).");
                        }
                    }
                    catch (JsonException) { }
                }
                return (false, false, $"API Error: {statusCode?.ToString() ?? "Unknown"} - {responseContent}");
            }
        }

        public async Task<bool> AcceptFriendRequestAsync(Account account, long friendUserId, string friendUsername)
        {
            if (account == null) { Console.WriteLine($"[-] Cannot AcceptFriendRequest: Account is null."); return false; }
            if (string.IsNullOrEmpty(account.XcsrfToken))
            {
                Console.WriteLine($"[-] Cannot AcceptFriendRequest for {account.Username}: Missing XCSRF token.");
                return false;
            }
            if (friendUserId <= 0) { Console.WriteLine($"[-] Cannot AcceptFriendRequest for {account.Username}: Invalid friend User ID ({friendUserId})."); return false; }
            if (account.UserId == friendUserId) { Console.WriteLine($"[-] Cannot AcceptFriendRequest for {account.Username}: Cannot accept request from yourself."); return false; }


            string url = $"{AppConfig.RobloxApiBaseUrl_Friends}/v1/users/{friendUserId}/accept-friend-request";
            var content = new StringContent("{}", Encoding.UTF8, "application/json");

            bool success = await _robloxHttpClient.SendRequestAsync(
                HttpMethod.Post,
                url,
                account,
                content,
                $"Accept Friend Request from {friendUsername} ({friendUserId})",
                allowRetryOnXcsrf: true
                );

            return success;
        }

        public async Task<int> GetFriendCountAsync(Account account)
        {
            if (account == null) { Console.WriteLine($"[-] Cannot GetFriendCount: Account is null."); return -1; }
            if (account.UserId <= 0) { Console.WriteLine($"[-] Cannot GetFriendCount: Invalid User ID ({account.UserId}) in Account object."); return -1; }

            string url = $"{AppConfig.RobloxApiBaseUrl_Friends}/v1/users/{account.UserId}/friends/count";

            var (statusCode, success, content) = await _robloxHttpClient.SendRequestAndReadAsync(
                HttpMethod.Get,
                url,
                account,
                null,
                "Get Friend Count",
                allowRetryOnXcsrf: false
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
                    else
                    {
                        Console.WriteLine($"[-] Could not parse friend count (missing/invalid 'count' property) from response for {account.Username}: {ConsoleUI.Truncate(content)}");
                    }
                }
                catch (JsonReaderException jex)
                {
                    Console.WriteLine($"[-] Error parsing friend count JSON for {account.Username}: {jex.Message}");
                }
                catch (Exception ex) { Console.WriteLine($"[-] Error processing friend count response for {account.Username}: {ex.Message}"); }
            }
            return -1;
        }
    }
}
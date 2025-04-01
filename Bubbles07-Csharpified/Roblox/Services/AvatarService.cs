using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Models;
using Roblox.Http;
using _Csharpified;

namespace Roblox.Services
{
    public class AvatarService
    {
        private readonly RobloxHttpClient _robloxHttpClient;

        public AvatarService(RobloxHttpClient robloxHttpClient)
        {
            _robloxHttpClient = robloxHttpClient ?? throw new ArgumentNullException(nameof(robloxHttpClient));
        }

        private static string TruncateForLog(string? value, int maxLength = 100)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= maxLength ? value.Substring(0, maxLength) + "..." : value;
        }

        public async Task<AvatarDetails?> FetchAvatarDetailsAsync(long userId)
        {
            string avatarUrl = $"{AppConfig.RobloxApiBaseUrl_Avatar}/v1/users/{userId}/avatar";
            JObject? avatarData = null;
            try
            {
                var externalClient = _robloxHttpClient.GetExternalHttpClient();

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(AppConfig.DefaultRequestTimeoutSec));
                HttpResponseMessage response = await externalClient.GetAsync(avatarUrl, cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    string jsonString = await response.Content.ReadAsStringAsync();
                    avatarData = JObject.Parse(jsonString);
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        Console.WriteLine($"[-] Failed to fetch avatar details for {userId}: 404 Not Found (User may not exist?).");
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                    {
                        Console.WriteLine($"[-] Failed to fetch avatar details for {userId}: 400 Bad Request. Details: {TruncateForLog(errorContent)}");
                    }
                    else
                    {
                        Console.WriteLine($"[-] Failed to fetch avatar details for {userId}: {response.StatusCode}. Details: {TruncateForLog(errorContent)}");
                    }
                    return null;
                }
            }
            catch (OperationCanceledException) { Console.WriteLine($"[!] Timeout fetching avatar details for {userId}."); return null; }
            catch (JsonReaderException jex) { Console.WriteLine($"[!] Error parsing avatar details JSON for {userId}: {jex.Message}."); return null; }
            catch (HttpRequestException hrex) { Console.WriteLine($"[!] Network error fetching avatar details for {userId}: {hrex.Message}"); return null; }
            catch (Exception ex) { Console.WriteLine($"[!] Exception fetching avatar details for {userId}: {ex.GetType().Name} - {ex.Message}"); return null; }

            if (avatarData == null) return null;

            try
            {
                var details = new AvatarDetails
                {
                    AssetIds = avatarData["assets"]?
                                .Select(a => a["id"]?.Value<long>() ?? 0)
                                .Where(id => id != 0)
                                .OrderBy(id => id)
                                .ToList() ?? new List<long>(),
                    BodyColors = avatarData["bodyColors"] as JObject,
                    PlayerAvatarType = avatarData["playerAvatarType"]?.ToString(),
                    Scales = avatarData["scales"] as JObject,
                    FetchTime = DateTime.UtcNow
                };

                if (details.BodyColors == null || details.PlayerAvatarType == null || details.Scales == null || details.AssetIds == null)
                {
                    Console.WriteLine($"[!] Warning: Fetched avatar data for {userId} missing required fields (bodyColors, playerAvatarType, scales, assets).");
                    return null;
                }

                return details;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] Error creating AvatarDetails object from JSON for {userId}: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> SetAvatarAsync(Account account, long sourceUserId)
        {
            if (string.IsNullOrEmpty(account.XcsrfToken))
            {
                Console.WriteLine($"[-] Cannot SetAvatar for {account.Username}: Missing XCSRF token.");
                return false;
            }
            Console.WriteLine($"[*] Action: SetAvatar Source: {sourceUserId} Target: {account.Username}");

            AvatarDetails? targetAvatarDetails = await FetchAvatarDetailsAsync(sourceUserId);

            if (targetAvatarDetails == null)
            {
                Console.WriteLine($"[-] Failed to get source avatar details from {sourceUserId}. Cannot proceed.");
                return false;
            }

            if (targetAvatarDetails.AssetIds == null || targetAvatarDetails.BodyColors == null || targetAvatarDetails.PlayerAvatarType == null || targetAvatarDetails.Scales == null)
            {
                Console.WriteLine($"[-] Source avatar data from {sourceUserId} is incomplete. Cannot apply.");
                return false;
            }

            var wearPayload = new JObject { ["assetIds"] = new JArray(targetAvatarDetails.AssetIds) };
            var bodyColorsPayload = targetAvatarDetails.BodyColors;
            var avatarTypePayload = new JObject { ["playerAvatarType"] = targetAvatarDetails.PlayerAvatarType };
            var scalesPayload = targetAvatarDetails.Scales;

            Console.WriteLine($"[>] Applying avatar configuration to {account.Username}...");
            bool overallSuccess = true;

            async Task<bool> ExecuteAvatarStep(Func<Task<bool>> stepAction, string description)
            {
                bool success = false;
                try
                {
                    success = await stepAction();
                    if (!success) Console.WriteLine($"   [-] Step Failed: {description} for {account.Username}.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   [-] ERROR applying {description} for {account.Username}: {ex.GetType().Name}");
                    success = false;
                }

                overallSuccess &= success;

                if (success)
                {
                    await Task.Delay(AppConfig.CurrentApiDelayMs / 2);
                }
                return success;
            }

            if (!await ExecuteAvatarStep(async () =>
            {
                string url = $"{AppConfig.RobloxApiBaseUrl_Avatar}/v1/avatar/set-body-colors";
                var content = new StringContent(bodyColorsPayload.ToString(Formatting.None), Encoding.UTF8, "application/json");
                return await _robloxHttpClient.SendRequestAsync(HttpMethod.Post, url, account, content, "Set Body Colors");
            }, "Body Colors")) return false;

            if (!await ExecuteAvatarStep(async () =>
            {
                string url = $"{AppConfig.RobloxApiBaseUrl_Avatar}/v1/avatar/set-player-avatar-type";
                var content = new StringContent(avatarTypePayload.ToString(Formatting.None), Encoding.UTF8, "application/json");
                return await _robloxHttpClient.SendRequestAsync(HttpMethod.Post, url, account, content, "Set Avatar Type");
            }, "Avatar Type")) return false;

            if (!await ExecuteAvatarStep(async () =>
            {
                string url = $"{AppConfig.RobloxApiBaseUrl_Avatar}/v1/avatar/set-scales";
                var content = new StringContent(scalesPayload.ToString(Formatting.None), Encoding.UTF8, "application/json");
                return await _robloxHttpClient.SendRequestAsync(HttpMethod.Post, url, account, content, "Set Scales");
            }, "Scales")) return false;

            if (!await ExecuteAvatarStep(async () =>
            {
                string url = $"{AppConfig.RobloxApiBaseUrl_Avatar}/v1/avatar/set-wearing-assets";
                var content = new StringContent(wearPayload.ToString(Formatting.None), Encoding.UTF8, "application/json");
                return await _robloxHttpClient.SendRequestAsync(HttpMethod.Post, url, account, content, "Set Wearing Assets");
            }, "Wearing Assets")) return false;

            if (!await ExecuteAvatarStep(async () =>
            {
                string url = $"{AppConfig.RobloxApiBaseUrl_Avatar}/v1/avatar/redraw-thumbnail";
                var content = new StringContent("{}", Encoding.UTF8, "application/json");
                return await _robloxHttpClient.SendRequestAsync(HttpMethod.Post, url, account, content, "Redraw Thumbnail (Final Step)");
            }, "Redraw Thumbnail"))
            {
                Console.WriteLine($"[*] Warning: Avatar set, but final thumbnail redraw failed for {account.Username}.");
                overallSuccess = true;
            }

            return overallSuccess;
        }

        public bool CompareAvatarDetails(AvatarDetails? details1, AvatarDetails? details2)
        {
            if (ReferenceEquals(details1, details2)) return true;
            if (details1 == null || details2 == null) return false;

            if (!string.Equals(details1.PlayerAvatarType, details2.PlayerAvatarType, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Debug: Avatar type mismatch ('{details1.PlayerAvatarType}' vs '{details2.PlayerAvatarType}')");
                return false;
            }

            if (details1.BodyColors == null || details2.BodyColors == null) return false;
            if (!JToken.DeepEquals(details1.BodyColors, details2.BodyColors))
            {
                Console.WriteLine("Debug: Body colors mismatch.");
                Console.WriteLine($"   D1: {details1.BodyColors.ToString(Formatting.None)}");
                Console.WriteLine($"   D2: {details2.BodyColors.ToString(Formatting.None)}");
                return false;
            }

            if (details1.Scales == null || details2.Scales == null) return false;
            if (!JToken.DeepEquals(details1.Scales, details2.Scales))
            {
                Console.WriteLine("Debug: Scales mismatch.");
                Console.WriteLine($"   D1: {details1.Scales.ToString(Formatting.None)}");
                Console.WriteLine($"   D2: {details2.Scales.ToString(Formatting.None)}");
                return false;
            }

            var assets1 = details1.AssetIds;
            var assets2 = details2.AssetIds;

            if (assets1 == null || assets2 == null) return false;

            if (!assets1.SequenceEqual(assets2))
            {
                Console.WriteLine($"Debug: Asset IDs mismatch (Count {assets1.Count} vs {assets2.Count}).");
                Console.WriteLine($"   D1: [{string.Join(",", assets1)}]");
                Console.WriteLine($"   D2: [{string.Join(",", assets2)}]");
                return false;
            }

            return true;
        }
    }
}
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Text;
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
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
        }

        public async Task<bool> SetAvatarAsync(Account account, long sourceUserId)
        {
            if (string.IsNullOrEmpty(account.XcsrfToken))
            {
                Console.WriteLine($"[-] Cannot SetAvatar for {account.Username}: Missing XCSRF token.");
                return false;
            }
            Console.WriteLine($"[*] Action: SetAvatar Source: {sourceUserId} Target: {account.Username}");

            string targetAvatarUrl = $"https://avatar.roblox.com/v1/users/{sourceUserId}/avatar";
            JObject? targetAvatarData = null;
            try
            {
                Console.WriteLine($"[>] Fetching source avatar model...");
                using var externalClient = _robloxHttpClient.GetExternalHttpClient();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                HttpResponseMessage response = await externalClient.GetAsync(targetAvatarUrl, cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    string jsonString = await response.Content.ReadAsStringAsync();
                    targetAvatarData = JObject.Parse(jsonString);
                    Console.WriteLine($"[+] Source avatar data acquired.");
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[-] Failed to fetch source avatar: {response.StatusCode}. Details: {TruncateForLog(errorContent)}");
                    return false;
                }
            }
            catch (OperationCanceledException) { Console.WriteLine($"[!] Timeout fetching source avatar."); return false; }
            catch (JsonReaderException jex) { Console.WriteLine($"[!] Error parsing source avatar JSON: {jex.Message}."); return false; }
            catch (Exception ex) { Console.WriteLine($"[!] Exception fetching source avatar: {ex.Message}"); return false; }

            if (targetAvatarData == null) { Console.WriteLine($"[-] Failed to process source avatar JSON."); return false; }

            JArray? assets = targetAvatarData["assets"] as JArray;
            JObject? bodyColors = targetAvatarData["bodyColors"] as JObject;
            string? playerAvatarType = targetAvatarData["playerAvatarType"]?.ToString();
            JObject? scales = targetAvatarData["scales"] as JObject;

            if (assets == null || bodyColors == null || playerAvatarType == null || scales == null)
            {
                Console.WriteLine($"[-] Source avatar data missing required fields (assets, bodyColors, playerAvatarType, scales).");
                return false;
            }

            List<long> assetIds = assets.Select(a => a["id"]?.Value<long>() ?? 0).Where(id => id != 0).ToList();
            var wearPayload = new JObject { ["assetIds"] = new JArray(assetIds) };

            Console.WriteLine($"[>] Applying avatar configuration to {account.Username}...");
            bool overallSuccess = true;

            async Task<bool> ExecuteAvatarStep(Func<Task<bool>> step, string description)
            {
                bool success = await step();
                overallSuccess &= success;
                if (success) { await Task.Delay(AppConfig.CurrentApiDelayMs); }
                else { Console.WriteLine($"[-] Step Failed: {description}. Aborting further avatar steps for {account.Username}."); }
                return success;
            }

            if (!await ExecuteAvatarStep(async () =>
            {
                string url = "https://avatar.roblox.com/v1/avatar/set-body-colors";
                var content = new StringContent(bodyColors.ToString(), Encoding.UTF8, "application/json");
                return await _robloxHttpClient.SendRequestAsync(HttpMethod.Post, url, account, content, "Set Body Colors");
            }, "Set Body Colors")) return false;

            if (!await ExecuteAvatarStep(async () =>
            {
                string url = "https://avatar.roblox.com/v1/avatar/set-player-avatar-type";
                var payloadJson = new JObject { ["playerAvatarType"] = playerAvatarType };
                var content = new StringContent(payloadJson.ToString(), Encoding.UTF8, "application/json");
                return await _robloxHttpClient.SendRequestAsync(HttpMethod.Post, url, account, content, "Set Avatar Type");
            }, "Set Avatar Type")) return false;

            if (!await ExecuteAvatarStep(async () =>
            {
                string url = "https://avatar.roblox.com/v1/avatar/set-scales";
                var content = new StringContent(scales.ToString(), Encoding.UTF8, "application/json");
                return await _robloxHttpClient.SendRequestAsync(HttpMethod.Post, url, account, content, "Set Scales");
            }, "Set Scales")) return false;

            if (!await ExecuteAvatarStep(async () =>
            {
                string url = "https://avatar.roblox.com/v1/avatar/set-wearing-assets";
                var content = new StringContent(wearPayload.ToString(), Encoding.UTF8, "application/json");
                return await _robloxHttpClient.SendRequestAsync(HttpMethod.Post, url, account, content, "Set Wearing Assets");
            }, "Set Wearing Assets")) return false;

            Console.WriteLine(overallSuccess
                ? $"[*] Avatar copy process completed successfully for {account.Username}."
                : $"[-] Avatar copy process encountered errors for {account.Username}.");

            return overallSuccess;
        }
    }
}
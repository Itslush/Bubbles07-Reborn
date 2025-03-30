using Newtonsoft.Json.Linq;
using Models;
using _Csharpified;

namespace Roblox.Http
{
    public class RobloxHttpClient
    {
        private static readonly HttpClient httpClient = new HttpClient(new HttpClientHandler { UseCookies = false });
        private const int XcsrfRetryDelayMs = 1000;

        private static string Truncate(string? value, int maxLength = 100)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
        }

        private HttpRequestMessage CreateBaseRequest(HttpMethod method, string url, Account account, HttpContent? content = null)
        {
            var request = new HttpRequestMessage(method, url);
            if (!string.IsNullOrEmpty(account.Cookie))
            {
                request.Headers.Add("Cookie", $".ROBLOSECURITY={account.Cookie}");
            }
            if (!string.IsNullOrEmpty(account.XcsrfToken))
            {
                request.Headers.Add("X-CSRF-TOKEN", account.XcsrfToken);
            }
            request.Content = content;
            return request;
        }

        public async Task<(bool Success, string Content)> SendRequestAndReadAsync(
            HttpMethod method,
            string url,
            Account account,
            HttpContent? content = null,
            string actionDescription = "API request",
            bool allowRetryOnXcsrf = true,
            Action<HttpRequestMessage>? configureRequest = null)
        {
            HttpRequestMessage request = CreateBaseRequest(method, url, account, content);
            configureRequest?.Invoke(request);

            bool retried = false;
        retry_request:
            try
            {
                if (!string.IsNullOrEmpty(account.XcsrfToken))
                {
                    if (request.Headers.Contains("X-CSRF-TOKEN")) request.Headers.Remove("X-CSRF-TOKEN");
                    request.Headers.Add("X-CSRF-TOKEN", account.XcsrfToken);
                }
                else if (allowRetryOnXcsrf)
                {
                    Console.WriteLine($"[!] Warning: Attempting request '{actionDescription}' for {account.Username} with missing XCSRF.");
                }

                Console.WriteLine($"[>] Sending {actionDescription} for {account.Username}...");
                HttpResponseMessage response = await httpClient.SendAsync(request.Clone());
                string responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[-] FAILED: {actionDescription} for {account.Username}. Code: {response.StatusCode}. Data: {Truncate(responseContent)}");

                    if (response.StatusCode == System.Net.HttpStatusCode.Forbidden && response.Headers.Contains("X-CSRF-TOKEN") && allowRetryOnXcsrf && !retried)
                    {
                        string? newToken = response.Headers.GetValues("X-CSRF-TOKEN").FirstOrDefault();
                        if (newToken != null && newToken != account.XcsrfToken)
                        {
                            Console.WriteLine($"[!] XCSRF Rotation Detected for {account.Username}. Attempting refresh and retry...");
                            account.XcsrfToken = newToken; // Update the account's token directly
                            retried = true;
                            if (request.Headers.Contains("X-CSRF-TOKEN")) request.Headers.Remove("X-CSRF-TOKEN");
                            request.Headers.Add("X-CSRF-TOKEN", account.XcsrfToken);
                            await Task.Delay(XcsrfRetryDelayMs);
                            goto retry_request;
                        }
                        else if (newToken == account.XcsrfToken)
                        {
                            Console.WriteLine($"[!] Received 403 Forbidden for {account.Username} but XCSRF token did not change. Not retrying automatically. ({actionDescription})");
                        }
                        else
                        {
                            Console.WriteLine($"[!] Received 403 Forbidden for {account.Username} with missing/empty XCSRF header. Cannot retry. ({actionDescription})");
                        }
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        Console.WriteLine($"[!] RATE LIMITED (429) on '{actionDescription}' for {account.Username}. Consider increasing delays.");
                        await Task.Delay(AppConfig.CurrentFriendActionDelayMs * 2);
                    }

                    return (false, responseContent);
                }
                else
                {
                    Console.WriteLine($"[+] SUCCESS: {actionDescription} for {account.Username}.");
                    return (true, responseContent);
                }
            }
            catch (HttpRequestException hrex)
            {
                Console.WriteLine($"[!] NETWORK EXCEPTION: During '{actionDescription}' for {account.Username}: {hrex.Message} (StatusCode: {hrex.StatusCode})");
                return (false, hrex.Message);
            }
            catch (TaskCanceledException tcex)
            {
                Console.WriteLine($"[!] TIMEOUT/CANCEL EXCEPTION: During '{actionDescription}' for {account.Username}: {tcex.Message}");
                return (false, tcex.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] GENERAL EXCEPTION: During '{actionDescription}' for {account.Username}: {ex.GetType().Name} - {ex.Message}");
                return (false, ex.Message);
            }
        }

        public async Task<bool> SendRequestAsync(
            HttpMethod method,
            string url,
            Account account,
            HttpContent? content = null,
            string actionDescription = "API request",
            bool allowRetryOnXcsrf = true,
            Action<HttpRequestMessage>? configureRequest = null)
        {
            var (success, _) = await SendRequestAndReadAsync(method, url, account, content, actionDescription, allowRetryOnXcsrf, configureRequest);
            return success;
        }

        // Static client for external, unauthenticated requests if needed elsewhere
        private static readonly HttpClient externalHttpClient = new HttpClient();
        public HttpClient GetExternalHttpClient() => externalHttpClient;

        // Specific method for raw cookie validation without an Account object
        public static async Task<(bool IsValid, long UserId, string Username)> ValidateCookieAsync(string cookie)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://users.roblox.com/v1/users/authenticated");
            request.Headers.Add("Cookie", $".ROBLOSECURITY={cookie}");
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                HttpResponseMessage response = await httpClient.SendAsync(request, cts.Token); // Use the shared client
                if (response.IsSuccessStatusCode)
                {
                    string jsonString = await response.Content.ReadAsStringAsync();
                    var accountInfo = JObject.Parse(jsonString);
                    long userId = accountInfo["id"]?.Value<long>() ?? 0;
                    string username = accountInfo["name"]?.Value<string>() ?? "N/A";
                    if (userId > 0 && username != "N/A")
                    {
                        return (true, userId, username);
                    }
                    else
                    {
                        Console.WriteLine($"[!] Validation Error: Parsed user ID or username was invalid from response.");
                        return (false, 0, "N/A");
                    }
                }
                else
                {
                    Console.WriteLine($"[!] Validation Failed: API request returned status {response.StatusCode}.");
                    return (false, 0, "N/A");
                }
            }
            catch (OperationCanceledException) { Console.WriteLine($"[!] Validation Timeout."); }
            catch (Newtonsoft.Json.JsonReaderException jex) { Console.WriteLine($"[!] Validation JSON Parse Error: {jex.Message}"); }
            catch (HttpRequestException hrex) { Console.WriteLine($"[!] Validation Network Error: {hrex.Message}"); }
            catch (Exception ex) { Console.WriteLine($"[!] Validation Exception: {ex.GetType().Name} - {ex.Message}"); }
            return (false, 0, "N/A");
        }

        // Specific method for raw XCSRF fetching without an Account object
        public static async Task<string> FetchXCSRFTokenAsync(string cookie)
        {
            Console.WriteLine($"[*] Attempting XCSRF token acquisition...");
            var request = new HttpRequestMessage(HttpMethod.Post, "https://auth.roblox.com/v2/logout");
            request.Headers.Add("Cookie", $".ROBLOSECURITY={cookie}");
            request.Headers.Add("X-CSRF-TOKEN", "fetch");

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden && response.Headers.Contains("X-CSRF-TOKEN"))
                {
                    string? token = response.Headers.GetValues("X-CSRF-TOKEN").FirstOrDefault();
                    if (!string.IsNullOrEmpty(token)) { Console.WriteLine($"[+] XCSRF acquired via POST /logout."); return token; }
                }
                Console.WriteLine($"[!] POST /logout failed or didn't return token ({response.StatusCode}). Trying POST /birthdate...");

                var infoReq = new HttpRequestMessage(HttpMethod.Post, "https://accountinformation.roblox.com/v1/birthdate");
                infoReq.Headers.Add("Cookie", $".ROBLOSECURITY={cookie}");
                infoReq.Headers.Add("X-CSRF-TOKEN", "fetch");
                infoReq.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

                using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var infoResp = await httpClient.SendAsync(infoReq, HttpCompletionOption.ResponseHeadersRead, cts2.Token);
                if (infoResp.StatusCode == System.Net.HttpStatusCode.Forbidden && infoResp.Headers.Contains("X-CSRF-TOKEN"))
                {
                    string? token = infoResp.Headers.GetValues("X-CSRF-TOKEN").FirstOrDefault();
                    if (!string.IsNullOrEmpty(token)) { Console.WriteLine($"[+] XCSRF acquired via POST /birthdate."); return token; }
                }
                Console.WriteLine($"[!] POST /birthdate failed or didn't return token ({infoResp.StatusCode}). Attempting scrape...");

                var getReq = new HttpRequestMessage(HttpMethod.Get, "https://www.roblox.com/my/account");
                getReq.Headers.Add("Cookie", $".ROBLOSECURITY={cookie}");
                using var cts3 = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var getResp = await httpClient.SendAsync(getReq, cts3.Token);
                if (getResp.IsSuccessStatusCode)
                {
                    string html = await getResp.Content.ReadAsStringAsync();
                    var match = System.Text.RegularExpressions.Regex.Match(html, @"Roblox\.XsrfToken\.setToken\('(.+?)'\)");
                    if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value)) { Console.WriteLine("[+] XCSRF acquired via scrape (Method 1)."); return match.Groups[1].Value; }
                    match = System.Text.RegularExpressions.Regex.Match(html, @"data-csrf-token=""(.+?)""");
                    if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value)) { Console.WriteLine("[+] XCSRF acquired via scrape (Method 2)."); return match.Groups[1].Value; }
                    match = System.Text.RegularExpressions.Regex.Match(html, @"meta name=""csrf-token"" data-token=""(.+?)""");
                    if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value)) { Console.WriteLine("[+] XCSRF acquired via scrape (Method 3)."); return match.Groups[1].Value; }
                    Console.WriteLine($"[!] Scrape successful ({getResp.StatusCode}) but token not found in HTML content.");
                }
                else { Console.WriteLine($"[!] Scrape failed ({getResp.StatusCode})."); }
            }
            catch (OperationCanceledException) { Console.WriteLine($"[!] XCSRF fetch timeout."); }
            catch (HttpRequestException hrex) { Console.WriteLine($"[!] XCSRF fetch network exception: {hrex.Message}"); }
            catch (Exception ex) { Console.WriteLine($"[!] XCSRF fetch exception: {ex.GetType().Name} - {ex.Message}"); }

            Console.WriteLine("[-] Failed to acquire XCSRF Token using all methods.");
            return "";
        }
    }

}
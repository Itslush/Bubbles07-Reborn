using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Models;
using _Csharpified;

namespace Roblox.Http
{
    public class RobloxHttpClient
    {
        private static readonly HttpClient httpClient = new HttpClient(new HttpClientHandler { UseCookies = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate });
        private const int XcsrfRetryDelayMs = AppConfig.XcsrfRetryDelayMs;

        private static readonly HttpClient externalHttpClient = new HttpClient(new HttpClientHandler { UseCookies = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate });
        public HttpClient GetExternalHttpClient() => externalHttpClient;

        private static string Truncate(string? value, int maxLength = 100)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= maxLength ? value.Substring(0, maxLength) + "..." : value;
        }

        private HttpRequestMessage CreateBaseRequest(HttpMethod method, string url, Account account, HttpContent? content = null)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.UserAgent.ParseAdd("Roblox/WinInet");
            request.Headers.Accept.ParseAdd("application/json, text/plain, */*");
            request.Headers.AcceptEncoding.ParseAdd("gzip, deflate");
            request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

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
                if (request.Headers.Contains("X-CSRF-TOKEN")) request.Headers.Remove("X-CSRF-TOKEN");
                if (!string.IsNullOrEmpty(account.XcsrfToken))
                {
                    request.Headers.Add("X-CSRF-TOKEN", account.XcsrfToken);
                }
                else if (allowRetryOnXcsrf && (method == HttpMethod.Post || method == HttpMethod.Patch || method == HttpMethod.Delete))
                {
                    Console.WriteLine($"[!] Warning: Attempting modifying request '{actionDescription}' for {account.Username} with missing XCSRF.");
                }

                using HttpRequestMessage clonedRequest = request.Clone();

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(AppConfig.DefaultRequestTimeoutSec));
                HttpResponseMessage response = await httpClient.SendAsync(clonedRequest, cts.Token);
                string responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[-] FAILED: {actionDescription} for {account.Username}. Code: {(int)response.StatusCode} ({response.ReasonPhrase}). URL: {Truncate(url, 60)}. Data: {Truncate(responseContent)}");

                    if (response.StatusCode == System.Net.HttpStatusCode.Forbidden && response.Headers.Contains("X-CSRF-TOKEN") && allowRetryOnXcsrf && !retried)
                    {
                        string? newToken = response.Headers.GetValues("X-CSRF-TOKEN").FirstOrDefault();
                        if (!string.IsNullOrEmpty(newToken) && newToken != account.XcsrfToken)
                        {
                            Console.WriteLine($"[!] XCSRF Rotation Detected for {account.Username}. Updating token and retrying...");
                            account.XcsrfToken = newToken;
                            await Task.Delay(XcsrfRetryDelayMs);
                            retried = true;
                            goto retry_request;
                        }
                        else if (newToken == account.XcsrfToken)
                        {
                            Console.WriteLine($"[!] Received 403 Forbidden for {account.Username} but XCSRF token in response header did not change. Not retrying automatically. ({actionDescription})");
                        }
                        else
                        {
                            Console.WriteLine($"[!] Received 403 Forbidden for {account.Username} but X-CSRF-TOKEN header was missing or empty in response. Cannot retry based on this. ({actionDescription})");
                        }
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        Console.WriteLine($"[!] RATE LIMITED (429) on '{actionDescription}' for {account.Username}. Consider increasing delays. Failing action.");
                        await Task.Delay(AppConfig.RateLimitRetryDelayMs);
                        return (false, responseContent);
                    }
                    else if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        Console.WriteLine($"[!] UNAUTHORIZED (401) on '{actionDescription}' for {account.Username}. Cookie might be invalid. Marking account invalid.");
                        account.IsValid = false;
                        account.XcsrfToken = "";
                        return (false, responseContent);
                    }

                    return (false, responseContent);
                }
                else
                {
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
                Console.WriteLine($"[!] TIMEOUT/CANCEL EXCEPTION: During '{actionDescription}' for {account.Username} (Timeout: {AppConfig.DefaultRequestTimeoutSec}s): {tcex.Message}");
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

        public static async Task<(bool IsValid, long UserId, string Username)> ValidateCookieAsync(string cookie)
        {
            if (string.IsNullOrWhiteSpace(cookie)) return (false, 0, "N/A");

            var request = new HttpRequestMessage(HttpMethod.Get, AppConfig.RobloxApiBaseUrl_Users + "/v1/users/authenticated");
            request.Headers.Add("Cookie", $".ROBLOSECURITY={cookie}");
            request.Headers.UserAgent.ParseAdd("Roblox/WinInet");

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                HttpResponseMessage response = await httpClient.SendAsync(request, cts.Token);

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
                        Console.WriteLine($"[!] Validation Error: Parsed user ID ({userId}) or username ('{username}') was invalid from response: {Truncate(jsonString)}");
                        return (false, 0, "N/A");
                    }
                }
                else if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    Console.WriteLine($"[!] Validation Failed: API request returned {response.StatusCode} (Unauthorized). Cookie is likely invalid or expired.");
                    return (false, 0, "N/A");
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[!] Validation Failed: API request returned status {(int)response.StatusCode} ({response.ReasonPhrase}). Response: {Truncate(errorContent)}");
                    return (false, 0, "N/A");
                }
            }
            catch (OperationCanceledException) { Console.WriteLine($"[!] Validation Timeout ({TimeSpan.FromSeconds(15).TotalSeconds}s)."); }
            catch (Newtonsoft.Json.JsonReaderException jex) { Console.WriteLine($"[!] Validation JSON Parse Error: {jex.Message}"); }
            catch (HttpRequestException hrex) { Console.WriteLine($"[!] Validation Network Error: {hrex.Message} (StatusCode: {hrex.StatusCode})"); }
            catch (Exception ex) { Console.WriteLine($"[!] Validation Exception: {ex.GetType().Name} - {ex.Message}"); }

            return (false, 0, "N/A");
        }

        public static async Task<string> FetchXCSRFTokenAsync(string cookie)
        {
            if (string.IsNullOrWhiteSpace(cookie)) return "";

            Console.WriteLine($"[*] Attempting XCSRF token acquisition...");

            var logoutReq = new HttpRequestMessage(HttpMethod.Post, AppConfig.RobloxApiBaseUrl_Auth + "/v2/logout");
            logoutReq.Headers.Add("Cookie", $".ROBLOSECURITY={cookie}");
            logoutReq.Headers.Add("X-CSRF-TOKEN", "fetch");
            logoutReq.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            logoutReq.Headers.UserAgent.ParseAdd("Roblox/WinInet");

            try
            {
                using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                HttpResponseMessage response = await httpClient.SendAsync(logoutReq, HttpCompletionOption.ResponseHeadersRead, cts1.Token);

                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden && response.Headers.Contains("X-CSRF-TOKEN"))
                {
                    string? token = response.Headers.GetValues("X-CSRF-TOKEN").FirstOrDefault();
                    if (!string.IsNullOrEmpty(token))
                    {
                        Console.WriteLine($"[+] XCSRF acquired via POST /logout.");
                        return token;
                    }
                }
                Console.WriteLine($"[-] POST /logout failed or didn't return token ({response.StatusCode}). Trying POST /account/settings/birthdate...");

            }
            catch (OperationCanceledException) { Console.WriteLine($"[!] XCSRF fetch (POST /logout) timeout."); }
            catch (HttpRequestException hrex) { Console.WriteLine($"[!] XCSRF fetch (POST /logout) network exception: {hrex.Message}"); }
            catch (Exception ex) { Console.WriteLine($"[!] XCSRF fetch (POST /logout) exception: {ex.GetType().Name} - {ex.Message}"); }

            var bdayReq = new HttpRequestMessage(HttpMethod.Post, AppConfig.RobloxApiBaseUrl + "/account/settings/birthdate");
            bdayReq.Headers.Add("Cookie", $".ROBLOSECURITY={cookie}");
            bdayReq.Headers.Add("X-CSRF-TOKEN", "fetch");
            bdayReq.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            bdayReq.Headers.UserAgent.ParseAdd("Roblox/WinInet");

            try
            {
                using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var bdayResp = await httpClient.SendAsync(bdayReq, HttpCompletionOption.ResponseHeadersRead, cts2.Token);
                if (bdayResp.StatusCode == System.Net.HttpStatusCode.Forbidden && bdayResp.Headers.Contains("X-CSRF-TOKEN"))
                {
                    string? token = bdayResp.Headers.GetValues("X-CSRF-TOKEN").FirstOrDefault();
                    if (!string.IsNullOrEmpty(token))
                    {
                        Console.WriteLine($"[+] XCSRF acquired via POST /account/settings/birthdate.");
                        return token;
                    }
                }
                Console.WriteLine($"[-] POST /account/settings/birthdate failed or didn't return token ({bdayResp.StatusCode}). Trying scrape...");
            }
            catch (OperationCanceledException) { Console.WriteLine($"[!] XCSRF fetch (POST /account/settings/birthdate) timeout."); }
            catch (HttpRequestException hrex) { Console.WriteLine($"[!] XCSRF fetch (POST /account/settings/birthdate) network exception: {hrex.Message}"); }
            catch (Exception ex) { Console.WriteLine($"[!] XCSRF fetch (POST /account/settings/birthdate) exception: {ex.GetType().Name} - {ex.Message}"); }

            var getReq = new HttpRequestMessage(HttpMethod.Get, AppConfig.RobloxWebBaseUrl + "/my/account");
            getReq.Headers.Add("Cookie", $".ROBLOSECURITY={cookie}");
            getReq.Headers.UserAgent.ParseAdd("Roblox/WinInet");

            try
            {
                using var cts3 = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var getResp = await httpClient.SendAsync(getReq, cts3.Token);
                if (getResp.IsSuccessStatusCode)
                {
                    string html = await getResp.Content.ReadAsStringAsync();
                    var match = Regex.Match(html, @"Roblox\.XsrfToken\.setToken\('(.+?)'\)");
                    if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value)) { Console.WriteLine("[+] XCSRF acquired via scrape (Method: JS setToken)."); return match.Groups[1].Value; }

                    match = Regex.Match(html, @"data-csrf-token=""(.+?)""");
                    if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value)) { Console.WriteLine("[+] XCSRF acquired via scrape (Method: data-csrf-token)."); return match.Groups[1].Value; }

                    match = Regex.Match(html, @"meta name=""csrf-token"" data-token=""(.+?)""");
                    if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value)) { Console.WriteLine("[+] XCSRF acquired via scrape (Method: meta tag)."); return match.Groups[1].Value; }

                    Console.WriteLine($"[!] Scrape successful ({getResp.StatusCode}) but token not found in HTML content with known patterns.");
                }
                else { Console.WriteLine($"[!] Scrape failed ({getResp.StatusCode})."); }
            }
            catch (OperationCanceledException) { Console.WriteLine($"[!] XCSRF fetch (Scrape) timeout."); }
            catch (HttpRequestException hrex) { Console.WriteLine($"[!] XCSRF fetch (Scrape) network exception: {hrex.Message}"); }
            catch (RegexMatchTimeoutException) { Console.WriteLine($"[!] XCSRF fetch (Scrape) regex timeout."); }
            catch (Exception ex) { Console.WriteLine($"[!] XCSRF fetch (Scrape) exception: {ex.GetType().Name} - {ex.Message}"); }

            Console.WriteLine("[-] Failed to acquire XCSRF Token using all methods.");
            return "";
        }
    }
}
using Newtonsoft.Json.Linq;
using Models;
using _Csharpified;
using System.Text.RegularExpressions;
using System.Net;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using Roblox.Http;
using UI;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Roblox.Http
{
    public class RobloxHttpClient
    {
        private static readonly HttpClient httpClient = new HttpClient(new HttpClientHandler
        {
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        });
        private const int XcsrfRetryDelayMs = AppConfig.XcsrfRetryDelayMs;

        private static readonly HttpClient externalHttpClient = new HttpClient(new HttpClientHandler
        {
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        });
        public HttpClient GetExternalHttpClient() => externalHttpClient;

        private HttpRequestMessage CreateBaseRequest(HttpMethod method, string url, Account account, HttpContent? content = null)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            {
                Console.WriteLine($"[!] Invalid URL format provided: {url}");
                throw new ArgumentException("Invalid URL format", nameof(url));
            }

            var request = new HttpRequestMessage(method, url);

            request.Headers.UserAgent.ParseAdd("Roblox/WinInet");
            request.Headers.Accept.ParseAdd("application/json, text/plain, */*");
            request.Headers.AcceptEncoding.ParseAdd("gzip, deflate");
            request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

            if (!string.IsNullOrEmpty(account?.Cookie))
            {
                request.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={account.Cookie}");
            }

            if (!string.IsNullOrEmpty(account?.XcsrfToken))
            {
                request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", account.XcsrfToken);
            }

            request.Content = content;
            return request;
        }

        public async Task<(HttpStatusCode? StatusCode, bool IsSuccess, string Content)> SendRequestAndReadAsync(
            HttpMethod method,
            string url,
            Account account,
            HttpContent? content = null,
            string actionDescription = "API request",
            bool allowRetryOnXcsrf = true,
            Action<HttpRequestMessage>? configureRequest = null)
        {
            if (account == null)
            {
                Console.WriteLine($"[!] Cannot send request '{actionDescription}': Account object is null.");
                return (null, false, "Account object was null.");
            }

            HttpRequestMessage request;
            try
            {
                request = CreateBaseRequest(method, url, account, content);
                configureRequest?.Invoke(request);
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"[!] Request Creation Failed for '{actionDescription}': {ex.Message}");
                return (null, false, ex.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] Unexpected Error Creating Request for '{actionDescription}': {ex.Message}");
                return (null, false, $"Unexpected error during request creation: {ex.Message}");
            }

            bool retried = false;
        retry_request:
            HttpResponseMessage? response = null;

            try
            {
                if (request.Headers.Contains("X-CSRF-TOKEN")) request.Headers.Remove("X-CSRF-TOKEN");
                if (!string.IsNullOrEmpty(account.XcsrfToken))
                {
                    request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", account.XcsrfToken);
                }
                else if (allowRetryOnXcsrf && (method == HttpMethod.Post || method == HttpMethod.Patch || method == HttpMethod.Delete))
                {
                    Console.WriteLine($"[!] Warning: Attempting modifying request '{actionDescription}' for {account.Username} with missing XCSRF.");
                }

                using HttpRequestMessage clonedRequest = request.Clone();

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(AppConfig.DefaultRequestTimeoutSec));
                response = await httpClient.SendAsync(clonedRequest, cts.Token);
                string responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[-] FAILED: {actionDescription} for {account.Username}. Code: {(int)response.StatusCode} ({response.ReasonPhrase}). URL: {ConsoleUI.Truncate(url, 60)}. Data: {ConsoleUI.Truncate(responseContent)}");

                    if (response.StatusCode == System.Net.HttpStatusCode.Forbidden &&
                        response.Headers.TryGetValues("X-CSRF-TOKEN", out var csrfHeaderValues) &&
                        allowRetryOnXcsrf && !retried)
                    {
                        string? newToken = csrfHeaderValues?.FirstOrDefault();
                        if (!string.IsNullOrEmpty(newToken) && newToken != account.XcsrfToken)
                        {
                            Console.WriteLine($"[!] XCSRF Rotation Detected for {account.Username}. Updating token and retrying...");
                            account.XcsrfToken = newToken;
                            await Task.Delay(XcsrfRetryDelayMs);
                            retried = true;
                            response?.Dispose();
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
                        return (response.StatusCode, false, responseContent);
                    }
                    else if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        Console.WriteLine($"[!] UNAUTHORIZED (401) on '{actionDescription}' for {account.Username}. Cookie might be invalid. Marking account invalid.");
                        account.IsValid = false;
                        account.XcsrfToken = "";
                        return (response.StatusCode, false, responseContent);
                    }

                    return (response.StatusCode, false, responseContent);
                }
                else
                {
                    return (response.StatusCode, true, responseContent);
                }
            }
            catch (HttpRequestException hrex)
            {
                Console.WriteLine($"[!] NETWORK EXCEPTION: During '{actionDescription}' for {account.Username}: {hrex.Message} (StatusCode: {hrex.StatusCode})");
                return (hrex.StatusCode, false, hrex.Message);
            }
            catch (TaskCanceledException tcex) when (tcex.InnerException is TimeoutException)
            {
                Console.WriteLine($"[!] TIMEOUT EXCEPTION: During '{actionDescription}' for {account.Username} (Timeout: {AppConfig.DefaultRequestTimeoutSec}s): Request timed out.");
                return (HttpStatusCode.RequestTimeout, false, "Request timed out.");
            }
            catch (TaskCanceledException tcex)
            {
                Console.WriteLine($"[!] TASK CANCELED: During '{actionDescription}' for {account.Username}: {tcex.Message}");
                return (HttpStatusCode.RequestTimeout, false, tcex.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] GENERAL EXCEPTION: During '{actionDescription}' for {account.Username}: {ex.GetType().Name} - {ex.Message}");
                return (null, false, ex.Message);
            }
            finally
            {
                response?.Dispose();
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
            var (_, isSuccess, _) = await SendRequestAndReadAsync(method, url, account, content, actionDescription, allowRetryOnXcsrf, configureRequest);
            return isSuccess;
        }

        public static async Task<(bool IsValid, long UserId, string Username)> ValidateCookieAsync(string cookie)
        {
            if (string.IsNullOrWhiteSpace(cookie)) return (false, 0, "N/A");

            string validationUrl = AppConfig.RobloxApiBaseUrl_Users + "/v1/users/authenticated";
            using var request = new HttpRequestMessage(HttpMethod.Get, validationUrl);
            request.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={cookie}");
            request.Headers.UserAgent.ParseAdd("Roblox/WinInet");

            HttpResponseMessage? response = null;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                response = await httpClient.SendAsync(request, cts.Token);

                string jsonString = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    JObject? accountInfo = null;
                    try { accountInfo = JObject.Parse(jsonString); }
                    catch (JsonReaderException jex)
                    {
                        Console.WriteLine($"[!] Validation JSON Parse Error: {jex.Message} Response: {ConsoleUI.Truncate(jsonString)}");
                        return (false, 0, "N/A");
                    }

                    long userId = accountInfo?["id"]?.Value<long>() ?? 0;
                    string? username = accountInfo?["name"]?.Value<string>();

                    if (userId > 0 && !string.IsNullOrWhiteSpace(username))
                    {
                        return (true, userId, username);
                    }
                    else
                    {
                        Console.WriteLine($"[!] Validation Error: Parsed user ID ({userId}) or username ('{username ?? "null"}') was invalid from response: {ConsoleUI.Truncate(jsonString)}");
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
                    Console.WriteLine($"[!] Validation Failed: API request returned status {(int)response.StatusCode} ({response.ReasonPhrase}). Response: {ConsoleUI.Truncate(jsonString)}");
                    return (false, 0, "N/A");
                }
            }
            catch (OperationCanceledException) { Console.WriteLine($"[!] Validation Timeout ({TimeSpan.FromSeconds(15).TotalSeconds}s)."); }
            catch (HttpRequestException hrex) { Console.WriteLine($"[!] Validation Network Error: {hrex.Message} (StatusCode: {hrex.StatusCode})"); }
            catch (Exception ex) { Console.WriteLine($"[!] Validation Exception: {ex.GetType().Name} - {ex.Message}"); }
            finally { response?.Dispose(); }


            return (false, 0, "N/A");
        }

        public static async Task<string> FetchXCSRFTokenAsync(string cookie)
        {
            if (string.IsNullOrWhiteSpace(cookie)) return "";

            Console.WriteLine($"[*] Attempting XCSRF token acquisition...");

            string logoutUrl = AppConfig.RobloxApiBaseUrl_Auth + "/v2/logout";
            using var logoutReq = new HttpRequestMessage(HttpMethod.Post, logoutUrl);
            logoutReq.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={cookie}");
            logoutReq.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", "fetch");
            logoutReq.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            logoutReq.Headers.UserAgent.ParseAdd("Roblox/WinInet");

            HttpResponseMessage? response = null;
            try
            {
                using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                response = await httpClient.SendAsync(logoutReq, HttpCompletionOption.ResponseHeadersRead, cts1.Token);

                if (response.StatusCode == HttpStatusCode.Forbidden && response.Headers.TryGetValues("X-CSRF-TOKEN", out var csrfHeaderValues))
                {
                    string? token = csrfHeaderValues?.FirstOrDefault();
                    if (!string.IsNullOrEmpty(token))
                    {
                        Console.WriteLine($"[+] XCSRF acquired via POST /logout.");
                        return token;
                    }
                }
                Console.WriteLine($"[-] POST /logout failed or didn't return token ({response?.StatusCode ?? HttpStatusCode.Unused}). Trying next method...");
            }
            catch (OperationCanceledException) { Console.WriteLine($"[!] XCSRF fetch (POST /logout) timeout."); }
            catch (HttpRequestException hrex) { Console.WriteLine($"[!] XCSRF fetch (POST /logout) network exception: {hrex.Message}"); }
            catch (Exception ex) { Console.WriteLine($"[!] XCSRF fetch (POST /logout) exception: {ex.GetType().Name} - {ex.Message}"); }
            finally { response?.Dispose(); }

            string bdayUrl = AppConfig.RobloxApiBaseUrl_AccountInfo + "/v1/birthdate";
            using var bdayReq = new HttpRequestMessage(HttpMethod.Post, bdayUrl);
            bdayReq.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={cookie}");
            bdayReq.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", "fetch");
            bdayReq.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            bdayReq.Headers.UserAgent.ParseAdd("Roblox/WinInet");

            response = null;
            try
            {
                using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                response = await httpClient.SendAsync(bdayReq, HttpCompletionOption.ResponseHeadersRead, cts2.Token);
                if (response.StatusCode == HttpStatusCode.Forbidden && response.Headers.TryGetValues("X-CSRF-TOKEN", out var csrfHeaderValues))
                {
                    string? token = csrfHeaderValues?.FirstOrDefault();
                    if (!string.IsNullOrEmpty(token))
                    {
                        Console.WriteLine($"[+] XCSRF acquired via POST {bdayUrl}.");
                        return token;
                    }
                }
                Console.WriteLine($"[-] POST {bdayUrl} failed or didn't return token ({response?.StatusCode ?? HttpStatusCode.Unused}). Trying scrape...");
            }
            catch (OperationCanceledException) { Console.WriteLine($"[!] XCSRF fetch (POST {bdayUrl}) timeout."); }
            catch (HttpRequestException hrex) { Console.WriteLine($"[!] XCSRF fetch (POST {bdayUrl}) network exception: {hrex.Message}"); }
            catch (Exception ex) { Console.WriteLine($"[!] XCSRF fetch (POST {bdayUrl}) exception: {ex.GetType().Name} - {ex.Message}"); }
            finally { response?.Dispose(); }

            string scrapeUrl = AppConfig.RobloxWebBaseUrl + "/my/account";
            using var getReq = new HttpRequestMessage(HttpMethod.Get, scrapeUrl);
            getReq.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={cookie}");
            getReq.Headers.UserAgent.ParseAdd("Roblox/WinInet");

            response = null;
            try
            {
                using var cts3 = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                response = await httpClient.SendAsync(getReq, cts3.Token);
                if (response.IsSuccessStatusCode)
                {
                    string html = await response.Content.ReadAsStringAsync();

                    var patterns = new Dictionary<string, Regex>
                     {
                         { "JS setToken", new Regex(@"Roblox\.XsrfToken\.setToken\('(.+?)'\)", RegexOptions.Compiled | RegexOptions.Singleline, TimeSpan.FromSeconds(1)) },
                         { "data-csrf-token", new Regex(@"data-csrf-token=""(.+?)""", RegexOptions.Compiled | RegexOptions.Singleline, TimeSpan.FromSeconds(1)) },
                         { "meta tag", new Regex(@"meta name=""csrf-token"" data-token=""(.+?)""", RegexOptions.Compiled | RegexOptions.Singleline, TimeSpan.FromSeconds(1)) }
                     };

                    foreach (var kvp in patterns)
                    {
                        try
                        {
                            Match match = kvp.Value.Match(html);
                            if (match.Success && match.Groups.Count > 1 && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
                            {
                                Console.WriteLine($"[+] XCSRF acquired via scrape (Method: {kvp.Key}).");
                                return match.Groups[1].Value;
                            }
                        }
                        catch (RegexMatchTimeoutException) { Console.WriteLine($"[!] XCSRF fetch (Scrape) regex timeout for pattern: {kvp.Key}."); }
                    }

                    Console.WriteLine($"[!] Scrape successful ({response.StatusCode}) but token not found in HTML content with known patterns.");
                }
                else { Console.WriteLine($"[!] Scrape failed ({response.StatusCode})."); }
            }
            catch (OperationCanceledException) { Console.WriteLine($"[!] XCSRF fetch (Scrape) timeout."); }
            catch (HttpRequestException hrex) { Console.WriteLine($"[!] XCSRF fetch (Scrape) network exception: {hrex.Message}"); }
            catch (Exception ex) { Console.WriteLine($"[!] XCSRF fetch (Scrape) exception: {ex.GetType().Name} - {ex.Message}"); }
            finally { response?.Dispose(); }

            Console.WriteLine("[-] Failed to acquire XCSRF Token using all methods.");
            return "";
        }
    }
}
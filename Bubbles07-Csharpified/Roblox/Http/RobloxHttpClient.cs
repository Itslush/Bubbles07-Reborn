using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;
using System.Net;
using Newtonsoft.Json;
using Continuance.Models;
using Continuance.UI;

namespace Continuance.Roblox.Http
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
        public static HttpClient ExternalHttpClient => externalHttpClient;

        private static HttpRequestMessage CreateBaseRequest(HttpMethod method, string url, Account account, HttpContent? content = null)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            {
                ConsoleUI.WriteErrorLine($"Invalid URL format provided: {url}");
                throw new ArgumentException("Invalid URL format", nameof(url));
            }

            var request = new HttpRequestMessage(method, url);

            request.Headers.UserAgent.ParseAdd("Roblox/WinInet");
            request.Headers.Accept.ParseAdd("application/json, text/plain, */*");
            request.Headers.AcceptEncoding.ParseAdd("gzip, deflate");
            request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

            if (account != null)
            {
                if (!string.IsNullOrEmpty(account.Cookie))
                {
                    request.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={account.Cookie}");
                }
            }

            request.Content = content;
            return request;
        }

        public static async Task<(HttpStatusCode? StatusCode, bool IsSuccess, string Content)> SendRequest(
            HttpMethod method,
            string url,
            Account account,
            HttpContent? content = null,
            string actionDescription = "API request",
            bool allowRetryOnXcsrf = true,
            Action<HttpRequestMessage>? configureRequest = null)
        {
            if (account == null && (method == HttpMethod.Post || method == HttpMethod.Patch || method == HttpMethod.Delete || allowRetryOnXcsrf))
            {
                ConsoleUI.WriteErrorLine($"Cannot send modifying request '{actionDescription}': Account object is null.");
                return (null, false, "Account object was null for an authenticated request.");
            }

            HttpRequestMessage request;

            try
            {
                request = CreateBaseRequest(method, url, account!, content);
                configureRequest?.Invoke(request);
            }
            catch (ArgumentException ex)
            {
                ConsoleUI.WriteErrorLine($"Request Creation Failed for '{actionDescription}': {ex.Message}");
                return (null, false, ex.Message);
            }
            catch (Exception ex)
            {
                ConsoleUI.WriteErrorLine($"Unexpected Error Creating Request for '{actionDescription}': {ex.Message}");
                return (null, false, $"Unexpected error during request creation: {ex.Message}");
            }

            bool retried = false;
            retry_request:
            HttpResponseMessage? response = null;

            try
            {
                if (account != null)
                {
                    if (request.Headers.Contains("X-CSRF-TOKEN")) request.Headers.Remove("X-CSRF-TOKEN");

                    if (!string.IsNullOrEmpty(account.XcsrfToken))
                    {
                        request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", account.XcsrfToken);
                    }
                    else if (allowRetryOnXcsrf && (method == HttpMethod.Post || method == HttpMethod.Patch || method == HttpMethod.Delete))
                    {
                        ConsoleUI.WriteWarningLine($"Attempting modifying request '{actionDescription}' for {account.Username} with missing XCSRF.");
                    }
                }

                using HttpRequestMessage clonedRequest = request.Clone();

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(AppConfig.DefaultRequestTimeoutSec));
                response = await httpClient.SendAsync(clonedRequest, cts.Token);
                string responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    string username = account?.Username ?? "N/A";
                    ConsoleUI.WriteErrorLine($"[-] FAILED: {actionDescription} for {username}. Code: {(int)response.StatusCode} ({response.ReasonPhrase}). URL: {ConsoleUI.Truncate(url, 60)}. Data: {ConsoleUI.Truncate(responseContent)}");

                    if (response.StatusCode == HttpStatusCode.Forbidden &&
                        response.Headers.TryGetValues("X-CSRF-TOKEN", out var csrfHeaderValues) &&
                        allowRetryOnXcsrf && !retried && account != null)
                    {
                        string? newToken = csrfHeaderValues?.FirstOrDefault()?.Trim();
                        if (!string.IsNullOrEmpty(newToken) && newToken != account.XcsrfToken)
                        {
                            ConsoleUI.WriteErrorLine($"XCSRF Rotation Detected for {account.Username}. Updating token and retrying...");
                            account.XcsrfToken = newToken;
                            await Task.Delay(XcsrfRetryDelayMs);
                            retried = true;
                            response?.Dispose();
                            goto retry_request;
                        }
                        else if (newToken == account.XcsrfToken)
                        {
                            ConsoleUI.WriteWarningLine($"Received 403 Forbidden for {account.Username} but XCSRF token in response header did not change. Not retrying automatically. ({actionDescription})");
                        }
                        else
                        {
                            ConsoleUI.WriteWarningLine($"Received 403 Forbidden for {account.Username} but X-CSRF-TOKEN header was missing or empty in response. Cannot retry based on this. ({actionDescription})");
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        ConsoleUI.WriteErrorLine($"RATE LIMITED (429) on '{actionDescription}' for {username}. Consider increasing delays. Failing action.");
                        await Task.Delay(AppConfig.RateLimitRetryDelayMs);
                        return (response.StatusCode, false, responseContent);
                    }
                    else if (response.StatusCode == HttpStatusCode.Unauthorized && account != null)
                    {
                        ConsoleUI.WriteErrorLine($"UNAUTHORIZED (401) on '{actionDescription}' for {account.Username}. Cookie might be invalid. Marking account invalid.");
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
                ConsoleUI.WriteErrorLine($"NETWORK EXCEPTION: During '{actionDescription}' for {account?.Username ?? "N/A"}: {hrex.Message} (StatusCode: {hrex.StatusCode})");
                return (hrex.StatusCode, false, hrex.Message);
            }

            catch (TaskCanceledException)
            {
                ConsoleUI.WriteErrorLine($"TIMEOUT/CANCELLED: During '{actionDescription}' for {account?.Username ?? "N/A"} (Timeout: {AppConfig.DefaultRequestTimeoutSec}s): Request cancelled or timed out.");
                return (HttpStatusCode.RequestTimeout, false, "Request timed out or was cancelled.");
            }
            catch (Exception ex)
            {
                ConsoleUI.WriteErrorLine($"GENERAL EXCEPTION: During '{actionDescription}' for {account?.Username ?? "N/A"}: {ex.GetType().Name} - {ex.Message}");
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
            var (_, isSuccess, _) = await SendRequest(method, url, account, content, actionDescription, allowRetryOnXcsrf, configureRequest);
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
                        ConsoleUI.WriteErrorLine($"Validation JSON Parse Error: {jex.Message} Response: {ConsoleUI.Truncate(jsonString)}");
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
                        ConsoleUI.WriteErrorLine($"Validation Error: Parsed user ID ({userId}) or username ('{username ?? "null"}') was invalid from response: {ConsoleUI.Truncate(jsonString)}");
                        return (false, 0, "N/A");
                    }
                }
                else if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    ConsoleUI.WriteErrorLine($"Validation Failed: API request returned {response.StatusCode} (Unauthorized). Cookie is likely invalid or expired.");
                    return (false, 0, "N/A");
                }
                else
                {
                    ConsoleUI.WriteErrorLine($"Validation Failed: API request returned status {(int)response.StatusCode} ({response.ReasonPhrase}). Response: {ConsoleUI.Truncate(jsonString)}");
                    return (false, 0, "N/A");
                }
            }
            catch (OperationCanceledException) { ConsoleUI.WriteErrorLine($"Validation Timeout ({TimeSpan.FromSeconds(15).TotalSeconds}s)."); }
            catch (HttpRequestException hrex) { ConsoleUI.WriteErrorLine($"Validation Network Error: {hrex.Message} (StatusCode: {hrex.StatusCode})"); }
            catch (Exception ex) { ConsoleUI.WriteErrorLine($"Validation Exception: {ex.GetType().Name} - {ex.Message}"); }
            finally { response?.Dispose(); }

            return (false, 0, "N/A");
        }

        public static async Task<string> FetchXCSRFTokenAsync(string cookie)
        {
            if (string.IsNullOrWhiteSpace(cookie)) return "";

            ConsoleUI.WriteInfoLine($"Attempting XCSRF token acquisition...");

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
                        ConsoleUI.WriteSuccessLine($"XCSRF acquired via POST /logout.");
                        return token.Trim();
                    }
                }
                ConsoleUI.WriteErrorLine($"POST /logout failed or didn't return token ({response?.StatusCode ?? HttpStatusCode.Unused}). Trying next method...");
            }
            catch (OperationCanceledException) { ConsoleUI.WriteErrorLine($"XCSRF fetch (POST /logout) timeout."); }
            catch (HttpRequestException hrex) { ConsoleUI.WriteErrorLine($"XCSRF fetch (POST /logout) network exception: {hrex.Message}"); }
            catch (Exception ex) { ConsoleUI.WriteErrorLine($"XCSRF fetch (POST /logout) exception: {ex.GetType().Name} - {ex.Message}"); }

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
                        ConsoleUI.WriteSuccessLine($"XCSRF acquired via POST {bdayUrl}.");
                        return token.Trim();
                    }
                }
                ConsoleUI.WriteErrorLine($"POST {bdayUrl} failed or didn't return token ({response?.StatusCode ?? HttpStatusCode.Unused}). Trying scrape...");
            }

            catch (OperationCanceledException) { ConsoleUI.WriteErrorLine($"XCSRF fetch (POST {bdayUrl}) timeout."); }
            catch (HttpRequestException hrex) { ConsoleUI.WriteErrorLine($"XCSRF fetch (POST {bdayUrl}) network exception: {hrex.Message}"); }
            catch (Exception ex) { ConsoleUI.WriteErrorLine($"XCSRF fetch (POST {bdayUrl}) exception: {ex.GetType().Name} - {ex.Message}"); }

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
                         { "JS setToken", new Regex(@"Roblox\.XsrfToken\.setToken\('(.+?)'\)", RegexOptions.Compiled | RegexOptions.Singleline, TimeSpan.FromSeconds(2)) },
                         { "data-csrf-token", new Regex(@"data-csrf-token=[""'](.+?)[""']", RegexOptions.Compiled | RegexOptions.Singleline, TimeSpan.FromSeconds(2)) },
                         { "meta tag", new Regex(@"<meta\s+name=[""']csrf-token[""']\s+data-token=[""'](.+?)[""']", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)) }
                     };

                    foreach (var kvp in patterns)
                    {
                        try
                        {
                            Match match = kvp.Value.Match(html);
                            if (match.Success && match.Groups.Count > 1 && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
                            {
                                ConsoleUI.WriteSuccessLine($"XCSRF acquired via scrape (Method: {kvp.Key}).");
                                return match.Groups[1].Value.Trim();
                            }
                        }
                        catch (RegexMatchTimeoutException) { ConsoleUI.WriteWarningLine($"XCSRF fetch (Scrape) regex timeout for pattern: {kvp.Key}."); }
                    }

                    ConsoleUI.WriteWarningLine($"Scrape successful ({response.StatusCode}) but token not found in HTML content with known patterns.");
                }
                else { ConsoleUI.WriteErrorLine($"Scrape failed ({response.StatusCode})."); }
            }

            catch (OperationCanceledException) { ConsoleUI.WriteErrorLine($"XCSRF fetch (Scrape) timeout."); }
            catch (HttpRequestException hrex) { ConsoleUI.WriteErrorLine($"XCSRF fetch (Scrape) network exception: {hrex.Message}"); }
            catch (Exception ex) { ConsoleUI.WriteErrorLine($"XCSRF fetch (Scrape) exception: {ex.GetType().Name} - {ex.Message}"); }

            finally { response?.Dispose(); }

            ConsoleUI.WriteErrorLine("Failed to acquire XCSRF Token using all methods.");

            return "";
        }
    }
}
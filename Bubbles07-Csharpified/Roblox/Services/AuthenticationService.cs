using System.Net.Http.Headers;
using Models;
using Roblox.Http;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Linq;
using _Csharpified;
using System.Threading;
using UI;
using System.Net;

namespace Roblox.Services
{
    public class AuthenticationService
    {
        private readonly RobloxHttpClient _robloxHttpClient;
        private static readonly HttpClient directHttpClient = new HttpClient(new HttpClientHandler
        {
            UseCookies = false,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        });

        public AuthenticationService(RobloxHttpClient robloxHttpClient)
        {
            _robloxHttpClient = robloxHttpClient ?? throw new ArgumentNullException(nameof(robloxHttpClient));
        }

        public Task<(bool IsValid, long UserId, string Username)> ValidateCookieAsync(string cookie)
        {
            return RobloxHttpClient.ValidateCookieAsync(cookie);
        }

        public Task<string> FetchXCSRFTokenAsync(string cookie)
        {
            return RobloxHttpClient.FetchXCSRFTokenAsync(cookie);
        }

        public async Task<bool> RefreshXCSRFTokenIfNeededAsync(Account account)
        {
            if (account == null)
            {
                Console.WriteLine("[!] Cannot refresh XCSRF: Account object is null.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(account.Cookie))
            {
                Console.WriteLine($"[!] Cannot refresh XCSRF for User ID {account.UserId}: Cookie is missing.");
                account.IsValid = false;
                account.XcsrfToken = "";
                return false;
            }

            string url = $"{AppConfig.RobloxApiBaseUrl_Friends}/v1/users/{account.UserId}/friends/count";

            var (_, success, _) = await _robloxHttpClient.SendRequestAndReadAsync(
                HttpMethod.Get,
                url,
                account,
                null,
                "XCSRF Refresh Check",
                allowRetryOnXcsrf: true
            );

            if (account.IsValid && !string.IsNullOrEmpty(account.XcsrfToken))
            {
                return true;
            }
            else
            {
                Console.WriteLine($"[-] XCSRF token for {account.Username} could not be validated or refreshed via API check.");

                if (account.IsValid && !string.IsNullOrWhiteSpace(account.Cookie))
                {
                    Console.WriteLine($"[*] Account still marked valid, attempting a full XCSRF fetch as a fallback...");
                    string newToken = await FetchXCSRFTokenAsync(account.Cookie);
                    if (!string.IsNullOrEmpty(newToken))
                    {
                        account.XcsrfToken = newToken;
                        account.IsValid = true;
                        Console.WriteLine($"[+] Successfully fetched new XCSRF token for {account.Username} via fallback.");
                        return true;
                    }
                    else
                    {
                        Console.WriteLine($"[-] Fallback XCSRF fetch also failed for {account.Username}. Marking account invalid.");
                        account.IsValid = false;
                        account.XcsrfToken = "";
                        return false;
                    }
                }
                else if (!account.IsValid)
                {
                    Console.WriteLine($"[-] Account was marked invalid (likely 401). Skipping fallback XCSRF fetch.");
                    return false;
                }
                else
                {
                    Console.WriteLine($"[-] Cannot attempt fallback XCSRF fetch as cookie is missing.");
                    account.IsValid = false;
                    return false;
                }
            }
        }

        public async Task<string?> GetAuthenticationTicketAsync(Account account, string gameId)
        {
            if (account == null)
            {
                Console.WriteLine($"[-] Cannot Get Auth Ticket: Account is null.");
                return null;
            }
            if (string.IsNullOrEmpty(account.XcsrfToken) || string.IsNullOrWhiteSpace(account.Cookie))
            {
                Console.WriteLine($"[-] Cannot Get Auth Ticket for {account.Username}: Missing XCSRF token or Cookie.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(gameId) || !long.TryParse(gameId, out _))
            {
                Console.WriteLine($"[-] Cannot Get Auth Ticket for {account.Username}: Invalid Game ID '{gameId}'.");
                return null;
            }

            string? authTicket = null;
            string authUrl = AppConfig.RobloxApiBaseUrl_Auth + "/v1/authentication-ticket/";
            var authContent = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

            bool retried = false;
        retry_auth_request:
            HttpResponseMessage? rawAuthResponse = null;
            try
            {
                Console.WriteLine($"[>] Requesting game authentication ticket for {account.Username}...");

                using var request = new HttpRequestMessage(HttpMethod.Post, authUrl);

                request.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={account.Cookie}");
                if (!string.IsNullOrEmpty(account.XcsrfToken))
                {
                    if (request.Headers.Contains("X-CSRF-TOKEN")) request.Headers.Remove("X-CSRF-TOKEN");
                    request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", account.XcsrfToken);
                }
                else
                {
                    Console.WriteLine($"[-] Cannot Get Auth Ticket for {account.Username}: XCSRF token became empty before request.");
                    return null;
                }
                request.Headers.UserAgent.ParseAdd("Roblox/WinInet");
                request.Headers.TryAddWithoutValidation("Origin", AppConfig.RobloxWebBaseUrl);
                try
                {
                    request.Headers.Referrer = new Uri($"{AppConfig.RobloxWebBaseUrl}/games/{gameId}/");
                }
                catch (FormatException ex)
                {
                    Console.WriteLine($"[!] Warning: Invalid game ID '{gameId}' for Referrer header: {ex.Message}. Proceeding without Referrer.");
                }
                request.Headers.Accept.ParseAdd("application/json, text/plain, */*");

                request.Content = authContent;

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(AppConfig.DefaultRequestTimeoutSec));
                rawAuthResponse = await directHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                string authResponseContent = "";

                if (rawAuthResponse.IsSuccessStatusCode &&
                    rawAuthResponse.Headers.TryGetValues("RBX-Authentication-Ticket", out var ticketValues))
                {
                    authTicket = ticketValues?.FirstOrDefault();
                    if (!string.IsNullOrEmpty(authTicket))
                    {
                        Console.WriteLine($"[+] Auth Ticket Obtained for {account.Username}.");
                        return authTicket;
                    }
                    else
                    {
                        authResponseContent = await rawAuthResponse.Content.ReadAsStringAsync();
                        Console.WriteLine($"[-] Auth ticket header present but empty for {account.Username}? Status: {rawAuthResponse.StatusCode}. Details: {ConsoleUI.Truncate(authResponseContent)}");
                        return null;
                    }
                }
                else if (rawAuthResponse.StatusCode == System.Net.HttpStatusCode.Forbidden &&
                         rawAuthResponse.Headers.TryGetValues("X-CSRF-TOKEN", out var xcsrfValues) &&
                         !retried)
                {
                    string? newToken = xcsrfValues?.FirstOrDefault();
                    if (!string.IsNullOrEmpty(newToken) && newToken != account.XcsrfToken)
                    {
                        Console.WriteLine($"[!] Auth ticket request (403): XCSRF Rotation Detected for {account.Username}. Updating token and retrying...");
                        account.XcsrfToken = newToken;
                        retried = true;
                        await Task.Delay(AppConfig.XcsrfRetryDelayMs);
                        goto retry_auth_request;
                    }
                    else
                    {
                        authResponseContent = await rawAuthResponse.Content.ReadAsStringAsync();
                        Console.WriteLine($"[!] Auth ticket request (403) for {account.Username}: XCSRF token on response was not new ('{newToken}') or was missing. Cannot retry based on XCSRF. Body: {ConsoleUI.Truncate(authResponseContent)}");
                        return null;
                    }
                }
                else if (rawAuthResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    authResponseContent = await rawAuthResponse.Content.ReadAsStringAsync();
                    Console.WriteLine($"[-] Failed to get auth ticket for {account.Username}: Unauthorized (401). Cookie may be invalid. Marking invalid. Details: {ConsoleUI.Truncate(authResponseContent)}");
                    account.IsValid = false;
                    account.XcsrfToken = "";
                    return null;
                }
                else
                {
                    authResponseContent = await rawAuthResponse.Content.ReadAsStringAsync();
                    Console.WriteLine($"[-] Failed to get auth ticket for {account.Username}. Status: {(int)rawAuthResponse.StatusCode} ({rawAuthResponse.ReasonPhrase}). Details: {ConsoleUI.Truncate(authResponseContent)}");
                    if (authResponseContent.Contains("Captcha", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"[!] CAPTCHA required for auth ticket? Action cannot proceed automatically.");
                    }
                    return null;
                }
            }
            catch (OperationCanceledException) when (retried) { Console.WriteLine($"[!] Timeout getting auth ticket for {account.Username} (on retry)."); return null; }
            catch (OperationCanceledException) { Console.WriteLine($"[!] Timeout getting auth ticket for {account.Username}."); return null; }
            catch (HttpRequestException hrex) { Console.WriteLine($"[!] Network error getting auth ticket for {account.Username}: {hrex.Message}"); return null; }
            catch (UriFormatException ufex) { Console.WriteLine($"[!] URL format error during auth ticket request for {account.Username}: {ufex.Message} (Check GameID/URLs)"); return null; }
            catch (Exception ex) { Console.WriteLine($"[!] Exception getting auth ticket for {account.Username}: {ex.GetType().Name} - {ex.Message}"); return null; }
            finally { rawAuthResponse?.Dispose(); }
        }
    }
}
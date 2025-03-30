using System.Net.Http.Headers;
using Models;
using Roblox.Http;

namespace Roblox.Services
{
    public class AuthenticationService
    {
        private readonly RobloxHttpClient _robloxHttpClient;

        public AuthenticationService(RobloxHttpClient robloxHttpClient)
        {
            _robloxHttpClient = robloxHttpClient ?? throw new ArgumentNullException(nameof(robloxHttpClient));
        }

        public Task<(bool IsValid, long UserId, string Username)> ValidateCookieAsync(string cookie)
        {
            return RobloxHttpClient.ValidateCookieAsync(cookie); // Use static method from HttpClient wrapper
        }

        public Task<string> FetchXCSRFTokenAsync(string cookie)
        {
            return RobloxHttpClient.FetchXCSRFTokenAsync(cookie); // Use static method from HttpClient wrapper
        }

        public async Task<bool> RefreshXCSRFTokenIfNeededAsync(Account account)
        {
            string url = $"https://friends.roblox.com/v1/users/{account.UserId}/friends/count";

            var (success, _) = await _robloxHttpClient.SendRequestAndReadAsync(HttpMethod.Get, url, account, null, "XCSRF Refresh Check", allowRetryOnXcsrf: true);

            if (!string.IsNullOrEmpty(account.XcsrfToken))
            {
                Console.WriteLine($"[*] XCSRF token for {account.Username} appears valid or refreshed.");
                return true;
            }
            else
            {
                Console.WriteLine($"[!] Initial XCSRF check/refresh failed for {account.Username}. Attempting full fetch...");
                string newToken = await FetchXCSRFTokenAsync(account.Cookie);
                if (!string.IsNullOrEmpty(newToken))
                {
                    account.XcsrfToken = newToken;
                    Console.WriteLine($"[+] Successfully fetched new XCSRF token for {account.Username} during pre-check.");
                    return true;
                }
                else
                {
                    Console.WriteLine($"[-] Failed to obtain/refresh XCSRF token for {account.Username} during pre-check. Account might be invalid.");
                    account.IsValid = false;
                    return false;
                }
            }
        }

        public async Task<string?> GetAuthenticationTicketAsync(Account account, string gameId)
        {
            if (string.IsNullOrEmpty(account.XcsrfToken) || string.IsNullOrWhiteSpace(account.Cookie))
            {
                Console.WriteLine($"[-] Cannot Get Auth Ticket for {account.Username}: Missing XCSRF token or Cookie.");
                return null;
            }

            string? authTicket = null;
            try
            {
                Console.WriteLine($"[>] Requesting game authentication ticket...");
                string authUrl = "https://auth.roblox.com/v1/authentication-ticket/";
                var authContent = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

                HttpResponseMessage rawAuthResponse;
                using var request = new HttpRequestMessage(HttpMethod.Post, authUrl);
                request.Headers.Add("Cookie", $".ROBLOSECURITY={account.Cookie}");
                request.Headers.Add("X-CSRF-TOKEN", account.XcsrfToken);
                request.Content = authContent;
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                try { request.Headers.Referrer = new Uri($"https://www.roblox.com/games/{gameId}/"); } catch { }
                request.Headers.TryAddWithoutValidation("Origin", "https://www.roblox.com");

                // Need direct HttpClient access for headers
                using var httpClient = new HttpClient(new HttpClientHandler { UseCookies = false });
                using var cancellationts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                rawAuthResponse = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationts.Token);

                string authResponseContent = "";

                if (rawAuthResponse.IsSuccessStatusCode && rawAuthResponse.Headers.Contains("RBX-Authentication-Ticket"))
                {
                    authTicket = rawAuthResponse.Headers.GetValues("RBX-Authentication-Ticket").FirstOrDefault();
                    if (!string.IsNullOrEmpty(authTicket)) { Console.WriteLine($"[+] Auth Ticket Obtained."); }
                    else
                    {
                        authResponseContent = await rawAuthResponse.Content.ReadAsStringAsync();
                        Console.WriteLine($"[-] Auth ticket header present but empty? Status: {rawAuthResponse.StatusCode}. Details: {TruncateForLog(authResponseContent)}");
                        return null;
                    }
                }
                else if (rawAuthResponse.StatusCode == System.Net.HttpStatusCode.Forbidden && rawAuthResponse.Headers.Contains("X-CSRF-TOKEN"))
                {
                    Console.WriteLine($"[!] Auth ticket request blocked (403 Forbidden). XCSRF might be stale.");
                    string? newToken = rawAuthResponse.Headers.GetValues("X-CSRF-TOKEN").FirstOrDefault();
                    if (newToken != null && newToken != account.XcsrfToken)
                    {
                        Console.WriteLine($"[!] Updating XCSRF token based on 403 response...");
                        account.XcsrfToken = newToken;
                        Console.WriteLine($"[!] XCSRF updated. Please re-run the action.");
                    }
                    else { Console.WriteLine($"[!] XCSRF token on 403 response was not new or was missing. Cannot proceed."); }
                    return null;
                }
                else
                {
                    authResponseContent = await rawAuthResponse.Content.ReadAsStringAsync();
                    Console.WriteLine($"[-] Failed to get auth ticket. Status: {rawAuthResponse.StatusCode}. Details: {TruncateForLog(authResponseContent)}");
                    return null;
                }
            }
            catch (OperationCanceledException) { Console.WriteLine($"[!] Timeout getting auth ticket."); return null; }
            catch (HttpRequestException hrex) { Console.WriteLine($"[!] Network error getting auth ticket: {hrex.Message}"); return null; }
            catch (Exception ex) { Console.WriteLine($"[!] Exception getting auth ticket: {ex.GetType().Name} - {ex.Message}"); return null; }

            return authTicket;
        }

        private static string TruncateForLog(string? value, int maxLength = 100)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= maxLength ? value : value[..maxLength] + "...";
        }
    }
}
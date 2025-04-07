using Continuance;
using Continuance.Models;
using Continuance.Roblox.Http;
using Continuance.UI;

namespace Continuance.Roblox.Services
{
    public class AuthenticationService(RobloxHttpClient robloxHttpClient)
    {
        private readonly RobloxHttpClient _robloxHttpClient = robloxHttpClient ?? throw new ArgumentNullException(nameof(robloxHttpClient));
        private static readonly HttpClient directHttpClient = new HttpClient(new HttpClientHandler
        {
            UseCookies = false,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        });

        public Task<(bool IsValid, long UserId, string Username)> ValidateCookieAsync(string cookie)
        {
            return RobloxHttpClient.ValidateCookieAsync(cookie);
        }

        public static Task<string> FetchXCSRFTokenAsync(string cookie)
        {
            return RobloxHttpClient.FetchXCSRFTokenAsync(cookie);
        }

        public async Task<bool> RefreshXCSRFTokenIfNeededAsync(Account account)
        {
            if (account == null)
            {
                ConsoleUI.WriteErrorLine("Cannot refresh XCSRF: Account object is null.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(account.Cookie))
            {
                ConsoleUI.WriteErrorLine($"Cannot refresh XCSRF for User ID {account.UserId}: Cookie is missing.");
                account.IsValid = false;
                account.XcsrfToken = "";
                return false;
            }

            string url = $"{AppConfig.RobloxApiBaseUrl_Friends}/v1/users/{account.UserId}/friends/count";

            var (_, success, content) = await _robloxHttpClient.SendRequestAndReadAsync(
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
                ConsoleUI.WriteErrorLine($"XCSRF token for {account.Username} could not be validated or refreshed via API check.");

                if (account.IsValid && !string.IsNullOrWhiteSpace(account.Cookie))
                {
                    ConsoleUI.WriteInfoLine($"Account still marked valid, attempting a full XCSRF fetch as a fallback...");
                    string newToken = await FetchXCSRFTokenAsync(account.Cookie);
                    if (!string.IsNullOrEmpty(newToken))
                    {
                        account.XcsrfToken = newToken;
                        account.IsValid = true;
                        ConsoleUI.WriteSuccessLine($"Successfully fetched new XCSRF token for {account.Username} via fallback.");
                        return true;
                    }
                    else
                    {
                        ConsoleUI.WriteErrorLine($"Fallback XCSRF fetch also failed for {account.Username}. Marking account invalid.");
                        account.IsValid = false;
                        account.XcsrfToken = "";
                        return false;
                    }
                }
                else if (!account.IsValid)
                {
                    ConsoleUI.WriteErrorLine($"Account was marked invalid (likely 401). Skipping fallback XCSRF fetch.");
                    return false;
                }
                else
                {
                    ConsoleUI.WriteErrorLine($"Cannot attempt fallback XCSRF fetch as cookie is missing.");
                    account.IsValid = false;
                    return false;
                }
            }
        }

        public async Task<string?> GetAuthenticationTicketAsync(Account account, string gameId)
        {
            if (account == null)
            {
                ConsoleUI.WriteErrorLine($"Cannot Get Auth Ticket: Account is null.");
                return null;
            }
            if (string.IsNullOrEmpty(account.XcsrfToken) || string.IsNullOrWhiteSpace(account.Cookie))
            {
                ConsoleUI.WriteWarningLine($"Cannot Get Auth Ticket for {account.Username}: Missing XCSRF token or Cookie.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(gameId) || !long.TryParse(gameId, out _))
            {
                ConsoleUI.WriteErrorLine($"Cannot Get Auth Ticket for {account.Username}: Invalid Game ID '{gameId}'.");
                return null;
            }

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
                    ConsoleUI.WriteErrorLine($"Cannot Get Auth Ticket for {account.Username}: XCSRF token became empty before request.");
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
                    ConsoleUI.WriteWarningLine($"Invalid game ID '{gameId}' for Referrer header: {ex.Message}. Proceeding without Referrer.");
                }
                request.Headers.Accept.ParseAdd("application/json, text/plain, */*");

                request.Content = authContent;

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(AppConfig.DefaultRequestTimeoutSec));
                rawAuthResponse = await directHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                string authResponseContent = "";

                if (rawAuthResponse.IsSuccessStatusCode &&
                    rawAuthResponse.Headers.TryGetValues("RBX-Authentication-Ticket", out var ticketValues))
                {
                    string? authTicket = ticketValues?.FirstOrDefault();
                    if (!string.IsNullOrEmpty(authTicket))
                    {
                        ConsoleUI.WriteSuccessLine($"Auth Ticket Obtained for {account.Username}.");
                        return authTicket;
                    }
                    else
                    {
                        authResponseContent = await rawAuthResponse.Content.ReadAsStringAsync();
                        ConsoleUI.WriteWarningLine($"Auth ticket header present but empty for {account.Username}? Status: {rawAuthResponse.StatusCode}. Details: {ConsoleUI.Truncate(authResponseContent)}");
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
                        ConsoleUI.WriteErrorLine($"Auth ticket request (403): XCSRF Rotation Detected for {account.Username}. Updating token and retrying...");
                        account.XcsrfToken = newToken;
                        retried = true;
                        await Task.Delay(AppConfig.XcsrfRetryDelayMs);
                        goto retry_auth_request;
                    }
                    else
                    {
                        authResponseContent = await rawAuthResponse.Content.ReadAsStringAsync();
                        ConsoleUI.WriteWarningLine($"Auth ticket request (403) for {account.Username}: XCSRF token on response was not new ('{newToken}') or was missing. Cannot retry based on XCSRF. Body: {ConsoleUI.Truncate(authResponseContent)}");
                        return null;
                    }
                }
                else if (rawAuthResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    authResponseContent = await rawAuthResponse.Content.ReadAsStringAsync();
                    ConsoleUI.WriteErrorLine($"Failed to get auth ticket for {account.Username}: Unauthorized (401). Cookie may be invalid. Marking invalid. Details: {ConsoleUI.Truncate(authResponseContent)}");
                    account.IsValid = false;
                    account.XcsrfToken = "";
                    return null;
                }
                else
                {
                    authResponseContent = await rawAuthResponse.Content.ReadAsStringAsync();
                    ConsoleUI.WriteErrorLine($"Failed to get auth ticket for {account.Username}. Status: {(int)rawAuthResponse.StatusCode} ({rawAuthResponse.ReasonPhrase}). Details: {ConsoleUI.Truncate(authResponseContent)}");
                    if (authResponseContent.Contains("Captcha", StringComparison.OrdinalIgnoreCase))
                    {
                        ConsoleUI.WriteErrorLine($"CAPTCHA required for auth ticket? Action cannot proceed automatically.");
                    }
                    return null;
                }
            }
            catch (OperationCanceledException) when (retried) { ConsoleUI.WriteErrorLine($"Timeout getting auth ticket for {account.Username} (on retry)."); return null; }
            catch (OperationCanceledException) { ConsoleUI.WriteErrorLine($"Timeout getting auth ticket for {account.Username}."); return null; }
            catch (HttpRequestException hrex) { ConsoleUI.WriteErrorLine($"Network error getting auth ticket for {account.Username}: {hrex.Message}"); return null; }
            catch (UriFormatException ufex) { ConsoleUI.WriteErrorLine($"URL format error during auth ticket request for {account.Username}: {ufex.Message} (Check GameID/URLs)"); return null; }
            catch (Exception ex) { ConsoleUI.WriteErrorLine($"Exception getting auth ticket for {account.Username}: {ex.GetType().Name} - {ex.Message}"); return null; }
            finally { rawAuthResponse?.Dispose(); }
        }
    }
}
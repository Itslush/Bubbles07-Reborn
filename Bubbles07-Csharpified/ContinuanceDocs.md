# Continuance Codebase Documentation

## 1. Overview

Continuance is a C# console application designed to automate tasks across multiple Roblox accounts. It allows users to load accounts using their `.ROBLOSECURITY` cookies, manage these accounts (select, view status), and execute various actions such as setting display names, changing avatars, joining groups, acquiring badges via game launch, managing friend requests, and verifying account status against predefined criteria. The application features a menu-driven interface, configurable settings (delays, retries, default values), and basic error handling with retry mechanisms for Roblox API interactions.

### 1.1. Understanding XCSRF Tokens

**What is XCSRF?**

XCSRF (Cross-Site Request Forgery), often written as CSRF, is a type of web security vulnerability. It occurs when a malicious website, email, blog, instant message, or program causes a user's web browser to perform an unwanted action on a trusted site when the user is authenticated.

**How CSRF Protection Works (XCSRF Tokens):**

To prevent CSRF attacks, many web applications use **anti-CSRF tokens**, often referred to as **XCSRF tokens** (especially when passed in HTTP headers like `X-CSRF-TOKEN`). Here's the basic mechanism:

1.  **Token Generation:** When a user logs in or accesses a form, the server generates a unique, secret, unpredictable token associated with the user's session.
2.  **Token Embedding:** This token is embedded within the web page (e.g., in a hidden form field or a JavaScript variable).
3.  **Token Submission:** When the user performs a state-changing action (like submitting a form, changing settings, sending a friend request), the browser must include this token in the request sent back to the server.
4.  **Token Validation:** The server validates that the token submitted with the request matches the token associated with the user's session. If they match, the action is processed. If they don't match, or if the token is missing, the server rejects the request, assuming it might be forged.

**Why Continuance Needs XCSRF Tokens:**

*   **Roblox Protection:** Roblox employs XCSRF protection on its APIs for actions that modify state (e.g., sending friend requests, changing display names, joining groups, setting avatars).
*   **Authentication vs. Authorization:** While the `.ROBLOSECURITY` cookie authenticates the user (proves who they are), the XCSRF token authorizes specific actions initiated *intentionally* from the Roblox website or an application acting on its behalf. It ensures that the request wasn't triggered unknowingly from another site.
*   **Continuance's Role:** Continuance needs to obtain a valid XCSRF token for each account *before* performing modifying actions. It does this primarily by making specific initial requests that trigger Roblox to provide the token in a response header (as seen in `RobloxHttpClient.FetchXCSRFTokenAsync`). It then includes this token in the `X-CSRF-TOKEN` header for subsequent sensitive requests. The code also handles cases where Roblox might invalidate an old token and provide a new one in a 403 Forbidden response, automatically updating the stored token and retrying the request.

## 2. Core Concepts

*   **Account Management:** The central concept revolves around managing `Account` objects, each representing a Roblox account identified primarily by its `.ROBLOSECURITY` cookie.
*   **Actions:** Specific operations that can be performed on selected accounts (e.g., Set Display Name, Join Group).
*   **Services:** Classes dedicated to interacting with specific Roblox API endpoints (e.g., `FriendService`, `AvatarService`).
*   **Configuration:** Application behavior (API delays, retry counts, default action parameters) is controlled via constants and runtime settings managed by `AppConfig` and potentially loaded/saved from a `settings.json` file.
*   **XCSRF Token:** As explained in 1.1, crucial for performing state-changing actions.
*   **Rate Limiting:** The application incorporates delays and retry mechanisms to mitigate hitting Roblox API rate limits.
*   **Automation:** Uses external tools/protocols like Selenium WebDriver and the `roblox-player:` protocol handler.

## 3. Project Structure (Namespaces)

*   **`Continuance` (Root):** Contains the entry point (`Initialize`), core configuration (`AppConfig`), and potentially shared models or utilities.
*   **`Continuance.Models`:** Defines data structures used throughout the application (`Account`, `AppSettings`, `AvatarDetails`, `VerificationStatus`).
*   **`Continuance.Core`:** Contains core logic components like `AccountManager` responsible for state management of accounts.
*   **`Continuance.Roblox.Http`:** Handles HTTP communication with Roblox APIs (`RobloxHttpClient`) and related utilities (`HttpRequestMessageExtensions`).
*   **`Continuance.Roblox.Services`:** Contains classes that encapsulate logic for specific Roblox features by interacting with the HTTP client and APIs (e.g., `AuthenticationService`, `UserService`, `FriendService`, `AvatarService`, `GroupService`, `BadgeService`).
*   **`Continuance.Roblox.Automation`:** Contains classes responsible for interacting with external processes or tools (`GameLauncher`, `WebDriverManager`).
*   **`Continuance.UI`:** Manages the console user interface, including menus (`MainMenu`, `ActionsMenu`) and formatting utilities (`ConsoleUI`).
*   **`Actions`:** Contains the `AccountActionExecutor` class, which orchestrates the execution of actions on selected accounts, and related enums like `AcceptAttemptResult`.

## 4. Key Components

### 4.1. `Initialize` (Entry Point)

*   **Purpose:** Sets up and starts the application.
*   **Responsibilities:**
    *   Sets console encoding and title.
    *   Loads application settings using `LoadSettings` (from `settings.json` or defaults).
    *   Instantiates all necessary services, managers, and UI components (manual dependency injection).
    *   Starts the main application loop by calling `mainMenu.Show()`.
    *   Handles application shutdown message.
*   **`LoadSettings`/`SaveSettings`:** Private static methods to handle reading from and writing to `settings.json` using Newtonsoft.Json, applying defaults if the file is missing or corrupt.

### 4.2. `AppConfig` & `AppSettings`

*   **`AppConfig` (Static Class):**
    *   **Purpose:** Central repository for configuration values.
    *   **Features:**
        *   Defines `const` default values for various settings (API URLs, delays, retries, action parameters).
        *   Holds `static` properties for *runtime* settings that can be modified during execution (e.g., `CurrentApiDelayMs`, `RuntimeDefaultDisplayName`). These are initialized from defaults or loaded settings.
        *   Provides `UpdateRuntimeDefaults` to apply loaded `AppSettings`.
        *   Provides `GetCurrentSettings` to create an `AppSettings` object from the current runtime values for saving.
*   **`AppSettings` (Model Class):**
    *   **Purpose:** A plain data object representing the structure of `settings.json`. Used for serialization/deserialization. Mirrors the configurable properties in `AppConfig`.

### 4.3. Models (`Continuance.Models`)

*   **`Account`:**
    *   Represents a single Roblox account.
    *   Properties: `Cookie` (.ROBLOSECURITY), `UserId`, `Username`, `XcsrfToken`, `IsValid` (flag indicating if the cookie/token seems valid).
    *   `ToString()` override for basic display.
*   **`AvatarDetails`:**
    *   Stores information about a user's avatar fetched from the API.
    *   Properties: `AssetIds` (worn items), `BodyColors`, `PlayerAvatarType` (R6/R15), `Scales`, `FetchTime`. Uses `JObject` for complex nested JSON data like colors and scales.
*   **`VerificationStatus` (Enum):**
    *   Represents the result of the `VerifyAccountStatus` action for an account: `NotChecked`, `Passed`, `Failed`, `Error`.
*   **`AcceptAttemptResult` (Enum in `Actions` namespace):**
    *   Represents the outcome of trying to accept a friend request within the `HandleLimitedFriendRequestsAsync` logic.

### 4.4. `RobloxHttpClient` (`Continuance.Roblox.Http`)

*   **Purpose:** Centralized handling of all HTTP requests to Roblox APIs.
*   **Key Features:**
    *   Uses a static `HttpClient` instance (`httpClient`) for Roblox API calls. Configured to not use automatic cookie handling.
    *   Uses a separate static `HttpClient` (`externalHttpClient`) potentially for calls not requiring Roblox auth cookies (like fetching public avatar data).
    *   `CreateBaseRequest`: Helper to create `HttpRequestMessage` with common headers (User-Agent, Accept, etc.) and optional cookie.
    *   `SendRequestAndReadAsync`: The core method.
        *   Takes `HttpMethod`, `url`, `Account`, optional `content`, `actionDescription`, `allowRetryOnXcsrf`, `configureRequest` delegate.
        *   Clones the request before sending (`HttpRequestMessageExtensions.Clone`) to allow retries.
        *   Injects `.ROBLOSECURITY` cookie and `X-CSRF-TOKEN` header from the `Account` object.
        *   Handles responses: Checks `IsSuccessStatusCode`.
        *   **XCSRF Handling:** If a request fails with 403 Forbidden and an `X-CSRF-TOKEN` header is present in the response, it updates the token on the `Account` object and retries the request once (`goto retry_request`).
        *   **Error Handling:** Detects common errors like 429 (Rate Limited), 401 (Unauthorized - marks account invalid), and logs details.
        *   Handles `HttpRequestException`, `TaskCanceledException` (Timeout).
        *   Returns `(HttpStatusCode?, bool IsSuccess, string Content)`.
    *   `SendRequestAsync`: A convenience wrapper around `SendRequestAndReadAsync` that only returns the success boolean.
    *   `ValidateCookieAsync` (static): Checks if a `.ROBLOSECURITY` cookie is valid by hitting the `/users/authenticated` endpoint. Returns validity, User ID, and Username.
    *   `FetchXCSRFTokenAsync` (static): Attempts to fetch an XCSRF token using multiple strategies:
        1.  POST to `/logout` (common method).
        2.  POST to `/account/settings/birthdate`.
        3.  GET request to `/my/account` and scraping the HTML for the token using Regex (multiple patterns). Returns the token or an empty string on failure.

### 4.5. Services (`Continuance.Roblox.Services`)

Each service class typically depends on `RobloxHttpClient` and encapsulates API calls related to a specific Roblox feature.

*   **`AuthenticationService`:**
    *   Wraps `RobloxHttpClient.ValidateCookieAsync` and `RobloxHttpClient.FetchXCSRFTokenAsync`.
    *   `RefreshXCSRFTokenIfNeededAsync`: Checks token validity by making a simple authenticated GET request (e.g., friend count). If it fails due to XCSRF issues, it uses the retry logic built into `RobloxHttpClient`. If the token is missing or the account is marked invalid, it may attempt a full `FetchXCSRFTokenAsync` as a fallback. Returns `true` if the account has a valid token after the operation.
    *   `GetAuthenticationTicketAsync`: Handles the specific request flow to obtain a game launch authentication ticket (`RBX-Authentication-Ticket` header) required by the `roblox-player:` protocol. Includes Referrer header and XCSRF retry logic specific to this endpoint.
*   **`UserService`:**
    *   `SetDisplayNameAsync`: Sends a PATCH request to update the display name.
    *   `GetUsernamesAsync`: Fetches user details (including display name and actual username) from the `/users/{userId}` endpoint. Updates the username cached on the `Account` object if it differs.
    *   `GetCurrentDisplayNameAsync`: Convenience method calling `GetUsernamesAsync` and returning only the display name.
*   **`AvatarService`:**
    *   `FetchAvatarDetailsAsync`: Gets detailed avatar information (assets, colors, type, scales) for a given User ID using the *external* HTTP client.
    *   `SetAvatarAsync`: Orchestrates setting an account's avatar to match a source user's avatar.
        1.  Fetches details of the `sourceUserId`.
        2.  Calls multiple API endpoints sequentially (`set-body-colors`, `set-player-avatar-type`, `set-scales`, `set-wearing-assets`, `redraw-thumbnail`) using the target `account`'s credentials and the fetched details. Includes delays between steps.
    *   `CompareAvatarDetails`: Performs a deep comparison between two `AvatarDetails` objects (checks type, assets sequence, and deep equality for JObjects - scales, body colors).
*   **`GroupService`:**
    *   `JoinGroupAsync`: Sends a POST request to join a specified group ID.
*   **`FriendService`:**
    *   `SendFriendRequestAsync`: Sends a POST request to initiate a friendship. Handles the specific API error code (5) indicating a request is already pending or they are already friends.
    *   `AcceptFriendRequestAsync`: Sends a POST request to accept an incoming friend request.
    *   `GetFriendCountAsync`: Fetches the number of friends for an account.
*   **`BadgeService`:**
    *   `GetBadgeCountAsync`: Fetches a list of recently awarded badges for a user and returns the count (up to the specified `limit` - 10, 25, 50, or 100).
    *   `MonitorBadgeAcquisitionAsync`: Used after launching a game. Periodically checks the badge count using `GetBadgeCountAsync` for a limited time or until the `badgeGoal` is met or the user presses Enter.

### 4.6. Automation (`Continuance.Roblox.Automation`)

*   **`GameLauncher`:**
    *   **Purpose:** Launches the Roblox client to a specific game for the purpose of earning badges. **Requires user interaction within the game.**
    *   `LaunchGameForBadgesAsync`:
        1.  Validates account/game ID.
        2.  Refreshes XCSRF and gets an `AuthenticationTicket` using `AuthenticationService`.
        3.  Constructs the `roblox-player:1+launchmode:play...` URL including the auth ticket and other parameters.
        4.  Uses `Process.Start` with `UseShellExecute = true` to invoke the protocol handler.
        5.  Calls `BadgeService.MonitorBadgeAcquisitionAsync` to wait and check for badge progress.
        6.  Calls `TerminateRobloxProcessesAsync` to attempt to close the game client afterwards.
    *   `TerminateRobloxProcessesAsync` (private static): Attempts to find and kill processes named "RobloxPlayerBeta", "RobloxPlayerLauncher", etc. Only runs in interactive mode.
*   **`WebDriverManager`:**
    *   **Purpose:** Manages launching and configuring a Selenium ChromeDriver instance.
    *   `StartBrowserWithCookie` (static):
        1.  Configures `ChromeOptions` (headless mode optional, disable extensions, sandbox, etc.).
        2.  Suppresses ChromeDriver logging.
        3.  Starts the `ChromeDriver`.
        4.  Navigates to a Roblox page (e.g., login page).
        5.  Clears existing cookies.
        6.  Injects the `.ROBLOSECURITY` cookie from the `Account` object.
        7.  Navigates to the target `url`.
        8.  Performs a basic check for page elements (`#nav-robux-balance`, `#nav-username`) to heuristically verify login success.
        9.  Includes extensive error handling and hints for common ChromeDriver setup issues (missing driver, version mismatch, permissions). Returns the `IWebDriver` instance or `null` on failure.

### 4.7. Core (`Continuance.Core`)

*   **`AccountManager`:**
    *   **Purpose:** Manages the collection of loaded `Account` objects and their selection state.
    *   **State:**
        *   `_accounts`: `List<Account>` holding all loaded accounts.
        *   `_selectedAccountIndices`: `List<int>` storing the *indices* of selected accounts from the `_accounts` list.
        *   `_verificationResults`: `Dictionary<long, (VerificationStatus Status, string Details)>` to store results from the Verify action.
        *   `_lock`: A `Lock` object (presumably `System.Threading.Lock` or similar) used to protect access to the lists/dictionary, ensuring basic thread safety for add/select operations.
    *   **Methods:**
        *   `GetAllAccounts`, `GetSelectedAccountIndices`, `GetSelectedAccounts`: Provide read-only or copied access to the state.
        *   `AddAccountAsync`: Adds a single cookie, validates it, fetches XCSRF, creates `Account`, adds to list (if not duplicate).
        *   `ImportAccountsFromFileAsync`: Reads cookies from a file, calls `ImportAccountsAsync`.
        *   `ImportAccountsAsync`: Takes a list of cookies. Uses a `SemaphoreSlim` for concurrency control. For each cookie, runs a task to validate, fetch XCSRF, create `Account`, and add to the list (handling duplicates, errors). Logs progress.
        *   `UpdateSelection`: Toggles selection status for given indices.
        *   `SelectAll`, `SelectNone`, `SelectValid`, `SelectInvalid`, `SelectFailedVerification`: Modify the `_selectedAccountIndices` list based on criteria.
        *   `GetVerificationStatus`, `SetVerificationStatus`, `ClearVerificationResults`: Manage the verification results dictionary.
        *   `ExportAccountsToFileAsync`: Writes cookies and sorted usernames to a specified file.

### 4.8. Actions (`Actions`)

*   **`AccountActionExecutor`:**
    *   **Purpose:** Executes actions requested by the UI on the accounts selected in `AccountManager`.
    *   **Dependencies:** `AccountManager`, all `Service` classes, `WebDriverManager`, `GameLauncher`.
    *   **`PerformActionOnSelectedAsync`:** The main generic action execution method.
        *   Gets selected accounts from `AccountManager`.
        *   Filters for valid accounts (and optionally those with XCSRF tokens).
        *   Handles user confirmation if the number of accounts exceeds `AppConfig.CurrentActionConfirmationThreshold`.
        *   Iterates through the accounts to process.
        *   For each account, calls the provided `action` delegate (which contains the specific logic for that action, e.g., calling `_userService.SetDisplayNameAsync`).
        *   **Retry Logic:** Implements a retry loop (`AppConfig.CurrentMaxApiRetries`) with delays (`AppConfig.CurrentApiRetryDelayMs`) for the provided `action` if it returns `Success = false`. Handles exceptions during action execution and retries.
        *   Logs success, failure, or skipped status for each account.
        *   Tracks overall success/fail/skip counts and execution time.
        *   Uses `AppConfig.CurrentApiDelayMs` for delays between processing different accounts.
    *   **Specific Action Methods (`SetDisplayNameOnSelectedAsync`, `SetAvatarOnSelectedAsync`, etc.):**
        *   Each method defines the specific logic for an action, often including:
            *   Pre-checks (e.g., checking current display name before setting, comparing current avatar before setting).
            *   Calling the appropriate `Service` method.
            *   Wrapping this logic in a lambda passed to `PerformActionOnSelectedAsync`.
            *   `SetAvatarOnSelectedAsync` uses `GetOrFetchTargetAvatarDetailsAsync` for caching the source avatar.
    *   **`GetOrFetchTargetAvatarDetailsAsync`:** Caches the `AvatarDetails` for the source user ID used in `SetAvatarOnSelectedAsync` to avoid redundant API calls. Uses a `Lock` for thread safety around the cache.
    *   **`HandleLimitedFriendRequestsAsync`:** Implements the complex friend request cycling logic.
        *   **Phase 0 (Pre-check):** Validates accounts, refreshes XCSRF, gets current friend counts, filters out accounts meeting the goal or failing checks. Sorts remaining accounts.
        *   **Batching:** Optionally splits accounts into batches if the count is high, with configurable size and delay between batches.
        *   **Phase 1 (Sending):** Within each batch, iterates through accounts. Each account (`receiver`) has requests sent *to* it from the next 1 or 2 accounts in the (circular) batch (`sender1`, `sender2`). Uses `FriendService.SendFriendRequestAsync`. Tracks successful/pending sends in `batchSuccessfulSendPairs`. Introduces random delays (`AppConfig.CurrentFriendActionDelayMs` +/- randomness).
        *   **Wait:** Fixed delay between Phase 1 and Phase 2.
        *   **Phase 2 (Accepting):** Iterates through accounts again (`receiverAccount`). Attempts to accept requests *from* the accounts that previously sent *to* it (`potentialSender1`, `potentialSender2`), but *only if* the corresponding send was successful/pending in Phase 1 (checked against `batchSuccessfulSendPairs`). Uses `TryAcceptRequestAsync`.
        *   **`TryAcceptRequestAsync`:** Helper method called by Phase 2. Checks if the pair was already accepted in this run, checks sender/receiver validity, calls `FriendService.AcceptFriendRequestAsync`, adds pair to `acceptedPairs` on success. Returns an `AcceptAttemptResult`.
        *   **Summary:** Logs detailed statistics for sends and accepts (attempted, success, failed, skipped/pending) for each batch and overall.
    *   **`VerifyAccountStatusOnSelectedAsync`:**
        *   Fetches required target avatar details if needed.
        *   Iterates through selected valid accounts.
        *   For each account, calls various services (`FriendService.GetFriendCountAsync`, `BadgeService.GetBadgeCountAsync`, `UserService.GetUsernamesAsync`, `AvatarService.FetchAvatarDetailsAsync`).
        *   Compares results against requirements (`requiredFriends`, `requiredBadges`, `expectedDisplayName`, `targetAvatarDetails`).
        *   Sets the result (`VerificationStatus` and failure reasons) in `AccountManager`.
        *   Logs detailed pass/fail status for each check and overall.
    *   **`ExecuteAllAutoAsync`:** Calls `SetDisplayNameOnSelectedAsync`, `SetAvatarOnSelectedAsync`, `HandleLimitedFriendRequestsAsync`, and `GetBadgesOnSelectedAsync` sequentially using current runtime defaults from `AppConfig`. Skips badge step if non-interactive or configured improperly.

### 4.9. UI (`Continuance.UI`)

*   **`ConsoleUI` (Static Class):**
    *   Provides constants for box-drawing characters (`T_Branch`, `T_Vertical`, etc.).
    *   Helper methods for formatting output:
        *   `PrintMenuTitle`, `PrintMenuFooter`: Draw formatted menu headers/footers.
        *   `WriteLineInsideBox`: Print text indented within the menu box.
        *   `TreeLine`: Format lines with tree connectors.
        *   `Truncate`: Limit string length.
        *   `WriteErrorLine`, `WriteSuccessLine`, `WriteInfoLine`, `WriteWarningLine`: Print messages with specific colors and prefixes (`[!]`, `[+]`, `[*]`, `[?]`).
*   **`MainMenu`:**
    *   Displays the main menu options (Add, Import, List, Select, Actions, Settings, Export, Exit).
    *   Handles user input for the main menu.
    *   Calls appropriate `AccountManager` methods or navigates to the `ActionsMenu`.
    *   Contains UI logic for specific menu options (`AddAccountUI`, `ImportCookiesFromFileUI`, `ListAccountsUI`, `SelectAccountsUI`, `ExportAccountsToFileUI`, `SaveCurrentSettingsUI`).
*   **`ActionsMenu`:**
    *   Displays the secondary menu for executing actions on selected accounts.
    *   Shows current default parameters for actions.
    *   Calculates and displays rough time estimates for actions based on selected account count and configured delays (`EstimateActionTimePerAccount`, `GetOverallEstimateString`).
    *   Prompts for action-specific parameters (e.g., target display name, source avatar ID), allowing overrides of defaults.
    *   Calls the corresponding methods on `AccountActionExecutor`.
    *   Includes the UI for `AdjustRateLimitsUI`.
    *   Contains helper methods for getting validated integer/long/string input from the user (`GetIntInput`, `GetLongInput`, `GetStringInput`).

## 5. Workflow & Execution Flow

1.  **Initialization (`Initialize.Main`):**
    *   Load settings (`settings.json` -> `AppConfig`).
    *   Instantiate services and managers.
2.  **Main Menu (`MainMenu.Show`):**
    *   Display options.
    *   User chooses an option:
        *   **Load/Import:** Call `AccountManager.AddAccountAsync` or `ImportAccounts...`. Account manager validates cookies and fetches initial XCSRF tokens via `AuthenticationService`.
        *   **List/Show:** Call `AccountManager` getters and format using `ConsoleUI`.
        *   **Select:** Call `AccountManager` selection methods.
        *   **Actions Menu:** Call `ActionsMenu.Show()`.
        *   **Adjust Limits:** Call `ActionsMenu.AdjustRateLimitsUI()`.
        *   **Export:** Call `AccountManager.ExportAccountsToFileAsync`.
        *   **Save Settings:** Call `SaveSettings` using `AppConfig.GetCurrentSettings()`.
        *   **Exit:** Break the loop.
3.  **Actions Menu (`ActionsMenu.Show`):**
    *   Display action options with estimates.
    *   User chooses an action.
    *   Prompt for action parameters (use defaults or overrides).
    *   Call the relevant method on `AccountActionExecutor`.
4.  **Action Execution (`AccountActionExecutor`):**
    *   `PerformActionOnSelectedAsync` orchestrates the process for the chosen action.
    *   Filters selected accounts (validity, tokens).
    *   Confirms with the user if needed.
    *   Iterates:
        *   Calls the specific action delegate.
        *   Action delegate performs pre-checks (if any).
        *   Action delegate calls the required `Service` method(s).
        *   Services call `RobloxHttpClient`.
        *   `RobloxHttpClient` sends the HTTP request, handles XCSRF refresh/retry if needed.
        *   Results propagate back.
        *   `PerformActionOnSelectedAsync` handles action-level retries.
        *   Logs results.
        *   Introduces delays.
    *   Special handling for `HandleLimitedFriendRequestsAsync` (batching, multiple phases) and interactive actions (`GetBadges`, `OpenInBrowser`).
5.  **Return:** Control returns to the menu loops.

## 6. Error Handling & Retries

*   **HTTP Level (`RobloxHttpClient`):**
    *   Handles standard HTTP errors (4xx, 5xx).
    *   Specific handling for 401 (Unauthorized - marks account invalid), 403 (Forbidden - attempts XCSRF refresh/retry), 429 (Rate Limited - logs warning, fails action).
    *   Catches `HttpRequestException` and `TaskCanceledException` (Timeouts).
*   **Action Level (`AccountActionExecutor.PerformActionOnSelectedAsync`):**
    *   Retries the entire action delegate for an account if it fails (returns `Success=false`), up to `CurrentMaxApiRetries` times with `CurrentApiRetryDelayMs` delays.
    *   Catches general exceptions during action execution and retries.
*   **Input Validation (UI):** Basic checks for user input formats (numbers, ranges, paths).
*   **Service Level:** Services generally rely on `RobloxHttpClient` for primary error handling but may add specific checks (e.g., FriendService checking for error code 5).

## 7. Dependencies

*   **External:**
    *   `Newtonsoft.Json`: For loading/saving `settings.json`.
    *   `Selenium.WebDriver`: For browser automation (`OpenInBrowser`).
    *   `Selenium.WebDriver.ChromeDriver`: The specific driver for Chrome.
*   **Internal:** Clear separation between UI, Actions, Core, Services, Http, Automation, and Models layers. Services depend on Http Client, Actions depend on Services and Managers, UI depends on Managers and Actions. `AppConfig` is globally accessed via static properties.

## 8. Potential Issues & Areas for Improvement

*   **Rate Limiting:** Despite delays and retries, aggressive use with many accounts can still lead to Roblox rate limits or flags. The batching in friend requests helps, but other actions might benefit from similar strategies.
*   **CAPTCHAs:** Actions like joining groups are noted as potentially broken due to CAPTCHAs. The current code has no mechanism to handle CAPTCHAs.
*   **XCSRF Fragility:** While retry logic exists, rapid XCSRF rotation or unexpected API behavior could still cause failures.
*   **Error Handling Granularity:** Some errors might just result in a generic "failed" status without specific user feedback on *why* (e.g., specific API error messages aren't always parsed or displayed).
*   **Concurrency:** While `ImportAccountsAsync` uses `SemaphoreSlim`, most other operations process accounts sequentially within `PerformActionOnSelectedAsync`. True parallel execution of actions across multiple accounts would require more complex state management and careful handling of shared resources (like `HttpClient` and rate limits). The current `lock` in `AccountManager` provides basic safety for list/dictionary access but doesn't manage concurrency of external API calls.
*   **Dependency Management:** Manual instantiation in `Initialize.Main` works but could be replaced by a proper Dependency Injection container (like `Microsoft.Extensions.DependencyInjection`) for better testability and maintainability.
*   **Testing:** Lack of automated tests (Unit, Integration). Changes require manual testing.
*   **UI Robustness:** Console UI is functional but basic. Input parsing could be more robust.
*   **WebDriver Management:** Relies on `chromedriver` being present and compatible. Could potentially use `WebDriverManager` libraries to automatically download/manage the correct driver version. `OpenInBrowser` action is noted as potentially useless.
*   **Hardcoded Values:** While many settings are in `AppConfig`, some values (like phase delays in `HandleLimitedFriendRequestsAsync`, badge monitoring intervals) are hardcoded within methods.

## 9. How to Use (Basic)

1.  Compile the code using a .NET SDK.
2.  Ensure `chromedriver.exe` (or the equivalent for your OS) is accessible (in the same directory or in the system PATH) and matches your installed Chrome version if using the "Open in Browser" feature.
3.  Run the compiled executable (`Continuance.exe`).
4.  A `settings.json` file will be created with default values if it doesn't exist. Modify this file *before* running to change default behaviors persistently.
5.  Use the Main Menu (Options 1, 2, 3) to load accounts using their `.ROBLOSECURITY` cookies.
6.  Use Option 5 to select the accounts you want to perform actions on.
7.  Use Option 8 to access the Actions Menu.
8.  Choose an action, confirm parameters, and let it run.
9.  Monitor the console output for progress and errors.
10. Use Option 9 to temporarily adjust delays/retries for the current session. Use Option 11 to save these adjustments to `settings.json`.
11. Use Option 10 to export loaded cookies/usernames.

This documentation provides a comprehensive overview of the Continuance codebase, its structure, functionality, and key implementation details.

## 10. Learning Resources

For someone looking to understand the concepts used in this project or build something similar, here are some recommended learning areas and resources:

**10.1. C# and .NET Fundamentals:**

*   **Microsoft Learn - C#:** The official Microsoft documentation and tutorials are excellent starting points.
    *   [Get started with C#](https://learn.microsoft.com/en-us/dotnet/csharp/get-started/)
    *   [C# Fundamentals for Absolute Beginners](https://learn.microsoft.com/en-us/shows/csharp-fundamentals-for-absolute-beginners/)
*   **Books:** "C# in Depth" by Jon Skeet (Intermediate/Advanced), "Pro C# 10 with .NET 6" by Andrew Troelsen and Phil Japikse (Comprehensive).

**10.2. Asynchronous Programming (`async`/`await`):**

*   Crucial for handling I/O operations (like HTTP requests) without blocking the application.
*   **Microsoft Learn - Asynchronous programming with async and await:**
    *   [Asynchronous programming](https://learn.microsoft.com/en-us/dotnet/csharp/async)
*   **Stephen Cleary's Blog:** A great resource for deep dives into async/await best practices.

**10.3. HTTP and REST APIs:**

*   Understanding how web communication works is essential.
*   **MDN Web Docs - HTTP:** Comprehensive documentation on HTTP methods, headers, status codes, etc.
    *   [An overview of HTTP](https://developer.mozilla.org/en-US/docs/Web/HTTP/Overview)
*   **REST API Concepts:** Search for tutorials explaining REST principles (Resources, Verbs, Representations).

**10.4. Working with `HttpClient` in C#:**

*   The primary tool for making HTTP requests in .NET.
*   **Microsoft Learn - Make HTTP requests using the HttpClient class:**
    *   [HttpClient Class](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient)
    *   [HttpClientFactory guidance](https://learn.microsoft.com/en-us/dotnet/core/extensions/httpclient-factory) (While this project uses static clients, Factory is often preferred in larger apps).

**10.5. JSON Manipulation (Newtonsoft.Json):**

*   Used extensively for parsing API responses and handling `settings.json`.
*   **Newtonsoft.Json Documentation:**
    *   [Official Documentation](https://www.newtonsoft.com/json/help/html/Introduction.htm)
*   **Tutorials:** Search for "Newtonsoft.Json C# tutorial".

**10.6. Regular Expressions (Regex):**

*   Used for the fallback XCSRF token scraping.
*   **Microsoft Learn - .NET Regular Expressions:**
    *   [.NET Regular Expressions](https://learn.microsoft.com/en-us/dotnet/standard/base-types/regular-expressions)
*   **Online Regex Testers:** Tools like regex101.com are invaluable for building and testing patterns.

**10.7. Concurrency and Threading:**

*   Understanding `Task`, `SemaphoreSlim`, `lock` for managing concurrent operations and shared resources.
*   **Microsoft Learn - Task asynchronous programming model (TAP):**
    *   [Task-based asynchronous pattern (TAP)](https://learn.microsoft.com/en-us/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap)
*   **Concurrency in C# Cookbook** by Stephen Cleary (Book).

**10.8. Roblox APIs:**

*   **Important Note:** Roblox does not offer official, publicly documented, or supported APIs for general user automation like this. The endpoints used are discovered by observing network traffic from the official website/client.
*   **Discovery:** Use browser developer tools (Network tab) while using roblox.com to see the API calls being made.

**10.9. Console Application Design:**

*   Structuring code, handling user input, formatting output. Experience comes with practice, but look at examples of well-structured console apps.

**10.10. (Optional) Selenium WebDriver:**

*   If interested in the browser automation aspect.
*   **Selenium Documentation:**
    *   [Official Selenium Documentation](https://www.selenium.dev/documentation/)
*   **Tutorials:** Search for "Selenium C# tutorial".

**10.11. (Optional) Dependency Injection:**

*   A pattern for managing dependencies between classes, improving testability and maintainability.
*   **Microsoft Learn - Dependency injection in .NET:**
    *   [Dependency injection in .NET](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection)
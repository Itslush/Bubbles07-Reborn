using Continuance.Models;

namespace Continuance
{
    public static class AppConfig
    {
        public const int DefaultApiDelayMs = 2500;
        public const int DefaultFriendActionDelayMs = 10000;
        public const int SafeApiDelayMs = 6000;
        public const int SafeFriendActionDelayMs = 6000;
        public const int MinAllowedDelayMs = 500;
        public const int XcsrfRetryDelayMs = 5000;
        public const int RateLimitRetryDelayMs = 15000;

        public const int DefaultMaxApiRetries = 3;
        public const int DefaultApiRetryDelayMs = 5000;
        public const int MinRetryDelayMs = 1000;

        public static int CurrentApiDelayMs { get; set; } = DefaultApiDelayMs;
        public static int CurrentFriendActionDelayMs { get; set; } = DefaultFriendActionDelayMs;
        public static int DefaultRequestTimeoutSec { get; set; } = 45;
        public static int CurrentMaxApiRetries { get; set; } = DefaultMaxApiRetries;
        public static int CurrentApiRetryDelayMs { get; set; } = DefaultApiRetryDelayMs;
        public static int CurrentActionConfirmationThreshold { get; set; } = 15;

        public const string DefaultDisplayName = "dotggslashrblxgenTCD";
        public const long DefaultGroupId = 4165692;
        public const string DefaultBadgeGameId = "11525834465";
        public const long DefaultTargetUserIdForAvatarCopy = 4075892082;
        public const string HomePageUrl = "https://www.roblox.com/home";

        public const int DefaultFriendGoal = 2;
        public const int DefaultBadgeGoal = 5;

        public static string RuntimeDefaultDisplayName { get; set; } = DefaultDisplayName;
        public static long RuntimeDefaultGroupId { get; set; } = DefaultGroupId;
        public static string RuntimeDefaultBadgeGameId { get; set; } = DefaultBadgeGameId;
        public static long RuntimeDefaultTargetUserIdForAvatarCopy { get; set; } = DefaultTargetUserIdForAvatarCopy;
        public static int RuntimeDefaultFriendGoal { get; set; } = DefaultFriendGoal;
        public static int RuntimeDefaultBadgeGoal { get; set; } = DefaultBadgeGoal;

        public const string RobloxApiBaseUrl = "https://api.roblox.com";
        public const string RobloxWebBaseUrl = "https://www.roblox.com";
        public const string RobloxApiBaseUrl_Users = "https://users.roblox.com";
        public const string RobloxApiBaseUrl_Friends = "https://friends.roblox.com";
        public const string RobloxApiBaseUrl_Avatar = "https://avatar.roblox.com";
        public const string RobloxApiBaseUrl_Groups = "https://groups.roblox.com";
        public const string RobloxApiBaseUrl_Badges = "https://badges.roblox.com";
        public const string RobloxApiBaseUrl_Auth = "https://auth.roblox.com";
        public const string RobloxApiBaseUrl_AccountInfo = "https://accountinformation.roblox.com";

        public static void UpdateRuntimeDefaults(AppSettings settings)
        {
            CurrentApiDelayMs = settings.ApiDelayMs >= MinAllowedDelayMs ? settings.ApiDelayMs : DefaultApiDelayMs;
            CurrentFriendActionDelayMs = settings.FriendActionDelayMs >= MinAllowedDelayMs ? settings.FriendActionDelayMs : DefaultFriendActionDelayMs;
            DefaultRequestTimeoutSec = settings.RequestTimeoutSec >= 5 && settings.RequestTimeoutSec <= 120 ? settings.RequestTimeoutSec : DefaultRequestTimeoutSec;
            CurrentMaxApiRetries = settings.MaxApiRetries >= 0 ? settings.MaxApiRetries : DefaultMaxApiRetries;
            CurrentApiRetryDelayMs = settings.ApiRetryDelayMs >= MinRetryDelayMs ? settings.ApiRetryDelayMs : DefaultApiRetryDelayMs;
            CurrentActionConfirmationThreshold = settings.ActionConfirmationThreshold >= 0 ? settings.ActionConfirmationThreshold : 15;

            RuntimeDefaultDisplayName = !string.IsNullOrWhiteSpace(settings.DefaultDisplayName) ? settings.DefaultDisplayName : DefaultDisplayName;
            RuntimeDefaultGroupId = settings.DefaultGroupId > 0 ? settings.DefaultGroupId : DefaultGroupId;
            RuntimeDefaultBadgeGameId = !string.IsNullOrWhiteSpace(settings.DefaultBadgeGameId) ? settings.DefaultBadgeGameId : DefaultBadgeGameId;
            RuntimeDefaultTargetUserIdForAvatarCopy = settings.DefaultTargetUserIdForAvatarCopy > 0 ? settings.DefaultTargetUserIdForAvatarCopy : DefaultTargetUserIdForAvatarCopy;
            RuntimeDefaultFriendGoal = settings.DefaultFriendGoal >= 0 ? settings.DefaultFriendGoal : DefaultFriendGoal;
            RuntimeDefaultBadgeGoal = settings.DefaultBadgeGoal >= 0 ? settings.DefaultBadgeGoal : DefaultBadgeGoal;
        }

        public static AppSettings GetCurrentSettings()
        {
            return new AppSettings
            {
                ApiDelayMs = CurrentApiDelayMs,
                FriendActionDelayMs = CurrentFriendActionDelayMs,
                RequestTimeoutSec = DefaultRequestTimeoutSec,
                MaxApiRetries = CurrentMaxApiRetries,
                ApiRetryDelayMs = CurrentApiRetryDelayMs,
                DefaultDisplayName = RuntimeDefaultDisplayName,
                DefaultGroupId = RuntimeDefaultGroupId,
                DefaultBadgeGameId = RuntimeDefaultBadgeGameId,
                DefaultTargetUserIdForAvatarCopy = RuntimeDefaultTargetUserIdForAvatarCopy,
                DefaultFriendGoal = RuntimeDefaultFriendGoal,
                DefaultBadgeGoal = RuntimeDefaultBadgeGoal,
                ActionConfirmationThreshold = CurrentActionConfirmationThreshold
            };
        }
    }
}
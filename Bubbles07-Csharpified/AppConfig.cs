namespace _Csharpified
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

        public const string DefaultDisplayName = "dotggslashrblxgenTCD";
        public const long DefaultGroupId = 4165692;
        public const string DefaultBadgeGameId = "11525834465";
        public const long DefaultTargetUserIdForAvatarCopy = 4075892082;
        public const string HomePageUrl = "https://www.roblox.com/home";

        public const int DefaultFriendGoal = 2;
        public const int DefaultBadgeGoal = 5;

        public const string RobloxApiBaseUrl = "https://api.roblox.com";
        public const string RobloxWebBaseUrl = "https://www.roblox.com";
        public const string RobloxApiBaseUrl_Users = "https://users.roblox.com";
        public const string RobloxApiBaseUrl_Friends = "https://friends.roblox.com";
        public const string RobloxApiBaseUrl_Avatar = "https://avatar.roblox.com";
        public const string RobloxApiBaseUrl_Groups = "https://groups.roblox.com";
        public const string RobloxApiBaseUrl_Badges = "https://badges.roblox.com";
        public const string RobloxApiBaseUrl_Auth = "https://auth.roblox.com";
        public const string RobloxApiBaseUrl_AccountInfo = "https://accountinformation.roblox.com";
    }
}
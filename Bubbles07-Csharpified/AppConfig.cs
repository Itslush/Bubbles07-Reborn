using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace
    _Csharpified
{
    public static class AppConfig
    {
        public const int DefaultApiDelayMs = 2000;
        public const int DefaultFriendActionDelayMs = 6500;
        public const int SafeApiDelayMs = 1500;
        public const int SafeFriendActionDelayMs = 6000;
        public static int CurrentApiDelayMs { get; set; } = DefaultApiDelayMs;
        public static int CurrentFriendActionDelayMs { get; set; } = DefaultFriendActionDelayMs;
        public const int MinAllowedDelayMs = 500;

        public const string DefaultDisplayName = "dotggslashrblxgenTCD";
        public const long DefaultGroupId = 4165692;
        public const string DefaultBadgeGameId = "11525834465";
        public const long DefaultTargetUserIdForAvatarCopy = 4075892082;
        public const string HomePageUrl = "https://www.roblox.com/home";
    }
}

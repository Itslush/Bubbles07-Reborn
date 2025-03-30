using Actions;
using Core;
using Roblox.Automation;
using Roblox.Http;
using Roblox.Services;
using UI;

public class Program
{
    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("[*] Initializing Application Components...");

        var robloxHttpClient = new RobloxHttpClient();
        var authService = new AuthenticationService(robloxHttpClient);
        var accountManager = new AccountManager(authService);
        var userService = new UserService(robloxHttpClient);
        var avatarService = new AvatarService(robloxHttpClient);
        var groupService = new GroupService(robloxHttpClient);
        var friendService = new FriendService(robloxHttpClient);
        var badgeService = new BadgeService(robloxHttpClient);
        var webDriverManager = new WebDriverManager();
        var gameLauncher = new GameLauncher(authService, badgeService);

        var actionExecutor = new AccountActionExecutor(
            accountManager,
            authService,
            userService,
            avatarService,
            groupService,
            friendService,
            badgeService,
            webDriverManager,
            gameLauncher
        );

        var actionsMenu = new ActionsMenu(accountManager, actionExecutor);
        var mainMenu = new MainMenu(accountManager, actionsMenu);

        Console.WriteLine("[+] Initialization Complete.");

        await mainMenu.Show();

        Console.WriteLine($"[!] Terminating session...");
    }
}
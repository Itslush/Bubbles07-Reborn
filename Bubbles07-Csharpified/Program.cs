using _Csharpified.Actions;
using _Csharpified.Core;
using _Csharpified.Roblox.Automation;
using _Csharpified.Roblox.Http;
using _Csharpified.Roblox.Services;
using _Csharpified.UI;

public class Program
{
    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("[*] Initializing Application Components...");

        // Setup Dependency Injection (Manual)
        var robloxHttpClient = new RobloxHttpClient();
        var authService = new AuthenticationService(robloxHttpClient);
        var accountManager = new AccountManager(authService); // Give AccountManager the auth service for validation
        var userService = new UserService(robloxHttpClient);
        var avatarService = new AvatarService(robloxHttpClient);
        var groupService = new GroupService(robloxHttpClient);
        var friendService = new FriendService(robloxHttpClient);
        var badgeService = new BadgeService(robloxHttpClient);
        var webDriverManager = new WebDriverManager();
        var gameLauncher = new GameLauncher(authService, badgeService); // GameLauncher needs auth/badge services

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

        // Start the UI
        await mainMenu.Show();

        Console.WriteLine($"[!] Terminating session...");
    }
}
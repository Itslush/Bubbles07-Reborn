using System;
using System.Text;
using System.Threading.Tasks;
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
        try { Console.Title = "Bubbles07 - Reborn || OPERATION: TCD"; } catch { }

        Console.WriteLine("[*] Initializing Application Components...");

        var robloxHttpClient = new RobloxHttpClient();
        var authService = new AuthenticationService(robloxHttpClient);
        var userService = new UserService(robloxHttpClient);
        var avatarService = new AvatarService(robloxHttpClient);
        var groupService = new GroupService(robloxHttpClient);
        var friendService = new FriendService(robloxHttpClient);
        var badgeService = new BadgeService(robloxHttpClient);
        var accountManager = new AccountManager(authService);
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
        Console.WriteLine("[*] Clearing console and launching Main Menu...");
        await Task.Delay(1500);
        Console.Clear();

        await mainMenu.Show();

        Console.Clear();
        Console.WriteLine($"\n[!] Application shutting down. Press Enter to close window.");
        Console.ReadLine();
    }
}
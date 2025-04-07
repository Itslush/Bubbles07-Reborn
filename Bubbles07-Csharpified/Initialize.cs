using Newtonsoft.Json;
using Actions;
using Continuance.Core;
using Continuance.Roblox.Automation;
using Continuance.Roblox.Http;
using Continuance.Roblox.Services;
using Continuance;
using Continuance.Models;
using Continuance.UI;

public class Initialize
{
    private const string SettingsFilePath = "settings.json";

    private static void LoadSettings()
    {
        AppSettings settings = new AppSettings();
        if (File.Exists(SettingsFilePath))
        {
            try
            {
                string json = File.ReadAllText(SettingsFilePath);
                settings = JsonConvert.DeserializeObject<AppSettings>(json) ?? settings;
                ConsoleUI.WriteInfoLine($"Loaded settings from {SettingsFilePath}");
            }
            catch (Exception ex)
            {
                ConsoleUI.WriteErrorLine($"Failed to load settings from {SettingsFilePath}: {ex.Message}. Using defaults.");
                SaveSettings(settings);
            }
        }
        else
        {
            ConsoleUI.WriteInfoLine($"Settings file ({SettingsFilePath}) not found. Using defaults and creating file.");
            SaveSettings(settings);
        }

        AppConfig.UpdateRuntimeDefaults(settings);
    }

    private static void SaveSettings(AppSettings settings)
    {
        try
        {
            string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
            File.WriteAllText(SettingsFilePath, json);
        }
        catch (Exception ex)
        {
            ConsoleUI.WriteErrorLine($"Failed to save initial default settings to {SettingsFilePath}: {ex.Message}");
        }
    }

    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        try { Console.Title = "Continuance || OPERATION: TCD"; } catch { }

        ConsoleUI.WriteInfoLine("Initializing Application Components...");

        LoadSettings();

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

        ConsoleUI.WriteSuccessLine("Initialization Complete.");
        ConsoleUI.WriteInfoLine("Clearing console and launching Main Menu...");
        await Task.Delay(1500);
        Console.Clear();

        await mainMenu.Show();

        Console.Clear();
        ConsoleUI.WriteErrorLine($"\nApplication shutting down. Press Enter to close window.");
        Console.ReadLine();
    }
}
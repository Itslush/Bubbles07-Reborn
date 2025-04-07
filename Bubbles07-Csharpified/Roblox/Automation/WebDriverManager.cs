using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using OpenQA.Selenium;
using Continuance.Models;
using Continuance.UI;

namespace Continuance.Roblox.Automation
{
    public class WebDriverManager
    {
        public static IWebDriver? StartBrowserWithCookie(Account account, string url, bool headless = false)
        {
            if (string.IsNullOrWhiteSpace(account.Cookie))
            {
                ConsoleUI.WriteErrorLine($"Cannot start browser for {account.Username}: Account cookie is missing.");
                return null;
            }
            try
            {
                var options = new ChromeOptions();
                ConsoleUI.WriteInfoLine("Initializing WebDriver instance...");

                if (headless || !Environment.UserInteractive || Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
                {
                    ConsoleUI.WriteInfoLine("Configuring WebDriver :: Headless Mode Activated.");
                    options.AddArgument("--headless=new");
                    options.AddArgument("--disable-gpu");
                }

                options.AddArgument("--disable-extensions");
                options.AddArgument("--no-sandbox");
                options.AddArgument("--disable-dev-shm-usage");
                options.AddArgument("--window-size=400,400");
                options.AddArgument("--log-level=3");

                options.AddExcludedArgument("enable-logging");
                options.AddExcludedArgument("enable-automation");
                options.AddAdditionalOption("useAutomationExtension", false);

                ChromeDriverService service = ChromeDriverService.CreateDefaultService();
                service.HideCommandPromptWindow = true;
                service.SuppressInitialDiagnosticInformation = true;

                ConsoleUI.WriteInfoLine("Creating ChromeDriver...");
                IWebDriver driver = new ChromeDriver(service, options);
                ConsoleUI.WriteInfoLine("ChromeDriver created.");

                driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(60);

                ConsoleUI.WriteInfoLine("Navigating to Roblox.com to set cookie...");
                driver.Navigate().GoToUrl("https://www.roblox.com/login");
                Task.Delay(1000).Wait();

                driver.Manage().Cookies.DeleteAllCookies();
                ConsoleUI.WriteInfoLine("Purged existing browser cookies.");

                var seleniumCookie = new Cookie(
                     name: ".ROBLOSECURITY",
                     value: account.Cookie,
                     domain: ".roblox.com",
                     path: "/",
                     expiry: DateTime.Now.AddYears(1),
                     secure: true,
                     isHttpOnly: true,
                     sameSite: "Lax"
                     );

                driver.Manage().Cookies.AddCookie(seleniumCookie);
                ConsoleUI.WriteSuccessLine("ROBLOSECURITY Cookie Injected into WebDriver.");
                Task.Delay(500).Wait();

                ConsoleUI.WriteInfoLine($"Navigating WebDriver to target URL: {url}");
                driver.Navigate().GoToUrl(url);

                try
                {
                    WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));
                    wait.Until(d => d.FindElement(By.CssSelector("#nav-robux-balance, #nav-username")) != null);
                    ConsoleUI.WriteSuccessLine("Login Confirmed via Page Element.");
                }
                catch (WebDriverTimeoutException)
                {
                    ConsoleUI.WriteWarningLine("Could not confirm successful login via page element after setting cookie. Proceeding anyway.");
                }
                catch (NoSuchElementException)
                {
                    ConsoleUI.WriteWarningLine("Login confirmation element not found. Proceeding anyway.");
                }

                ConsoleUI.WriteSuccessLine("Navigation Complete.");
                return driver;
            }
            catch (Exception ex)
            {
                ConsoleUI.WriteErrorLine($"WebDriver Initialization Error for {account.Username}: {ex.Message}");
                if (ex.InnerException != null) ConsoleUI.WriteErrorLine($"Inner Exception: {ex.InnerException.Message}");

                if (ex is WebDriverException || ex.Message.Contains("chromedriver", StringComparison.CurrentCultureIgnoreCase))
                {
                    ConsoleUI.WriteWarningLine("Hint: Ensure 'chromedriver' (or 'chromedriver.exe') is in your system's PATH or the application's directory, is executable, and matches your installed Chrome browser version.");

                    if (OperatingSystem.IsWindows()) ConsoleUI.WriteWarningLine("Windows: Download from https://googlechromelabs.github.io/chrome-for-testing/ and place chromedriver.exe next to your program or in PATH.");
                    else if (OperatingSystem.IsLinux()) ConsoleUI.WriteWarningLine("Linux: Use package manager (e.g., 'sudo apt install chromium-chromedriver') or download and place in PATH, ensure executable ('chmod +x chromedriver').");
                    else if (OperatingSystem.IsMacOS()) ConsoleUI.WriteWarningLine("macOS: Use Homebrew ('brew install chromedriver') or download, place in PATH, and allow execution (System Preferences > Security & Privacy).");
                }
                return null;
            }
        }
    }
}
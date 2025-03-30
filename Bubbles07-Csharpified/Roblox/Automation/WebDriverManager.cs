using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using OpenQA.Selenium;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using _Csharpified.Models;
namespace
    _Csharpified.Roblox.Automation
{
    public class WebDriverManager
    {
        public IWebDriver? StartBrowserWithCookie(Account account, string url, bool headless = false)
        {
            if (string.IsNullOrWhiteSpace(account.Cookie))
            {
                Console.WriteLine($"[!] Cannot start browser for {account.Username}: Account cookie is missing.");
                return null;
            }
            try
            {
                var options = new ChromeOptions();
                Console.WriteLine($"[>] Initializing WebDriver instance...");
                if (headless || !Environment.UserInteractive || Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
                {
                    Console.WriteLine($"[*] Configuring WebDriver :: Headless Mode Activated.");
                    options.AddArgument("--headless=new");
                    options.AddArgument("--disable-gpu");
                }
                options.AddArgument("--disable-extensions");
                options.AddArgument("--no-sandbox");
                options.AddArgument("--disable-dev-shm-usage");
                options.AddArgument("--window-size=1920,1080");
                options.AddArgument("--log-level=3");
                options.AddExcludedArgument("enable-logging");
                options.AddExcludedArgument("enable-automation");
                options.AddAdditionalOption("useAutomationExtension", false);

                ChromeDriverService service = ChromeDriverService.CreateDefaultService();
                service.HideCommandPromptWindow = true;
                service.SuppressInitialDiagnosticInformation = true;

                Console.WriteLine($"[>] Creating ChromeDriver...");
                IWebDriver driver = new ChromeDriver(service, options);
                Console.WriteLine($"[>] ChromeDriver created.");

                driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(60);

                Console.WriteLine($"[>] Navigating to Roblox.com to set cookie...");
                driver.Navigate().GoToUrl("https://www.roblox.com/login");
                Task.Delay(1000).Wait();
                driver.Manage().Cookies.DeleteAllCookies();
                Console.WriteLine($"[>] Purged existing browser cookies.");

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
                Console.WriteLine($"[+] ROBLOSECURITY Cookie Injected into WebDriver.");
                Task.Delay(500).Wait();

                Console.WriteLine($"[>] Navigating WebDriver to target URL: {url}");
                driver.Navigate().GoToUrl(url);

                try
                {
                    WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));
                    wait.Until(d => d.FindElement(By.Id("nav-robux-balance")) != null || d.FindElement(By.Id("nav-username")) != null);
                    Console.WriteLine($"[+] Login Confirmed via Page Element.");
                }
                catch (WebDriverTimeoutException)
                {
                    Console.WriteLine($"[!] Warning: Could not confirm successful login via page element after setting cookie. Proceeding anyway.");
                }

                Console.WriteLine($"[+] Navigation Complete.");
                return driver;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] WebDriver Initialization Error for {account.Username}: {ex.Message}");
                if (ex.InnerException != null) Console.WriteLine($"[!] Inner Exception: {ex.InnerException.Message}");
                if (ex is WebDriverException || ex.Message.ToLower().Contains("chromedriver"))
                {
                    Console.WriteLine($"[?] Hint: Ensure 'chromedriver' (or 'chromedriver.exe') is in your system's PATH or the application's directory, is executable, and matches your installed Chrome browser version.");
                    if (OperatingSystem.IsWindows()) Console.WriteLine($"[?] Windows: Download from https://googlechromelabs.github.io/chrome-for-testing/ and place chromedriver.exe next to your program or in PATH.");
                    else if (OperatingSystem.IsLinux()) Console.WriteLine($"[?] Linux: Use package manager (e.g., 'sudo apt install chromium-chromedriver') or download and place in PATH, ensure executable ('chmod +x chromedriver').");
                    else if (OperatingSystem.IsMacOS()) Console.WriteLine($"[?] macOS: Use Homebrew ('brew install chromedriver') or download, place in PATH, and allow execution (System Preferences > Security & Privacy).");
                }
                return null;
            }
        }
    }
}
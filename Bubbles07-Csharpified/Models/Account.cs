namespace Continuance.Models
{
    public class Account
    {
        public string Cookie { get; set; } = "";
        public long UserId { get; set; }
        public string Username { get; set; } = "N/A";
        public string XcsrfToken { get; set; } = "";
        public bool IsValid { get; set; } = true;
        public override string ToString()
        {
            string validity = IsValid ? "[OK]" : "[BAD]";
            return $"{validity} {UserId}, {Username}";
        }
    }
}
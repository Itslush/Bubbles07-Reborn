namespace Models
{
    public class Account
    {
        public string Cookie { get; set; } = string.Empty;
        public long UserId { get; set; }
        public string Username { get; set; } = "N/A";
        public string XcsrfToken { get; set; } = string.Empty;
        public bool IsValid { get; set; } = false;

        public override string ToString()
        {
            string status = IsValid ? "[OK]" : "[!!]";
            return $"{status} ID: {UserId}, User: {Username}";
        }
    }
}
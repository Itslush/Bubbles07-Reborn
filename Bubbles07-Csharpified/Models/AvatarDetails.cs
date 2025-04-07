using Newtonsoft.Json.Linq;

namespace Continuance.Models
{
    public class AvatarDetails
    {
        public List<long>? AssetIds { get; set; }
        public JObject? BodyColors { get; set; }
        public string? PlayerAvatarType { get; set; }
        public JObject? Scales { get; set; }
        public DateTime FetchTime { get; set; }
    }
}
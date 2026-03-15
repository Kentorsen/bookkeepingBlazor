using System.Text.Json.Serialization;

namespace BookkeepingBlazor.Models
{
    public class RoleInfo
    {
        [JsonPropertyName("id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public long Id { get; set; }

        [JsonPropertyName("role_type")]
        public string? RoleType { get; set; }

        [JsonPropertyName("is_deleted")]
        public bool IsDeleted { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
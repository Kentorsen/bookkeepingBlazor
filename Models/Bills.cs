using System.Text.Json.Serialization;

namespace BookkeepingBlazor.Models
{
    public class Bill
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("io_type")]
        public short IoType { get; set; }

        [JsonPropertyName("main_category")]
        public long? MainCategory { get; set; }

        [JsonPropertyName("sub_category")]
        public long? SubCategory { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }

        [JsonPropertyName("owner_role_id")]
        public long? OwnerRoleId { get; set; }

        [JsonPropertyName("payer_role_id")]
        public long? PayerRoleId { get; set; }

        [JsonPropertyName("bill_date")]
        public DateTime? BillDate { get; set; }

        [JsonPropertyName("is_extra")]
        public bool IsExtra { get; set; }

        [JsonPropertyName("marked_payer_role_id")]
        public long? MarkedPayerRoleId { get; set; }

        [JsonPropertyName("is_deleted")]
        public bool IsDeleted { get; set; }

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("created_by")]
        public long CreatedBy { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTime UpdatedAt { get; set; }

        [JsonPropertyName("updated_by")]
        public long UpdatedBy { get; set; }
    }
}

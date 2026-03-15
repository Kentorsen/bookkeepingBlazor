using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BookkeepingBlazor.Models;

namespace BookkeepingBlazor.Services
{
    public class SupabaseService
    {
        private const string SupabaseUrl = "https://wklbdlsjgbqfalrfomfe.supabase.co";
        private const string SupabaseAnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6IndrbGJkbHNqZ2JxZmFscmZvbWZlIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzIzNTUyODMsImV4cCI6MjA4NzkzMTI4M30.YvWTV6Ed1xKj0-u5czeVhiKrIO2anKlFQ4Uh4BKIBSE";

        private readonly HttpClient _http;

        public SupabaseService(HttpClient http)
        {
            _http = http;
        }

        private void ApplyAuthHeaders(HttpRequestMessage request)
        {
            request.Headers.Add("apikey", SupabaseAnonKey);
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", SupabaseAnonKey);
        }

        private async Task<List<T>> GetListAsync<T>(string relativeUrl)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{SupabaseUrl}/rest/v1/{relativeUrl}");
            ApplyAuthHeaders(request);

            var response = await _http.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            return JsonSerializer.Deserialize<List<T>>(json, options) ?? new List<T>();
        }

        private async Task PatchAsync(string relativeUrl, object payload)
        {
            var json = JsonSerializer.Serialize(payload);
            var request = new HttpRequestMessage(
                HttpMethod.Patch,
                $"{SupabaseUrl}/rest/v1/{relativeUrl}");

            ApplyAuthHeaders(request);
            request.Headers.Add("Prefer", "return=minimal");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _http.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }

        public Task<List<Bill>> GetBillsByRangeAsync(DateOnly startDate, DateOnly endExclusive)
        {
            var url =
                $"bills" +
                $"?select=*" +
                $"&is_deleted=is.false" +
                $"&bill_date=gte.{startDate:yyyy-MM-dd}" +
                $"&bill_date=lt.{endExclusive:yyyy-MM-dd}" +
                $"&order=bill_date.desc,id.desc";

            return GetListAsync<Bill>(url);
        }

        public Task<List<RoleInfo>> GetRolesAsync()
        {
            return GetListAsync<RoleInfo>(
                "roles?select=id,role_type,is_deleted,name&order=id.asc");
        }

        public Task<List<AppUser>> GetUsersAsync()
        {
            return GetListAsync<AppUser>(
                "users?select=id,name,is_deleted&order=id.asc");
        }

        public Task<List<MainCategoryInfo>> GetMainCategoriesAsync()
        {
            return GetListAsync<MainCategoryInfo>(
                "main_categories?select=*&order=id.asc");
        }

        public Task<List<SubCategoryInfo>> GetSubCategoriesAsync()
        {
            return GetListAsync<SubCategoryInfo>(
                "sub_categories?select=*&order=id.asc");
        }

        public Task MarkBillAsync(long billId, long markedPayerRoleId)
        {
            return PatchAsync(
                $"bills?id=eq.{billId}",
                new
                {
                    marked_payer_role_id = markedPayerRoleId,
                    updated_at = DateTime.UtcNow
                });
        }

        public Task ClearBillMarkAsync(long billId)
        {
            return PatchAsync(
                $"bills?id=eq.{billId}",
                new
                {
                    marked_payer_role_id = 0,
                    updated_at = DateTime.UtcNow
                });
        }

        public Task SoftDeleteBillAsync(long billId)
        {
            return PatchAsync(
                $"bills?id=eq.{billId}",
                new
                {
                    is_deleted = true,
                    updated_at = DateTime.UtcNow
                });
        }

        public async Task InsertBillAsync(Bill newBill)
        {
            var json = JsonSerializer.Serialize(newBill);
            var request = new HttpRequestMessage(HttpMethod.Post, $"{SupabaseUrl}/rest/v1/bills");

            ApplyAuthHeaders(request);
            request.Headers.Add("Prefer", "return=minimal");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _http.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }
    }
}
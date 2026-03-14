using BookkeepingBlazor.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace BookkeepingBlazor.Services
{
    public class SupabaseService
    {
        // TODO：这里换成你 Supabase 后台里的 URL & anon key
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

        /// <summary>获取所有账单</summary>
        public async Task<List<Bill>> GetBillsAsync()
        {
            var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{SupabaseUrl}/rest/v1/bills?select=*&order=bill_date.desc");

            ApplyAuthHeaders(request);

            var response = await _http.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            return JsonSerializer.Deserialize<List<Bill>>(json, options) ?? new();
        }

        /// <summary>新增一条账单</summary>
        public async Task<Bill?> AddBillAsync(Bill bill)
        {
            // 注意：这里只发需要的字段，id/created_at 这种交给数据库生成
            var payload = new
            {
                io_type = bill.IoType,
                main_category = bill.MainCategory,
                sub_category = bill.SubCategory,
                title = bill.Title,
                amount = bill.Amount,
                owner_role_id = bill.OwnerRoleId,
                payer_role_id = bill.PayerRoleId,
                bill_date = bill.BillDate?.ToString("yyyy-MM-dd"),
                is_extra = bill.IsExtra,
                marked_payer_role_id = bill.MarkedPayerRoleId,
                is_deleted = false
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{SupabaseUrl}/rest/v1/bills");

            ApplyAuthHeaders(request);
            // 让 Supabase 把插入后的完整记录（含 id）返回
            request.Headers.Add("Prefer", "return=representation");
            request.Content = content;

            var response = await _http.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var respJson = await response.Content.ReadAsStringAsync();

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            // Supabase REST 插入返回的是一个数组
            var list = JsonSerializer.Deserialize<List<Bill>>(respJson, options);
            return list?.FirstOrDefault();
        }

        public async Task<List<Bill>> GetBillsByRangeAsync(DateOnly startDate, DateOnly endExclusive)
        {
            var url =
                $"{SupabaseUrl}/rest/v1/bills" +
                $"?select=*" +
                $"&is_deleted=is.false" +
                $"&bill_date=gte.{startDate:yyyy-MM-dd}" +
                $"&bill_date=lt.{endExclusive:yyyy-MM-dd}" +
                $"&order=bill_date.desc,id.desc";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyAuthHeaders(request);

            var response = await _http.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            return JsonSerializer.Deserialize<List<Bill>>(json, options) ?? new List<Bill>();
        }
    }
}

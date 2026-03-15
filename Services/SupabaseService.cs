using BookkeepingBlazor.Models;
using BookkeepingBlazor.Pages;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

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

        // ================= 核心：带有返回值的 POST 请求 =================
        // 用于在插入数据后，要求 Supabase 直接返回插入成功的数据（包含自动生成的 ID）
        private async Task<T?> PostAndReturnAsync<T>(string relativeUrl, object payload)
        {
            var json = JsonSerializer.Serialize(payload);
            var request = new HttpRequestMessage(HttpMethod.Post, $"{SupabaseUrl}/rest/v1/{relativeUrl}");

            ApplyAuthHeaders(request);
            // 关键点：要求 Supabase 返回插入的数据表现 (Representation)
            request.Headers.Add("Prefer", "return=representation");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _http.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            // Supabase 即使是单条插入，默认也会返回一个数组
            var resultList = JsonSerializer.Deserialize<List<T>>(responseJson, options);
            return resultList != null && resultList.Count > 0 ? resultList[0] : default;
        }

        // ================= 分类增删改接口 =================

        // --- 主类别 ---
        public Task<MainCategoryInfo?> InsertMainCategoryAsync(MainCategoryInfo cat)
        {
            // 使用匿名对象防止传递 ID=0 干扰自增
            return PostAndReturnAsync<MainCategoryInfo>("main_categories", new
            {
                name = cat.Name,
                type = cat.Type, // 你已经加过这个字段了
                is_deleted = false
            });
        }

        // --- 主类别 ---
        public Task UpdateMainCategoryAsync(long id, string newName)
        {
            // 删除了 updated_at 字段
            return PatchAsync($"main_categories?id=eq.{id}", new { name = newName });
        }

        public Task SoftDeleteMainCategoryAsync(long id)
        {
            // 删除了 updated_at 字段
            return PatchAsync($"main_categories?id=eq.{id}", new { is_deleted = true });
        }

        // --- 子类别 ---
        public Task<SubCategoryInfo?> InsertSubCategoryAsync(SubCategoryInfo cat)
        {
            return PostAndReturnAsync<SubCategoryInfo>("sub_categories", new
            {
                main_category_id = cat.MainCategoryId,
                name = cat.Name,
                is_deleted = false
            });
        }

        // --- 子类别 ---
        public Task UpdateSubCategoryAsync(long id, string newName)
        {
            // 删除了 updated_at 字段
            return PatchAsync($"sub_categories?id=eq.{id}", new { name = newName });
        }

        public Task SoftDeleteSubCategoryAsync(long id)
        {
            // 删除了 updated_at 字段
            return PatchAsync($"sub_categories?id=eq.{id}", new { is_deleted = true });
        }

        public Task SoftDeleteSubCategoriesByMainIdAsync(long mainCategoryId)
        {
            // 利用 Supabase (PostgREST) 的特性，直接根据 main_category_id 批量更新
            return PatchAsync($"sub_categories?main_category_id=eq.{mainCategoryId}", new { is_deleted = true });
        }

        // --- 编辑 ---
        // 1. 获取单条账单数据（用于编辑时回显）
        public async Task<Bill?> GetBillByIdAsync(long id)
        {
            var list = await GetListAsync<Bill>($"bills?id=eq.{id}");
            return list.FirstOrDefault();
        }

        // 2. 修改现有的 InsertBillAsync，让它使用 PostAndReturnAsync 返回包含自增ID的完整数据
        public Task<Bill?> InsertBillAsync(Bill bill)
        {
            return PostAndReturnAsync<Bill>("bills", bill);
        }

        // 3. 更新账单
        public Task UpdateBillAsync(Bill bill)
        {
            return PatchAsync($"bills?id=eq.{bill.Id}", new
            {
                io_type = bill.IoType,
                main_category_id = bill.MainCategoryId,
                sub_category_id = bill.SubCategoryId,
                title = bill.Title,
                amount = bill.Amount,
                owner_role_id = bill.OwnerRoleId,
                payer_role_id = bill.PayerRoleId,
                bill_date = bill.BillDate?.ToString("yyyy-MM-dd"), // 日期转字符串
                is_extra = bill.IsExtra,
                marked_payer_role_id = bill.MarkedPayerRoleId,
                updated_at = DateTime.UtcNow
            });
        }
    }
}
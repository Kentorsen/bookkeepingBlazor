using BookkeepingBlazor.Models;
using BookkeepingBlazor.Pages;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using Microsoft.JSInterop;

namespace BookkeepingBlazor.Services
{
    public class SupabaseService
    {
        private readonly string SupabaseUrl;
        private readonly string SupabaseAnonKey;
        private readonly HttpClient _http;
        private readonly IJSRuntime _js; // 💡 新增 JS 运行时
        private string? _cachedToken; // 💡 用于在内存中记住 Token

        public SupabaseService(HttpClient http, IConfiguration config, IJSRuntime js)
        {
            _http = http;
            _js = js;
            SupabaseUrl = config["Supabase:Url"];
            SupabaseAnonKey = config["Supabase:Key"];
        }

        // 💡 核心修改：如果有用户 Token 就用用户的，没有才用匿名的
        private void ApplyAuthHeaders(HttpRequestMessage request)
        {
            request.Headers.Add("apikey", SupabaseAnonKey);
            var token = !string.IsNullOrEmpty(_cachedToken) ? _cachedToken : SupabaseAnonKey;
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        // ================= 新增：处理 Token 和登录检查 =================

        // 1. 检查网址里有没有邮箱跳回来带的 Token，或者本地缓存的 Token
        public async Task CheckHandleAuthTokenAsync()
        {
            var hash = await _js.InvokeAsync<string>("authHelper.getHashValue");
            if (!string.IsNullOrEmpty(hash) && hash.Contains("access_token="))
            {
                var token = hash.Split('&').FirstOrDefault(x => x.Contains("access_token="))?.Split('=')[1];
                if (!string.IsNullOrEmpty(token))
                {
                    _cachedToken = token;
                    await _js.InvokeVoidAsync("authHelper.setItem", "sb-token", token);
                    await _js.InvokeVoidAsync("authHelper.clearHash"); // 把网址擦干净
                }
            }
            else
            {
                _cachedToken = await _js.InvokeAsync<string>("authHelper.getItem", "sb-token");
            }
        }

        // 2. 验证当前是否已登录
        public async Task<bool> IsLoggedInAsync()
        {
            if (string.IsNullOrEmpty(_cachedToken)) return false;

            var request = new HttpRequestMessage(HttpMethod.Get, $"{SupabaseUrl}/auth/v1/user");
            ApplyAuthHeaders(request);
            var response = await _http.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                // 如果报错说明 Token 过期了，清理掉
                await _js.InvokeVoidAsync("authHelper.removeItem", "sb-token");
                _cachedToken = null;
                return false;
            }
            return true;
        }

        // 3. 解析出当前登录的邮箱
        private async Task<string?> GetCurrentUserEmailAsync()
        {
            if (string.IsNullOrEmpty(_cachedToken)) return null;

            var request = new HttpRequestMessage(HttpMethod.Get, $"{SupabaseUrl}/auth/v1/user");
            ApplyAuthHeaders(request);
            var response = await _http.SendAsync(request);

            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("email").GetString();
        }

        // 4. 拿着邮箱去 users 表里换取真实数字 ID
        public async Task<long> GetCurrentAppUserIdAsync()
        {
            var email = await GetCurrentUserEmailAsync();
            if (string.IsNullOrEmpty(email)) return 0;

            // 💡 利用 Supabase API 直接查询邮箱匹配的 ID
            var users = await GetListAsync<AppUser>($"users?email=eq.{email}&select=id");
            return users.FirstOrDefault()?.Id ?? 0;
        }

        // ================= 验证码登录流程 =================

        // 1. 发送 6 位数验证码
        public async Task SendOtpAsync(string email)
        {
            var payload = new { email = email };
            var json = JsonSerializer.Serialize(payload);

            var request = new HttpRequestMessage(HttpMethod.Post, $"{SupabaseUrl}/auth/v1/otp");
            request.Headers.Add("apikey", SupabaseAnonKey);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                throw new Exception($"发送失败: {err}");
            }
        }

        // 2. 拿着用户输入的验证码去校验
        public async Task<string?> VerifyOtpAsync(string email, string token)
        {
            // type = "email" 表示我们要验证的是邮箱的 6 位数字验证码
            var payload = new { type = "email", email = email, token = token };
            var json = JsonSerializer.Serialize(payload);

            var request = new HttpRequestMessage(HttpMethod.Post, $"{SupabaseUrl}/auth/v1/verify");
            request.Headers.Add("apikey", SupabaseAnonKey);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception("验证码错误或已过期！");
            }

            // 验证成功！提取 Token 并存起来
            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            var accessToken = doc.RootElement.GetProperty("access_token").GetString();

            if (!string.IsNullOrEmpty(accessToken))
            {
                _cachedToken = accessToken;
                await _js.InvokeVoidAsync("authHelper.setItem", "sb-token", accessToken);
            }
            return accessToken;
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
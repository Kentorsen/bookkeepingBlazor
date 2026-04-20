using BookkeepingBlazor.Models;
using BookkeepingBlazor.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace BookkeepingBlazor.Components.Bills;

public partial class BillsHomeView : IDisposable
{
    [Inject] private SupabaseService Supabase { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    private const string NameYiyi = "依依";
    private const string NameYiyi2 = "一一";
    private const string NameShared = "共同";

    private const long DefaultYiyiRoleId = 2;
    private const long DefaultYiyi2RoleId = 1;
    private const long DefaultSharedRoleId = 3;

    private const double LeftRevealWidth = 120;
    private const double RightRevealWidth = 60;
    private const double SwipeOpenThreshold = 34;

    private enum PeriodMode { Month, Day }
    private enum SummaryPage { Expense, Income }
    private enum SwipeSide { None, Left, Right }

    private bool ShowEditor;
    private long? EditingBillId;
    private long? HighlightedBillId;

    private sealed record BillDayGroup(DateOnly Date, List<Bill> Items, decimal Expense, decimal Income);

    private sealed class BillSummary
    {
        public decimal ExpenseYiyiPersonal { get; set; }
        public decimal ExpenseYiyi2Personal { get; set; }
        public decimal SharedExpense { get; set; }
        public decimal SharedExpenseHalf => SharedExpense / 2m;
        public decimal YiyiExpenseTotal => ExpenseYiyiPersonal + SharedExpenseHalf;
        public decimal Yiyi2ExpenseTotal => ExpenseYiyi2Personal + SharedExpenseHalf;
        public decimal IncomeYiyiPersonal { get; set; }
        public decimal IncomeYiyi2Personal { get; set; }
        public decimal SharedIncome { get; set; }
        public decimal SharedIncomeHalf => SharedIncome / 2m;
        public decimal YiyiIncomeTotal => IncomeYiyiPersonal + SharedIncomeHalf;
        public decimal Yiyi2IncomeTotal => IncomeYiyi2Personal + SharedIncomeHalf;
        public decimal YiyiBalance => YiyiIncomeTotal - YiyiExpenseTotal;
        public decimal Yiyi2Balance => Yiyi2IncomeTotal - Yiyi2ExpenseTotal;
    }

    private PeriodMode CurrentMode = PeriodMode.Month;
    private SummaryPage CurrentSummaryPage = SummaryPage.Expense;
    private DateOnly FocusedDate = DateOnly.FromDateTime(DateTime.Today);
    private bool IsLoading = true;
    private bool ShowPicker;
    private bool IgnoreNextClick;
    private bool IsSummaryCollapsed;
    private string MonthPickerValue = "";
    private string DayPickerValue = "";
    private CancellationTokenSource? LongPressCts;
    private CancellationTokenSource? ToastCts;
    private double SummaryPointerStartX;
    private bool SummaryPointerDown;
    private ElementReference RecordsScrollRef;
    private bool ListPullTracking;
    private double PullStartY;
    private double PullDistance;
    private bool IsRefreshingList;
    private long? DragBillId;
    private double DragStartX;
    private double DragStartY;
    private double DragCurrentOffset;
    private bool DragIsHorizontal;
    private double DragBaseOffset;
    private long? OpenBillId;
    private SwipeSide OpenSwipeSide = SwipeSide.None;
    private bool ShowMarkRoleSheet;
    private Bill? MarkSheetBill;
    private bool ShowToast;
    private string ToastMessage = "";
    private List<Bill> BillItems = new();
    private BillSummary Summary = new();
    private List<RoleInfo> RoleItems = new();
    private Dictionary<long, string> RoleNameMap = new() { { 1, "一一" }, { 2, "依依" }, { 3, "共同" }, { 4, "草菠" } };
    private Dictionary<long, string> UserNameMap = new() { { 1, "一一" }, { 2, "依依" } };
    private Dictionary<long, string> MainCategoryNameMap = new();
    private Dictionary<long, string> SubCategoryNameMap = new();
    private long YiyiRoleId = DefaultYiyiRoleId;
    private long Yiyi2RoleId = DefaultYiyi2RoleId;
    private long SharedRoleId = DefaultSharedRoleId;
    private DateTime InitialDateForAdd = DateTime.Today;

    private string CurrentDisplayTitle =>
        CurrentMode == PeriodMode.Month
            ? $"{FocusedDate.Year}年{FocusedDate.Month:D2}月"
            : $"{FocusedDate.Year}年{FocusedDate.Month:D2}月{FocusedDate.Day:D2}日";

    private List<BillDayGroup> GroupedBills =>
        BillItems.OrderByDescending(x => x.BillDate).ThenByDescending(x => x.Id)
            .GroupBy(x => GetBillDate(x))
            .Select(g => new BillDayGroup(g.Key, g.ToList(), g.Where(x => x.IoType == 1).Sum(x => x.Amount), g.Where(x => x.IoType == 2).Sum(x => x.Amount)))
            .ToList();

    protected override async Task OnInitializedAsync()
    {
        await Supabase.CheckHandleAuthTokenAsync();
        var isLoggedIn = await Supabase.IsLoggedInAsync();
        if (!isLoggedIn)
        {
            Nav.NavigateTo("/login");
            return;
        }

        SyncPickerValue();
        await LoadReferenceDataAsync();
        await LoadBillsAsync();
    }

    public async Task RefreshFromShellAsync()
    {
        await RefreshBillsAsync();
    }

    public Task OpenAddFromShellAsync()
    {
        OpenAddBill();
        return Task.CompletedTask;
    }

    public async Task ShowShellToastAsync(string message)
    {
        await ShowToastAsync(message);
    }

    private async Task LoadReferenceDataAsync()
    {
        try
        {
            var roles = await Supabase.GetRolesAsync();
            if (roles.Count > 0)
            {
                RoleItems = roles.Where(x => !x.IsDeleted).ToList();
                RoleNameMap = RoleItems
                    .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                    .GroupBy(x => x.Id)
                    .ToDictionary(g => g.Key, g => g.First().Name!);

                YiyiRoleId = FindRoleIdByName(NameYiyi, DefaultYiyiRoleId);
                Yiyi2RoleId = FindRoleIdByName(NameYiyi2, DefaultYiyi2RoleId);
                SharedRoleId = FindRoleIdByName(NameShared, DefaultSharedRoleId);
            }
        }
        catch
        {
        }

        try
        {
            var users = await Supabase.GetUsersAsync();
            if (users.Count > 0)
            {
                UserNameMap = users
                    .Where(x => !x.IsDeleted && !string.IsNullOrWhiteSpace(x.Name))
                    .GroupBy(x => x.Id)
                    .ToDictionary(g => g.Key, g => g.First().Name!);
            }
        }
        catch
        {
        }

        try
        {
            var mainCategories = await Supabase.GetMainCategoriesAsync();
            if (mainCategories.Count > 0)
            {
                MainCategoryNameMap = mainCategories
                    .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                    .GroupBy(x => x.Id)
                    .ToDictionary(g => g.Key, g => g.First().Name!);
            }
        }
        catch
        {
        }

        try
        {
            var subCategories = await Supabase.GetSubCategoriesAsync();
            if (subCategories.Count > 0)
            {
                SubCategoryNameMap = subCategories
                    .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                    .GroupBy(x => x.Id)
                    .ToDictionary(g => g.Key, g => g.First().Name!);
            }
        }
        catch
        {
        }
    }

    private long FindRoleIdByName(string name, long fallbackId)
    {
        var found = RoleNameMap.FirstOrDefault(x => x.Value == name);
        return found.Key > 0 ? found.Key : fallbackId;
    }

    private async Task LoadBillsAsync()
    {
        IsLoading = true;
        StateHasChanged();

        try
        {
            var (start, endExclusive) = GetRange();
            BillItems = await Supabase.GetBillsByRangeAsync(start, endExclusive);
            Summary = BuildSummary(BillItems);
            CloseSwipeImmediate();
        }
        finally
        {
            IsLoading = false;
            StateHasChanged();
        }
    }

    private async Task RefreshBillsAsync()
    {
        await LoadBillsAsync();
        await ShowToastAsync("账单已刷新");
    }

    private (DateOnly Start, DateOnly EndExclusive) GetRange()
    {
        if (CurrentMode == PeriodMode.Month)
        {
            var start = new DateOnly(FocusedDate.Year, FocusedDate.Month, 1);
            return (start, start.AddMonths(1));
        }

        return (FocusedDate, FocusedDate.AddDays(1));
    }

    private BillSummary BuildSummary(List<Bill> source)
    {
        var result = new BillSummary();
        var expenses = source.Where(x => x.IoType == 1).ToList();
        var incomes = source.Where(x => x.IoType == 2).ToList();

        result.ExpenseYiyiPersonal = expenses.Where(x => x.PayerRoleId == YiyiRoleId).Sum(x => x.Amount);
        result.ExpenseYiyi2Personal = expenses.Where(x => x.PayerRoleId == Yiyi2RoleId).Sum(x => x.Amount);
        result.SharedExpense = expenses.Where(x => x.PayerRoleId == SharedRoleId).Sum(x => x.Amount);
        result.IncomeYiyiPersonal = incomes.Where(x => x.PayerRoleId == YiyiRoleId).Sum(x => x.Amount);
        result.IncomeYiyi2Personal = incomes.Where(x => x.PayerRoleId == Yiyi2RoleId).Sum(x => x.Amount);
        result.SharedIncome = incomes.Where(x => x.PayerRoleId == SharedRoleId).Sum(x => x.Amount);
        return result;
    }

    private DateOnly GetBillDate(Bill bill) =>
        bill.BillDate.HasValue ? DateOnly.FromDateTime(bill.BillDate.Value) : FocusedDate;

    private async Task GoPrevious()
    {
        FocusedDate = CurrentMode == PeriodMode.Month ? FocusedDate.AddMonths(-1) : FocusedDate.AddDays(-1);
        SyncPickerValue();
        ShowPicker = false;
        await LoadBillsAsync();
    }

    private async Task GoNext()
    {
        FocusedDate = CurrentMode == PeriodMode.Month ? FocusedDate.AddMonths(1) : FocusedDate.AddDays(1);
        SyncPickerValue();
        ShowPicker = false;
        await LoadBillsAsync();
    }

    private void SyncPickerValue()
    {
        MonthPickerValue = $"{FocusedDate:yyyy-MM}";
        DayPickerValue = $"{FocusedDate:yyyy-MM-dd}";
    }

    private async Task HandleDatePointerDown()
    {
        LongPressCts?.Cancel();
        LongPressCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(600, LongPressCts.Token);
            CurrentMode = CurrentMode == PeriodMode.Month ? PeriodMode.Day : PeriodMode.Month;
            SyncPickerValue();
            await LoadBillsAsync();
            IgnoreNextClick = true;
            StateHasChanged();
        }
        catch (TaskCanceledException)
        {
        }
    }

    private void HandleDatePointerUp() => LongPressCts?.Cancel();
    private void HandleDatePointerLeave() => LongPressCts?.Cancel();

    private void HandleDateAreaClick()
    {
        if (IgnoreNextClick)
        {
            IgnoreNextClick = false;
            return;
        }

        ShowPicker = !ShowPicker;
        SyncPickerValue();
    }

    private void OnMonthPickerChanged(ChangeEventArgs e) => MonthPickerValue = e.Value?.ToString() ?? "";
    private void OnDayPickerChanged(ChangeEventArgs e) => DayPickerValue = e.Value?.ToString() ?? "";

    private async Task ConfirmPickerAsync()
    {
        if (CurrentMode == PeriodMode.Month)
        {
            if (!string.IsNullOrWhiteSpace(MonthPickerValue))
            {
                var parts = MonthPickerValue.Split('-');
                if (parts.Length == 2 && int.TryParse(parts[0], out var year) && int.TryParse(parts[1], out var month))
                {
                    var day = Math.Min(FocusedDate.Day, DateTime.DaysInMonth(year, month));
                    FocusedDate = new DateOnly(year, month, day);
                }
            }
        }
        else if (DateOnly.TryParse(DayPickerValue, out var selectedDate))
        {
            FocusedDate = selectedDate;
        }

        ShowPicker = false;
        await LoadBillsAsync();
    }

    private void CancelPicker()
    {
        ShowPicker = false;
        SyncPickerValue();
    }

    private void ToggleSummaryCollapsed() => IsSummaryCollapsed = !IsSummaryCollapsed;
    private void SetSummaryPage(SummaryPage page) => CurrentSummaryPage = page;

    private void HandleSummaryPointerDown(PointerEventArgs e)
    {
        SummaryPointerDown = true;
        SummaryPointerStartX = e.ClientX;
    }

    private void HandleSummaryPointerLeave() => SummaryPointerDown = false;

    private void HandleSummaryPointerUp(PointerEventArgs e)
    {
        if (!SummaryPointerDown) return;
        var delta = e.ClientX - SummaryPointerStartX;
        SummaryPointerDown = false;
        if (Math.Abs(delta) < 35) return;
        CurrentSummaryPage = delta < 0 ? SummaryPage.Income : SummaryPage.Expense;
    }

    private async Task HandleListPointerDown(PointerEventArgs e)
    {
        if (ShowMarkRoleSheet) return;
        var scrollTop = await JS.InvokeAsync<double>("bookkeeping.getScrollTop", RecordsScrollRef);
        if (scrollTop <= 1)
        {
            ListPullTracking = true;
            PullStartY = e.ClientY;
            PullDistance = 0;
        }
    }

    private void HandleListPointerMove(PointerEventArgs e)
    {
        if (!ListPullTracking || IsRefreshingList) return;
        var delta = e.ClientY - PullStartY;
        PullDistance = delta > 0 ? Math.Min(delta * 0.6, 76) : 0;
        StateHasChanged();
    }

    private async Task HandleListPointerUp(PointerEventArgs e)
    {
        if (!ListPullTracking) return;
        ListPullTracking = false;

        if (PullDistance < 56)
        {
            PullDistance = 0;
            StateHasChanged();
            return;
        }

        IsRefreshingList = true;
        PullDistance = 46;
        StateHasChanged();

        try
        {
            await LoadBillsAsync();
        }
        finally
        {
            await Task.Delay(300);
            IsRefreshingList = false;
            PullDistance = 0;
            StateHasChanged();
        }
    }

    private void HandleRowPointerDown(long billId, PointerEventArgs e)
    {
        if (OpenBillId.HasValue && OpenBillId != billId)
        {
            CloseSwipeImmediate();
        }
        else if (OpenBillId.HasValue && OpenBillId == billId)
        {
            CloseSwipeImmediate();
            return;
        }

        DragBillId = billId;
        DragStartX = e.ClientX;
        DragStartY = e.ClientY;
        DragIsHorizontal = false;
        DragBaseOffset = GetBillOffset(billId);
        DragCurrentOffset = DragBaseOffset;

        if (OpenBillId.HasValue && OpenBillId.Value != billId)
        {
            CloseSwipeImmediate();
            DragBaseOffset = 0;
        }
    }

    private void HandleRowPointerMove(long billId, PointerEventArgs e)
    {
        if (DragBillId != billId) return;

        var dx = e.ClientX - DragStartX;
        var dy = e.ClientY - DragStartY;

        if (!DragIsHorizontal)
        {
            if (Math.Abs(dy) > 6 && Math.Abs(dy) > Math.Abs(dx) * 1.2)
            {
                DragBillId = null;
                DragCurrentOffset = 0;
                DragBaseOffset = 0;
                DragIsHorizontal = false;
                return;
            }

            if (Math.Abs(dx) < 14 || Math.Abs(dx) < Math.Abs(dy) + 4)
            {
                return;
            }

            DragIsHorizontal = true;
        }

        DragCurrentOffset = Math.Clamp(DragBaseOffset + dx, -LeftRevealWidth, RightRevealWidth);
        StateHasChanged();
    }

    private void HandleRowPointerUp(long billId, PointerEventArgs e)
    {
        if (DragBillId != billId) return;

        if (!DragIsHorizontal)
        {
            DragBillId = null;
            DragCurrentOffset = 0;
            DragBaseOffset = 0;
            return;
        }

        var finalOffset = DragCurrentOffset;

        if (finalOffset <= -SwipeOpenThreshold)
        {
            OpenBillId = billId;
            OpenSwipeSide = SwipeSide.Left;
        }
        else if (finalOffset >= SwipeOpenThreshold)
        {
            OpenBillId = billId;
            OpenSwipeSide = SwipeSide.Right;
        }
        else
        {
            CloseSwipeImmediate();
        }

        DragBillId = null;
        DragCurrentOffset = 0;
        DragBaseOffset = 0;
        DragIsHorizontal = false;
        StateHasChanged();
    }

    private double GetBillOffset(long billId)
    {
        if (DragBillId == billId) return DragCurrentOffset;
        if (OpenBillId == billId)
        {
            return OpenSwipeSide switch
            {
                SwipeSide.Left => -LeftRevealWidth,
                SwipeSide.Right => RightRevealWidth,
                _ => 0
            };
        }

        return 0;
    }

    private string GetRowTransformStyle(long billId) => $"transform: translate3d({GetBillOffset(billId)}px,0,0);";

    private string GetShellStateClass(long billId)
    {
        if (OpenBillId == billId) return "revealed";
        if (DragBillId == billId && DragIsHorizontal && Math.Abs(DragCurrentOffset) > 2) return "revealed";
        return "";
    }

    private void CloseSwipeImmediate()
    {
        OpenBillId = null;
        DragBillId = null;
        DragCurrentOffset = 0;
        DragBaseOffset = 0;
        OpenSwipeSide = SwipeSide.None;
        StateHasChanged();
    }

    private void OpenMarkSheet(Bill bill)
    {
        MarkSheetBill = bill;
        ShowMarkRoleSheet = true;
        CloseSwipeImmediate();
    }

    private void CloseMarkSheet()
    {
        ShowMarkRoleSheet = false;
        MarkSheetBill = null;
    }

    private IEnumerable<RoleInfo> GetMarkCandidates(Bill bill)
    {
        IEnumerable<RoleInfo> source = RoleItems.Count > 0
            ? RoleItems
            : RoleNameMap.Select(x => new RoleInfo
            {
                Id = x.Key,
                Name = x.Value,
                RoleType = x.Key == SharedRoleId ? "group" : "human",
                IsDeleted = false
            });

        return source
            .Where(x =>
                !x.IsDeleted &&
                !string.IsNullOrWhiteSpace(x.Name) &&
                !string.IsNullOrWhiteSpace(x.RoleType) &&
                (x.RoleType.Equals("human", StringComparison.OrdinalIgnoreCase) ||
                 x.RoleType.Equals("group", StringComparison.OrdinalIgnoreCase)) &&
                x.Id != bill.PayerRoleId)
            .OrderBy(x => x.Id);
    }

    private async Task MarkToRoleAsync(long billId, long roleId)
    {
        var currentUserId = await Supabase.GetCurrentAppUserIdAsync();
        await Supabase.MarkBillAsync(billId, roleId, currentUserId);

        var bill = BillItems.FirstOrDefault(b => b.Id == billId);
        if (bill is not null)
        {
            bill.MarkedPayerRoleId = roleId;
            bill.UpdatedBy = currentUserId;
            bill.UpdatedAt = DateTime.UtcNow;
        }

        CloseMarkSheet();
        await ShowToastAsync("已标记");
        StateHasChanged();
    }

    private async Task CancelMarkAsync(long billId)
    {
        var currentUserId = await Supabase.GetCurrentAppUserIdAsync();
        await Supabase.ClearBillMarkAsync(billId, currentUserId);

        var bill = BillItems.FirstOrDefault(b => b.Id == billId);
        if (bill is not null)
        {
            bill.MarkedPayerRoleId = 0;
            bill.UpdatedBy = currentUserId;
            bill.UpdatedAt = DateTime.UtcNow;
        }

        CloseSwipeImmediate();
        await ShowToastAsync("已取消标记");
        StateHasChanged();
    }

    private async Task DeleteBillAsync(long billId)
    {
        var ok = await JS.InvokeAsync<bool>("confirm", "确定删除这条账单吗？");
        if (!ok) return;

        var currentUserId = await Supabase.GetCurrentAppUserIdAsync();
        await Supabase.SoftDeleteBillAsync(billId, currentUserId);
        CloseSwipeImmediate();
        await LoadBillsAsync();
        await ShowToastAsync("已删除");
    }

    private async Task ShowToastAsync(string message)
    {
        ToastMessage = message;
        ShowToast = true;
        StateHasChanged();

        ToastCts?.Cancel();
        ToastCts = new CancellationTokenSource();

        try
        {
            await Task.Delay(1800, ToastCts.Token);
            ShowToast = false;
            StateHasChanged();
        }
        catch (TaskCanceledException)
        {
        }
    }

    private string Fmt(decimal value) => value.ToString("0.##");
    private string GetTitle(Bill bill) => string.IsNullOrWhiteSpace(bill.Title) ? "未命名账单" : bill.Title!;
    private string GetRoleName(long roleId) => RoleNameMap.TryGetValue(roleId, out var name) ? name : "-";
    private string GetUserName(long userId) => userId == 0 ? "-" : (UserNameMap.TryGetValue(userId, out var name) ? name : "-");
    private string GetMainCategoryName(long categoryId) => MainCategoryNameMap.TryGetValue(categoryId, out var name) ? name : "未分类";
    private bool HasSubCategoryName(long categoryId) => SubCategoryNameMap.ContainsKey(categoryId);
    private string GetSubCategoryName(long categoryId) => SubCategoryNameMap.TryGetValue(categoryId, out var name) ? name : "";

    private string GetDayBarClass(DateOnly date)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (date == today) return "today";
        var day = date.DayOfWeek;
        return day is DayOfWeek.Saturday or DayOfWeek.Sunday ? "weekend" : "";
    }

    private string GetDayTitle(DateOnly date) => $"{date.Year}年{date.Month:D2}月{date.Day:D2}日";

    private string GetWeekText(DateOnly date) => date.DayOfWeek switch
    {
        DayOfWeek.Monday => "周一",
        DayOfWeek.Tuesday => "周二",
        DayOfWeek.Wednesday => "周三",
        DayOfWeek.Thursday => "周四",
        DayOfWeek.Friday => "周五",
        DayOfWeek.Saturday => "周六",
        DayOfWeek.Sunday => "周日",
        _ => ""
    };

    public void Dispose()
    {
        LongPressCts?.Cancel();
        LongPressCts?.Dispose();
        ToastCts?.Cancel();
        ToastCts?.Dispose();
    }

    private void OpenAddBill()
    {
        EditingBillId = null;
        InitialDateForAdd = CurrentMode == PeriodMode.Month
            ? DateTime.Today
            : FocusedDate.ToDateTime(TimeOnly.MinValue);
        ShowEditor = true;
    }

    private void OpenEditBill(Bill bill)
    {
        EditingBillId = bill.Id;
        CloseSwipeImmediate();
        ShowEditor = true;
    }

    private void CloseEditor()
    {
        ShowEditor = false;
    }

    private Task HandleEditorSaved(Bill savedBill)
    {
        ShowEditor = false;

        if (EditingBillId.HasValue && EditingBillId.Value > 0)
        {
            var index = BillItems.FindIndex(b => b.Id == savedBill.Id);
            if (index >= 0) BillItems[index] = savedBill;
        }
        else
        {
            BillItems.Add(savedBill);
        }

        BillItems = BillItems.OrderByDescending(b => b.BillDate)
            .ThenByDescending(b => b.CreatedAt)
            .ToList();

        HighlightedBillId = savedBill.Id;
        StateHasChanged();
        _ = ShowToastAsync("保存成功");

        _ = Task.Run(async () =>
        {
            await Task.Delay(500);
            HighlightedBillId = null;
            await InvokeAsync(StateHasChanged);
        });

        return Task.CompletedTask;
    }
}

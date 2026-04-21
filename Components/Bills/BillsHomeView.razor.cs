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
    private enum ScreenMode { Home, Filter }
    private enum FilterTab { Condition, Marked }
    private enum IoFilter { Expense, Income, All }
    private enum TriState { No, Yes, All }
    private enum ConditionField
    {
        IoType,
        Amount,
        Category,
        Name,
        Date,
        IsExtra,
        Owner,
        Payer,
        Marked
    }

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
    private List<MainCategoryInfo> MainCategoryItems = new();
    private List<SubCategoryInfo> SubCategoryItems = new();
    private long YiyiRoleId = DefaultYiyiRoleId;
    private long Yiyi2RoleId = DefaultYiyi2RoleId;
    private long SharedRoleId = DefaultSharedRoleId;
    private DateTime InitialDateForAdd = DateTime.Today;
    private List<Bill> ConditionFilteredBills = new();
    private List<Bill> MarkedFilteredBills = new();
    private List<Bill> FilterAllBills = new();
    private bool IsFilterDataLoaded;
    private bool ShowConditionBuilder;
    private HashSet<ConditionField> ActiveConditionFields = new();
    private HashSet<ConditionField> DraftConditionFields = new();
    private IoFilter DraftConditionIoFilter = IoFilter.Expense;
    private decimal? DraftFilterMinAmount;
    private decimal? DraftFilterMaxAmount;
    private string DraftFilterKeyword = "";
    private DateOnly? DraftFilterStartDate;
    private DateOnly? DraftFilterEndDate;
    private TriState DraftExtraFilter = TriState.All;
    private TriState DraftMarkedFilter = TriState.All;
    private long? DraftFilterOwnerRoleId;
    private long? DraftFilterPayerRoleId;
    private HashSet<long> DraftExpenseMainCategoryIds = new();
    private HashSet<long> DraftExpenseSubCategoryIds = new();
    private HashSet<long> DraftIncomeMainCategoryIds = new();
    private HashSet<long> DraftIncomeSubCategoryIds = new();
    private ScreenMode CurrentScreenMode = ScreenMode.Home;
    private FilterTab CurrentFilterTab = FilterTab.Condition;
    private bool IsFilterConditionCollapsed;
    private bool IsFilterStatsCollapsed;
    private bool IsConditionFilterApplied;
    private bool IsMarkedFilterApplied;
    private decimal? FilterMinAmount;
    private decimal? FilterMaxAmount;
    private string FilterKeyword = "";
    private DateOnly? FilterStartDate;
    private DateOnly? FilterEndDate;
    private IoFilter ConditionIoFilter = IoFilter.Expense;
    private IoFilter MarkIoFilter = IoFilter.Expense;
    private TriState ExtraFilter = TriState.All;
    private TriState MarkedFilter = TriState.No;
    private long? FilterOwnerRoleId;
    private long? FilterPayerRoleId;
    private HashSet<long> ExpenseMainCategoryIds = new();
    private HashSet<long> ExpenseSubCategoryIds = new();
    private HashSet<long> IncomeMainCategoryIds = new();
    private HashSet<long> IncomeSubCategoryIds = new();
    private sealed record MarkSettlement(decimal YiyiNeedPayYiyi2)
    {
        public decimal Yiyi2NeedPayYiyi => -YiyiNeedPayYiyi2;
    }

    private string CurrentDisplayTitle =>
        CurrentMode == PeriodMode.Month
            ? $"{FocusedDate.Year}年{FocusedDate.Month:D2}月"
            : $"{FocusedDate.Year}年{FocusedDate.Month:D2}月{FocusedDate.Day:D2}日";

    private IReadOnlyList<Bill> DisplayedBills =>
        CurrentScreenMode == ScreenMode.Filter
            ? (CurrentFilterTab == FilterTab.Condition
                ? (IsConditionFilterApplied ? ConditionFilteredBills : Array.Empty<Bill>())
                : (IsMarkedFilterApplied ? MarkedFilteredBills : Array.Empty<Bill>()))
            : BillItems;
    private bool IsCurrentFilterApplied =>
        CurrentFilterTab == FilterTab.Condition ? IsConditionFilterApplied : IsMarkedFilterApplied;
    private bool CanConfirmConditionBuilder => DraftConditionFields.Count > 0;
    private bool IsDraftExpenseCategoryEnabled => DraftConditionIoFilter != IoFilter.Income;
    private bool IsDraftIncomeCategoryEnabled => DraftConditionIoFilter != IoFilter.Expense;
    private BillSummary CurrentSummaryData =>
        CurrentScreenMode == ScreenMode.Filter ? FilterSummary : Summary;

    private List<BillDayGroup> GroupedBills =>
        DisplayedBills.OrderByDescending(x => x.BillDate).ThenByDescending(x => x.Id)
            .GroupBy(x => GetBillDate(x))
            .Select(g => new BillDayGroup(g.Key, g.ToList(), g.Where(x => x.IoType == 1).Sum(x => x.Amount), g.Where(x => x.IoType == 2).Sum(x => x.Amount)))
            .ToList();

    private BillSummary FilterSummary => BuildSummary(ConditionFilteredBills);
    private MarkSettlement FilterSettlement => BuildMarkSettlement(MarkedFilteredBills);

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
        ResetFilterDefaults();
        await LoadBillsAsync();
    }

    public async Task RefreshFromShellAsync()
    {
        await RefreshBillsAsync();
    }

    public async Task OpenAddFromShellAsync()
    {
        OpenAddBill();
        await InvokeAsync(StateHasChanged);
    }

    public async Task ToggleFilterModeFromShellAsync()
    {
        if (CurrentScreenMode == ScreenMode.Filter)
        {
            CurrentScreenMode = ScreenMode.Home;
        }
        else
        {
            CurrentScreenMode = ScreenMode.Filter;
            if (FilterStartDate is null || FilterEndDate is null)
            {
                ResetFilterDefaults();
            }
            await EnsureFilterBillsLoadedAsync();
        }

        CloseSwipeImmediate();
        StateHasChanged();
    }

    public async Task ShowShellToastAsync(string message)
    {
        await ShowToastAsync(message);
    }

    private void ResetFilterDefaults()
    {
        var now = DateTime.Today;
        var monthStart = new DateOnly(now.Year, now.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        FilterStartDate = monthStart;
        FilterEndDate = monthEnd;
        ConditionIoFilter = IoFilter.Expense;
        MarkIoFilter = IoFilter.Expense;
        ExtraFilter = TriState.All;
        MarkedFilter = TriState.All;
        FilterOwnerRoleId = null;
        FilterPayerRoleId = null;
        FilterKeyword = "";
        FilterMinAmount = null;
        FilterMaxAmount = null;
        ExpenseMainCategoryIds.Clear();
        ExpenseSubCategoryIds.Clear();
        IncomeMainCategoryIds.Clear();
        IncomeSubCategoryIds.Clear();
        ActiveConditionFields.Clear();
        IsConditionFilterApplied = false;
        IsMarkedFilterApplied = false;
        ConditionFilteredBills.Clear();
        MarkedFilteredBills.Clear();
    }

    private void OpenConditionBuilder()
    {
        DraftConditionFields = ActiveConditionFields.ToHashSet();
        DraftConditionIoFilter = ConditionIoFilter;
        DraftFilterMinAmount = FilterMinAmount;
        DraftFilterMaxAmount = FilterMaxAmount;
        DraftFilterKeyword = FilterKeyword;
        DraftFilterStartDate = FilterStartDate;
        DraftFilterEndDate = FilterEndDate;
        DraftExtraFilter = ExtraFilter;
        DraftMarkedFilter = MarkedFilter;
        DraftFilterOwnerRoleId = FilterOwnerRoleId;
        DraftFilterPayerRoleId = FilterPayerRoleId;
        DraftExpenseMainCategoryIds = ExpenseMainCategoryIds.ToHashSet();
        DraftExpenseSubCategoryIds = ExpenseSubCategoryIds.ToHashSet();
        DraftIncomeMainCategoryIds = IncomeMainCategoryIds.ToHashSet();
        DraftIncomeSubCategoryIds = IncomeSubCategoryIds.ToHashSet();
        ShowConditionBuilder = true;
    }

    private void CancelConditionBuilder()
    {
        ShowConditionBuilder = false;
    }

    private async Task ConfirmConditionBuilderAsync()
    {
        if (!CanConfirmConditionBuilder) return;
        await EnsureFilterBillsLoadedAsync();

        ActiveConditionFields = DraftConditionFields.ToHashSet();
        ConditionIoFilter = DraftConditionIoFilter;
        FilterMinAmount = DraftFilterMinAmount;
        FilterMaxAmount = DraftFilterMaxAmount;
        FilterKeyword = DraftFilterKeyword;
        FilterStartDate = DraftFilterStartDate;
        FilterEndDate = DraftFilterEndDate;
        ExtraFilter = DraftExtraFilter;
        MarkedFilter = DraftMarkedFilter;
        FilterOwnerRoleId = DraftFilterOwnerRoleId;
        FilterPayerRoleId = DraftFilterPayerRoleId;
        ExpenseMainCategoryIds = DraftExpenseMainCategoryIds.ToHashSet();
        ExpenseSubCategoryIds = DraftExpenseSubCategoryIds.ToHashSet();
        IncomeMainCategoryIds = DraftIncomeMainCategoryIds.ToHashSet();
        IncomeSubCategoryIds = DraftIncomeSubCategoryIds.ToHashSet();

        if (!ActiveConditionFields.Contains(ConditionField.Marked))
        {
            MarkedFilter = TriState.All;
        }

        ShowConditionBuilder = false;
        CurrentFilterTab = FilterTab.Condition;
        IsConditionFilterApplied = true;
        ConditionFilteredBills = ApplyFilter(GetFilterSourceBills(), FilterTab.Condition).ToList();
        await ShowToastAsync("筛选已应用");
    }

    private async Task RemoveConditionFieldAsync(ConditionField field)
    {
        if (!ActiveConditionFields.Remove(field)) return;
        await EnsureFilterBillsLoadedAsync();

        if (ActiveConditionFields.Count == 0)
        {
            IsConditionFilterApplied = false;
            ConditionFilteredBills.Clear();
            await ShowToastAsync("已移除全部条件");
            return;
        }

        ConditionFilteredBills = ApplyFilter(GetFilterSourceBills(), FilterTab.Condition).ToList();
        await ShowToastAsync("已更新条件");
    }

    private IEnumerable<(ConditionField Field, string Label)> GetConditionTags()
    {
        foreach (var field in ActiveConditionFields)
        {
            var label = GetConditionTagLabel(field);
            if (!string.IsNullOrWhiteSpace(label))
            {
                yield return (field, label);
            }
        }
    }

    private string GetConditionTagLabel(ConditionField field)
    {
        return field switch
        {
            ConditionField.IoType => $"类型：{GetIoFilterText(ConditionIoFilter)}",
            ConditionField.Amount => $"金额：{FilterMinAmount?.ToString() ?? "-"}~{FilterMaxAmount?.ToString() ?? "-"}",
            ConditionField.Category => BuildCategoryTagLabel(),
            ConditionField.Name => $"名称：{(string.IsNullOrWhiteSpace(FilterKeyword) ? "-" : FilterKeyword)}",
            ConditionField.Date => $"日期：{FilterStartDate?.ToString("yyyy/MM/dd") ?? "-"}~{FilterEndDate?.ToString("yyyy/MM/dd") ?? "-"}",
            ConditionField.IsExtra => $"额外：{GetTriStateText(ExtraFilter)}",
            ConditionField.Owner => $"所属方：{GetRoleName(FilterOwnerRoleId ?? 0)}",
            ConditionField.Payer => $"付款/收款方：{GetRoleName(FilterPayerRoleId ?? 0)}",
            ConditionField.Marked => $"标记：{GetTriStateText(MarkedFilter)}",
            _ => string.Empty
        };
    }

    private string BuildCategoryTagLabel()
    {
        var mainSet = ConditionIoFilter == IoFilter.Income ? IncomeMainCategoryIds : ExpenseMainCategoryIds;
        var subSet = ConditionIoFilter == IoFilter.Income ? IncomeSubCategoryIds : ExpenseSubCategoryIds;
        if (mainSet.Count == 0) return "类别：-";
        if (mainSet.Count >= 3) return $"类别：{mainSet.Count}项";
        if (mainSet.Count == 2)
        {
            var names = mainSet.Select(GetMainCategoryName).ToList();
            return $"类别：{string.Join('、', names)}";
        }

        var mainId = mainSet.First();
        var mainName = GetMainCategoryName(mainId);
        if (subSet.Count == 0) return $"类别：{mainName}";
        if (subSet.Count == 1) return $"类别：{mainName}-{GetSubCategoryName(subSet.First())}";
        if (subSet.Count == 2)
        {
            var n = subSet.Select(GetSubCategoryName).ToList();
            return $"类别：{mainName}-{n[0]}、{n[1]}";
        }

        return $"类别：{mainName}等{subSet.Count}项";
    }

    private static string GetIoFilterText(IoFilter value) => value switch
    {
        IoFilter.Expense => "支出",
        IoFilter.Income => "收入",
        _ => "全部"
    };

    private static string GetTriStateText(TriState value) => value switch
    {
        TriState.Yes => "是",
        TriState.No => "否",
        _ => "全部"
    };

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
                MainCategoryItems = mainCategories.ToList();
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
                SubCategoryItems = subCategories.ToList();
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
            RebuildAppliedFilterResults();
            CloseSwipeImmediate();
        }
        finally
        {
            IsLoading = false;
            StateHasChanged();
        }
    }

    private void RebuildAppliedFilterResults()
    {
        var source = GetFilterSourceBills();
        if (IsConditionFilterApplied)
        {
            ConditionFilteredBills = ApplyFilter(source, FilterTab.Condition).ToList();
        }

        if (IsMarkedFilterApplied)
        {
            MarkedFilteredBills = ApplyFilter(source, FilterTab.Marked).ToList();
        }
    }

    private IEnumerable<Bill> GetFilterSourceBills() =>
        IsFilterDataLoaded ? FilterAllBills : BillItems;

    private async Task EnsureFilterBillsLoadedAsync(bool force = false)
    {
        if (IsFilterDataLoaded && !force) return;
        FilterAllBills = await Supabase.GetAllBillsAsync();
        IsFilterDataLoaded = true;
        RebuildAppliedFilterResults();
        await InvokeAsync(StateHasChanged);
    }

    private async Task RefreshBillsAsync()
    {
        await LoadBillsAsync();
        if (CurrentScreenMode == ScreenMode.Filter || IsConditionFilterApplied || IsMarkedFilterApplied)
        {
            await EnsureFilterBillsLoadedAsync(true);
        }
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
        var now = DateTime.UtcNow;
        UpdateBillMarkLocally(billId, roleId, currentUserId, now);
        CloseMarkSheet();
        RecalculateLocalState();
        await ShowToastAsync("已标记");
    }

    private async Task CancelMarkAsync(long billId)
    {
        var currentUserId = await Supabase.GetCurrentAppUserIdAsync();
        await Supabase.ClearBillMarkAsync(billId, currentUserId);
        var now = DateTime.UtcNow;
        UpdateBillMarkLocally(billId, 0, currentUserId, now);
        CloseSwipeImmediate();
        RecalculateLocalState();
        await ShowToastAsync("已取消标记");
    }

    private async Task DeleteBillAsync(long billId)
    {
        var ok = await JS.InvokeAsync<bool>("confirm", "确定删除这条账单吗？");
        if (!ok) return;

        var currentUserId = await Supabase.GetCurrentAppUserIdAsync();
        await Supabase.SoftDeleteBillAsync(billId, currentUserId);
        BillItems.RemoveAll(b => b.Id == billId);
        if (IsFilterDataLoaded)
        {
            FilterAllBills.RemoveAll(b => b.Id == billId);
        }
        CloseSwipeImmediate();
        RecalculateLocalState();
        await ShowToastAsync("已删除");
    }

    private void UpdateBillMarkLocally(long billId, long markedPayerRoleId, long updatedBy, DateTime updatedAt)
    {
        var sourceLists = new List<List<Bill>> { BillItems };
        if (IsFilterDataLoaded)
        {
            sourceLists.Add(FilterAllBills);
        }

        foreach (var list in sourceLists)
        {
            var bill = list.FirstOrDefault(b => b.Id == billId);
            if (bill is null) continue;
            bill.MarkedPayerRoleId = markedPayerRoleId;
            bill.UpdatedBy = updatedBy;
            bill.UpdatedAt = updatedAt;
        }
    }

    private void RecalculateLocalState()
    {
        Summary = BuildSummary(BillItems);
        RebuildAppliedFilterResults();
        StateHasChanged();
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

    private IReadOnlyList<Bill> ApplyFilter(IEnumerable<Bill> source, FilterTab targetTab)
    {
        IEnumerable<Bill> query = source;

        if (targetTab == FilterTab.Marked)
        {
            query = query.Where(b => b.MarkedPayerRoleId > 0);
            query = ApplyIoFilter(query, MarkIoFilter);

            if (FilterStartDate.HasValue)
            {
                var start = FilterStartDate.Value.ToDateTime(TimeOnly.MinValue);
                query = query.Where(b => b.BillDate.HasValue && b.BillDate.Value.Date >= start.Date);
            }

            if (FilterEndDate.HasValue)
            {
                var end = FilterEndDate.Value.ToDateTime(TimeOnly.MinValue);
                query = query.Where(b => b.BillDate.HasValue && b.BillDate.Value.Date <= end.Date);
            }
        }
        else
        {
            if (ActiveConditionFields.Contains(ConditionField.IoType))
            {
                query = ApplyIoFilter(query, ConditionIoFilter);
            }

            if (ActiveConditionFields.Contains(ConditionField.Amount) && FilterMinAmount.HasValue)
            {
                query = query.Where(b => b.Amount >= FilterMinAmount.Value);
            }

            if (ActiveConditionFields.Contains(ConditionField.Amount) && FilterMaxAmount.HasValue)
            {
                query = query.Where(b => b.Amount <= FilterMaxAmount.Value);
            }

            if (ActiveConditionFields.Contains(ConditionField.IsExtra) && ExtraFilter != TriState.All)
            {
                var isExtra = ExtraFilter == TriState.Yes;
                query = query.Where(b => b.IsExtra == isExtra);
            }

            if (ActiveConditionFields.Contains(ConditionField.Marked) && MarkedFilter != TriState.All)
            {
                var isMarked = MarkedFilter == TriState.Yes;
                query = query.Where(b => (b.MarkedPayerRoleId > 0) == isMarked);
            }

            if (ActiveConditionFields.Contains(ConditionField.Owner) && FilterOwnerRoleId.HasValue && FilterOwnerRoleId.Value > 0)
            {
                query = query.Where(b => b.OwnerRoleId == FilterOwnerRoleId.Value);
            }

            if (ActiveConditionFields.Contains(ConditionField.Payer) && FilterPayerRoleId.HasValue && FilterPayerRoleId.Value > 0)
            {
                query = query.Where(b => b.PayerRoleId == FilterPayerRoleId.Value);
            }

            if (ActiveConditionFields.Contains(ConditionField.Category))
            {
                query = query.Where(FilterCategoryMatch);
            }

            if (ActiveConditionFields.Contains(ConditionField.Name))
            {
                var tokens = (FilterKeyword ?? string.Empty)
                    .Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

                if (tokens.Length > 0)
                {
                    query = query.Where(b =>
                    {
                        var title = b.Title ?? string.Empty;
                        return tokens.All(t => title.Contains(t, StringComparison.OrdinalIgnoreCase));
                    });
                }
            }

            if (ActiveConditionFields.Contains(ConditionField.Date) && FilterStartDate.HasValue)
            {
                var start = FilterStartDate.Value.ToDateTime(TimeOnly.MinValue);
                query = query.Where(b => b.BillDate.HasValue && b.BillDate.Value.Date >= start.Date);
            }

            if (ActiveConditionFields.Contains(ConditionField.Date) && FilterEndDate.HasValue)
            {
                var end = FilterEndDate.Value.ToDateTime(TimeOnly.MinValue);
                query = query.Where(b => b.BillDate.HasValue && b.BillDate.Value.Date <= end.Date);
            }
        }

        return query.ToList();
    }

    private IEnumerable<Bill> ApplyIoFilter(IEnumerable<Bill> source, IoFilter filter)
    {
        return filter switch
        {
            IoFilter.Expense => source.Where(b => b.IoType == 1),
            IoFilter.Income => source.Where(b => b.IoType == 2),
            _ => source
        };
    }

    private bool FilterCategoryMatch(Bill bill)
    {
        if (ConditionIoFilter == IoFilter.Expense || ConditionIoFilter == IoFilter.All)
        {
            if (bill.IoType == 1)
            {
                if (ExpenseMainCategoryIds.Count > 0 && !ExpenseMainCategoryIds.Contains(bill.MainCategoryId)) return false;
                if (ExpenseSubCategoryIds.Count > 0 && (!bill.SubCategoryId.HasValue || !ExpenseSubCategoryIds.Contains(bill.SubCategoryId.Value))) return false;
            }
        }

        if (ConditionIoFilter == IoFilter.Income || ConditionIoFilter == IoFilter.All)
        {
            if (bill.IoType == 2)
            {
                if (IncomeMainCategoryIds.Count > 0 && !IncomeMainCategoryIds.Contains(bill.MainCategoryId)) return false;
                if (IncomeSubCategoryIds.Count > 0 && (!bill.SubCategoryId.HasValue || !IncomeSubCategoryIds.Contains(bill.SubCategoryId.Value))) return false;
            }
        }

        return true;
    }

    private MarkSettlement BuildMarkSettlement(IEnumerable<Bill> source)
    {
        decimal net = 0m;
        foreach (var bill in source.Where(b => b.MarkedPayerRoleId > 0))
        {
            net += CalculateSettlementForYiyi(bill.PayerRoleId, bill.MarkedPayerRoleId, bill.Amount);
        }

        return new MarkSettlement(net);
    }

    private decimal CalculateSettlementForYiyi(long shouldPayRoleId, long actualPayRoleId, decimal amount)
    {
        if (shouldPayRoleId == YiyiRoleId && actualPayRoleId == Yiyi2RoleId) return amount;
        if (shouldPayRoleId == YiyiRoleId && actualPayRoleId == SharedRoleId) return amount / 2m;
        if (shouldPayRoleId == Yiyi2RoleId && actualPayRoleId == YiyiRoleId) return -amount;
        if (shouldPayRoleId == Yiyi2RoleId && actualPayRoleId == SharedRoleId) return -amount / 2m;
        if (shouldPayRoleId == SharedRoleId && actualPayRoleId == YiyiRoleId) return -amount / 2m;
        if (shouldPayRoleId == SharedRoleId && actualPayRoleId == Yiyi2RoleId) return amount / 2m;
        return 0m;
    }

    private async Task ClearMarkedBillsAsync()
    {
        var target = MarkedFilteredBills.Where(x => x.MarkedPayerRoleId > 0).ToList();
        if (target.Count == 0)
        {
            await ShowToastAsync("当前没有可结清账单");
            return;
        }

        var ok = await JS.InvokeAsync<bool>("confirm", $"确定将当前筛选结果中的 {target.Count} 条标记账单设为已结清吗？");
        if (!ok) return;

        var currentUserId = await Supabase.GetCurrentAppUserIdAsync();
        foreach (var bill in target)
        {
            await Supabase.ClearBillMarkAsync(bill.Id, currentUserId);
            bill.MarkedPayerRoleId = 0;
            bill.UpdatedBy = currentUserId;
            bill.UpdatedAt = DateTime.UtcNow;
        }

        await LoadBillsAsync();
        await ShowToastAsync("已结清");
    }

    private IEnumerable<MainCategoryInfo> GetMainCategoriesForType(short ioType)
    {
        return MainCategoryItems
            .Where(x => !x.IsDeleted && x.Type == ioType)
            .OrderBy(x => x.Id);
    }

    private IEnumerable<SubCategoryInfo> GetSubCategoriesForMain(long mainCategoryId)
    {
        return SubCategoryItems
            .Where(x => !x.IsDeleted && x.MainCategoryId == mainCategoryId)
            .OrderBy(x => x.Id);
    }

    private void ToggleMainCategory(bool isExpense, long categoryId)
    {
        var set = isExpense ? ExpenseMainCategoryIds : IncomeMainCategoryIds;
        var subSet = isExpense ? ExpenseSubCategoryIds : IncomeSubCategoryIds;
        if (!set.Add(categoryId))
        {
            set.Remove(categoryId);
        }

        if (set.Count != 1)
        {
            subSet.Clear();
        }
    }

    private void ToggleSubCategory(bool isExpense, long categoryId)
    {
        var mainSet = isExpense ? ExpenseMainCategoryIds : IncomeMainCategoryIds;
        if (mainSet.Count != 1) return;

        var subSet = isExpense ? ExpenseSubCategoryIds : IncomeSubCategoryIds;
        if (!subSet.Add(categoryId))
        {
            subSet.Remove(categoryId);
        }
    }

    private void ClearCategorySelection(bool isExpense)
    {
        if (isExpense)
        {
            ExpenseMainCategoryIds.Clear();
            ExpenseSubCategoryIds.Clear();
        }
        else
        {
            IncomeMainCategoryIds.Clear();
            IncomeSubCategoryIds.Clear();
        }
    }

    private void SelectAllMainCategories(bool isExpense)
    {
        var source = GetMainCategoriesForType(isExpense ? (short)1 : (short)2).Select(x => x.Id).ToHashSet();
        if (isExpense)
        {
            ExpenseMainCategoryIds = source;
            if (ExpenseMainCategoryIds.Count != 1) ExpenseSubCategoryIds.Clear();
        }
        else
        {
            IncomeMainCategoryIds = source;
            if (IncomeMainCategoryIds.Count != 1) IncomeSubCategoryIds.Clear();
        }
    }

    private bool IsDraftFieldChecked(ConditionField field) => DraftConditionFields.Contains(field);

    private void ToggleDraftField(ConditionField field, bool isChecked)
    {
        if (isChecked)
        {
            DraftConditionFields.Add(field);
        }
        else
        {
            DraftConditionFields.Remove(field);
        }
    }

    private void SwitchFilterTab(FilterTab tab)
    {
        CurrentFilterTab = tab;
        var source = GetFilterSourceBills();

        if (tab == FilterTab.Condition && IsConditionFilterApplied)
        {
            ConditionFilteredBills = ApplyFilter(source, FilterTab.Condition).ToList();
        }
        else if (tab == FilterTab.Marked && IsMarkedFilterApplied)
        {
            MarkedFilteredBills = ApplyFilter(source, FilterTab.Marked).ToList();
        }
    }

    private void ToggleDraftMainCategory(bool isExpense, long categoryId)
    {
        if (isExpense && !IsDraftExpenseCategoryEnabled) return;
        if (!isExpense && !IsDraftIncomeCategoryEnabled) return;
        var set = isExpense ? DraftExpenseMainCategoryIds : DraftIncomeMainCategoryIds;
        var subSet = isExpense ? DraftExpenseSubCategoryIds : DraftIncomeSubCategoryIds;
        if (!set.Add(categoryId))
        {
            set.Remove(categoryId);
        }

        if (set.Count != 1)
        {
            subSet.Clear();
        }
    }

    private void ToggleDraftSubCategory(bool isExpense, long categoryId)
    {
        if (isExpense && !IsDraftExpenseCategoryEnabled) return;
        if (!isExpense && !IsDraftIncomeCategoryEnabled) return;
        var mainSet = isExpense ? DraftExpenseMainCategoryIds : DraftIncomeMainCategoryIds;
        if (mainSet.Count != 1) return;
        var subSet = isExpense ? DraftExpenseSubCategoryIds : DraftIncomeSubCategoryIds;
        if (!subSet.Add(categoryId))
        {
            subSet.Remove(categoryId);
        }
    }

    private void DraftSelectAllMainCategories(bool isExpense)
    {
        if (isExpense && !IsDraftExpenseCategoryEnabled) return;
        if (!isExpense && !IsDraftIncomeCategoryEnabled) return;
        var source = GetMainCategoriesForType(isExpense ? (short)1 : (short)2).Select(x => x.Id).ToHashSet();
        if (isExpense)
        {
            DraftExpenseMainCategoryIds = source;
            if (DraftExpenseMainCategoryIds.Count != 1) DraftExpenseSubCategoryIds.Clear();
        }
        else
        {
            DraftIncomeMainCategoryIds = source;
            if (DraftIncomeMainCategoryIds.Count != 1) DraftIncomeSubCategoryIds.Clear();
        }
    }

    private void DraftClearCategorySelection(bool isExpense)
    {
        if (isExpense && !IsDraftExpenseCategoryEnabled) return;
        if (!isExpense && !IsDraftIncomeCategoryEnabled) return;
        if (isExpense)
        {
            DraftExpenseMainCategoryIds.Clear();
            DraftExpenseSubCategoryIds.Clear();
        }
        else
        {
            DraftIncomeMainCategoryIds.Clear();
            DraftIncomeSubCategoryIds.Clear();
        }
    }

    private void DraftSelectAllSubCategories(bool isExpense)
    {
        if (isExpense && !IsDraftExpenseCategoryEnabled) return;
        if (!isExpense && !IsDraftIncomeCategoryEnabled) return;
        var mainSet = isExpense ? DraftExpenseMainCategoryIds : DraftIncomeMainCategoryIds;
        if (mainSet.Count != 1) return;
        var parentId = mainSet.First();
        var all = GetSubCategoriesForMain(parentId).Select(x => x.Id).ToHashSet();
        if (isExpense)
        {
            DraftExpenseSubCategoryIds = all;
        }
        else
        {
            DraftIncomeSubCategoryIds = all;
        }
    }

    private void SelectAllSubCategories(bool isExpense)
    {
        var mainSet = isExpense ? ExpenseMainCategoryIds : IncomeMainCategoryIds;
        if (mainSet.Count != 1) return;
        var parentId = mainSet.First();
        var all = GetSubCategoriesForMain(parentId).Select(x => x.Id).ToHashSet();
        if (isExpense)
        {
            ExpenseSubCategoryIds = all;
        }
        else
        {
            IncomeSubCategoryIds = all;
        }
    }

    private async Task ApplyFilterNowAsync()
    {
        await EnsureFilterBillsLoadedAsync();
        var source = GetFilterSourceBills();
        if (CurrentFilterTab == FilterTab.Condition)
        {
            IsConditionFilterApplied = true;
            ConditionFilteredBills = ApplyFilter(source, FilterTab.Condition).ToList();
        }
        else
        {
            IsMarkedFilterApplied = true;
            MarkedFilteredBills = ApplyFilter(source, FilterTab.Marked).ToList();
        }

        CloseSwipeImmediate();
        await ShowToastAsync("筛选已应用");
    }

    private void SetDraftConditionIoFilter(IoFilter value)
    {
        DraftConditionIoFilter = value;
        if (value == IoFilter.Expense)
        {
            DraftIncomeMainCategoryIds.Clear();
            DraftIncomeSubCategoryIds.Clear();
        }
        else if (value == IoFilter.Income)
        {
            DraftExpenseMainCategoryIds.Clear();
            DraftExpenseSubCategoryIds.Clear();
        }
    }

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

    private async Task HandleEditorSaved(Bill savedBill)
    {
        ShowEditor = false;

        UpsertBillLocally(savedBill);
        RecalculateLocalState();
        HighlightedBillId = savedBill.Id;
        StateHasChanged();
        _ = ShowToastAsync("保存成功");

        _ = Task.Run(async () =>
        {
            await Task.Delay(500);
            HighlightedBillId = null;
            await InvokeAsync(StateHasChanged);
        });

    }

    private void UpsertBillLocally(Bill savedBill)
    {
        UpsertHomeBillInRange(savedBill);
        if (IsFilterDataLoaded)
        {
            UpsertBillInList(FilterAllBills, savedBill);
        }
    }

    private void UpsertHomeBillInRange(Bill savedBill)
    {
        var inRange = IsBillInCurrentHomeRange(savedBill);
        var index = BillItems.FindIndex(b => b.Id == savedBill.Id);
        if (!inRange)
        {
            if (index >= 0)
            {
                BillItems.RemoveAt(index);
            }
            return;
        }

        if (index >= 0)
        {
            BillItems[index] = savedBill;
        }
        else
        {
            BillItems.Add(savedBill);
        }

        BillItems = BillItems
            .OrderByDescending(b => b.BillDate)
            .ThenByDescending(b => b.Id)
            .ToList();
    }

    private static void UpsertBillInList(List<Bill> source, Bill savedBill)
    {
        var index = source.FindIndex(b => b.Id == savedBill.Id);
        if (index >= 0)
        {
            source[index] = savedBill;
        }
        else
        {
            source.Add(savedBill);
        }

        source.Sort((a, b) =>
        {
            var dateCompare = Nullable.Compare(b.BillDate, a.BillDate);
            return dateCompare != 0 ? dateCompare : b.Id.CompareTo(a.Id);
        });
    }

    private bool IsBillInCurrentHomeRange(Bill bill)
    {
        if (!bill.BillDate.HasValue) return false;
        var (start, endExclusive) = GetRange();
        var date = DateOnly.FromDateTime(bill.BillDate.Value);
        return date >= start && date < endExclusive;
    }
}

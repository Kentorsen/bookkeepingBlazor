using System.Globalization;
using System.Net;
using System.Text;
using BookkeepingBlazor.Models;
using BookkeepingBlazor.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BookkeepingBlazor.Components.Bills;

public partial class BillsStatsPanel
{
    [Inject]
    private SupabaseService Supabase { get; set; } = default!;

    private const short IoExpense = 1;
    private const short IoIncome = 2;
    private const string NameYiyi = "依依";
    private const string NameYiyi2 = "一一";
    private const string NameShared = "共同";
    private const long DefaultYiyiRoleId = 2;
    private const long DefaultYiyi2RoleId = 1;
    private const long DefaultSharedRoleId = 3;

    private DateOnly StatsRangeStart = new(DateTime.Today.Year, 1, 1);
    private DateOnly StatsRangeEnd = new(DateTime.Today.Year, 12, 31);

    private bool ShowRangePicker;
    private StatsRangePickerMode RangePickerMode = StatsRangePickerMode.Year;
    private int PickerYear = DateTime.Today.Year;
    private DateOnly? DraftCustomStart;
    private DateOnly? DraftCustomEnd;

    private StatsMethodTab MethodTab = StatsMethodTab.Category;

    private enum StatsMethodTab
    {
        Category,
        Trend
    }

    private enum StatsRangePickerMode
    {
        Year,
        Custom
    }

    private readonly HashSet<StatsPersonKind> SelectedPersons = new() { StatsPersonKind.Total };

    private bool CategoryIoExpense = true;
    private bool TrendIoExpense = true;

    private readonly HashSet<string> ExpandedCategoryKeys = new();

    private bool IsLoadingStats = true;
    private string? LoadError;

    private long YiyiRoleId = DefaultYiyiRoleId;
    private long Yiyi2RoleId = DefaultYiyi2RoleId;
    private long SharedRoleId = DefaultSharedRoleId;

    private Dictionary<long, string> MainNames = new();
    private Dictionary<long, string> SubNames = new();

    private List<Bill> CachedBillsInRange = new();

    private readonly Dictionary<StatsPersonKind, OverviewMetrics> OverviewMap = new();
    private readonly Dictionary<(StatsPersonKind Person, bool ExpenseMode), CategoryStatsResult> CategoryMap = new();
    private readonly Dictionary<(StatsPersonKind Person, bool ExpenseMode), IReadOnlyList<MonthTrendPoint>> TrendMap = new();

    /// <summary>月趋势图：按图表序号记录当前选中的点下标（点击圆点查看金额）。</summary>
    private readonly Dictionary<int, int> TrendSelectedPointByChart = new();

    protected override async Task OnInitializedAsync()
    {
        DraftCustomStart = StatsRangeStart;
        DraftCustomEnd = StatsRangeEnd;
        await LoadStatsDataAsync();
    }

    private async Task LoadStatsDataAsync()
    {
        IsLoadingStats = true;
        LoadError = null;
        await InvokeAsync(StateHasChanged);

        try
        {
            await ResolveRoleIdsAsync();
            await LoadCategoryNamesAsync();

            var endExclusive = StatsRangeEnd.AddDays(1);
            CachedBillsInRange = await Supabase.GetBillsByRangeAsync(StatsRangeStart, endExclusive);

            RecomputeAggregates();
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
            CachedBillsInRange = new List<Bill>();
            ClearAggregateMaps();
        }
        finally
        {
            IsLoadingStats = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task ResolveRoleIdsAsync()
    {
        try
        {
            var roles = await Supabase.GetRolesAsync();
            var map = roles
                .Where(x => !x.IsDeleted && !string.IsNullOrWhiteSpace(x.Name))
                .GroupBy(x => x.Id)
                .ToDictionary(g => g.Key, g => g.First().Name!);

            YiyiRoleId = FindRoleIdByName(map, NameYiyi, DefaultYiyiRoleId);
            Yiyi2RoleId = FindRoleIdByName(map, NameYiyi2, DefaultYiyi2RoleId);
            SharedRoleId = FindRoleIdByName(map, NameShared, DefaultSharedRoleId);
        }
        catch
        {
            YiyiRoleId = DefaultYiyiRoleId;
            Yiyi2RoleId = DefaultYiyi2RoleId;
            SharedRoleId = DefaultSharedRoleId;
        }
    }

    private static long FindRoleIdByName(Dictionary<long, string> map, string name, long fallbackId)
    {
        var found = map.FirstOrDefault(x => x.Value == name);
        return found.Key > 0 ? found.Key : fallbackId;
    }

    private async Task LoadCategoryNamesAsync()
    {
        MainNames = new Dictionary<long, string>();
        SubNames = new Dictionary<long, string>();

        try
        {
            var mains = await Supabase.GetMainCategoriesAsync();
            MainNames = mains
                .Where(x => !x.IsDeleted && !string.IsNullOrWhiteSpace(x.Name))
                .GroupBy(x => x.Id)
                .ToDictionary(g => g.Key, g => g.First().Name!);
        }
        catch
        {
        }

        try
        {
            var subs = await Supabase.GetSubCategoriesAsync();
            SubNames = subs
                .Where(x => !x.IsDeleted && !string.IsNullOrWhiteSpace(x.Name))
                .GroupBy(x => x.Id)
                .ToDictionary(g => g.Key, g => g.First().Name!);
        }
        catch
        {
        }
    }

    private void ClearAggregateMaps()
    {
        OverviewMap.Clear();
        CategoryMap.Clear();
        TrendMap.Clear();
        TrendSelectedPointByChart.Clear();
    }

    private void ClearTrendSelection() => TrendSelectedPointByChart.Clear();

    private int? GetTrendSelection(int chartIdx) =>
        TrendSelectedPointByChart.TryGetValue(chartIdx, out var i) ? i : null;

    private void ToggleTrendPoint(int chartIdx, int pointIndex)
    {
        if (TrendSelectedPointByChart.TryGetValue(chartIdx, out var cur) && cur == pointIndex)
        {
            TrendSelectedPointByChart.Remove(chartIdx);
        }
        else
        {
            TrendSelectedPointByChart[chartIdx] = pointIndex;
        }

        StateHasChanged();
    }

    private void TrendDotKeydown(int chartIdx, int pointIndex, KeyboardEventArgs e)
    {
        if (e.Code is "Enter" or "NumpadEnter" or "Space")
        {
            ToggleTrendPoint(chartIdx, pointIndex);
        }
    }

    private bool TryGetTrendSelectionDetail(int chartIdx, TrendChartModel chart, out string detail)
    {
        detail = "";
        if (!TrendSelectedPointByChart.TryGetValue(chartIdx, out var idx) ||
            idx < 0 || idx >= chart.DataPoints.Count)
        {
            return false;
        }

        var d = chart.DataPoints[idx];
        detail = $"{d.Year}年{d.Month}月：{Fmt(d.Amount)}";
        return true;
    }

    private bool HasBillsInCurrentStatsRange() =>
        CachedBillsInRange.Any(b => StatsAggregation.BillInInclusiveDateRange(b, StatsRangeStart, StatsRangeEnd));

    private void RecomputeAggregates()
    {
        ClearAggregateMaps();
        ExpandedCategoryKeys.Clear();

        if (SelectedPersons.Count == 0)
        {
            return;
        }

        var inRange = CachedBillsInRange
            .Where(b => StatsAggregation.BillInInclusiveDateRange(b, StatsRangeStart, StatsRangeEnd))
            .ToList();

        if (inRange.Count == 0)
        {
            return;
        }

        foreach (var person in new[] { StatsPersonKind.Yiyi, StatsPersonKind.Yiyi2, StatsPersonKind.Total })
        {
            var pb = StatsAggregation.FilterByPerson(inRange, person, YiyiRoleId, Yiyi2RoleId, SharedRoleId).ToList();
            OverviewMap[person] = StatsAggregation.ComputeOverview(pb, IoExpense, IoIncome);

            CategoryMap[(person, true)] = StatsAggregation.ComputeCategoryStats(pb, IoExpense, MainNames, SubNames);
            CategoryMap[(person, false)] = StatsAggregation.ComputeCategoryStats(pb, IoIncome, MainNames, SubNames);

            TrendMap[(person, true)] = StatsAggregation.ComputeMonthlyTrend(pb, StatsRangeStart, StatsRangeEnd, IoExpense);
            TrendMap[(person, false)] = StatsAggregation.ComputeMonthlyTrend(pb, StatsRangeStart, StatsRangeEnd, IoIncome);
        }
    }

    private void OpenRangePicker()
    {
        PickerYear = StatsRangeStart.Year;
        DraftCustomStart = StatsRangeStart;
        DraftCustomEnd = StatsRangeEnd;
        RangePickerMode = StatsRangePickerMode.Year;
        ShowRangePicker = true;
    }

    private void CloseRangePicker() => ShowRangePicker = false;

    private async Task ConfirmRangePickerAsync()
    {
        if (RangePickerMode == StatsRangePickerMode.Year)
        {
            StatsRangeStart = new DateOnly(PickerYear, 1, 1);
            StatsRangeEnd = new DateOnly(PickerYear, 12, 31);
        }
        else if (DraftCustomStart is { } s && DraftCustomEnd is { } e)
        {
            StatsRangeStart = s <= e ? s : e;
            StatsRangeEnd = s <= e ? e : s;
        }

        ShowRangePicker = false;
        await LoadStatsDataAsync();
    }

    private void DecPickerYear() => PickerYear--;

    private void IncPickerYear() => PickerYear++;

    private void PickThisYear()
    {
        PickerYear = DateTime.Today.Year;
    }

    private void PickLastYear()
    {
        PickerYear = DateTime.Today.Year - 1;
    }

    private void PickThisMonthRange()
    {
        var t = DateTime.Today;
        DraftCustomStart = new DateOnly(t.Year, t.Month, 1);
        DraftCustomEnd = new DateOnly(t.Year, t.Month, DateTime.DaysInMonth(t.Year, t.Month));
    }

    private void PickLastMonthRange()
    {
        var t = DateTime.Today.AddMonths(-1);
        DraftCustomStart = new DateOnly(t.Year, t.Month, 1);
        DraftCustomEnd = new DateOnly(t.Year, t.Month, DateTime.DaysInMonth(t.Year, t.Month));
    }

    private void PickLast7Days()
    {
        var end = DateOnly.FromDateTime(DateTime.Today);
        DraftCustomStart = end.AddDays(-6);
        DraftCustomEnd = end;
    }

    private void PickLast30Days()
    {
        var end = DateOnly.FromDateTime(DateTime.Today);
        DraftCustomStart = end.AddDays(-29);
        DraftCustomEnd = end;
    }

    private void PickAllRange()
    {
        DraftCustomStart = new DateOnly(2020, 1, 1);
        DraftCustomEnd = DateOnly.FromDateTime(DateTime.Today);
    }

    private string FormatRangeLine() => $"{FormatCn(StatsRangeStart)}~{FormatCn(StatsRangeEnd)}";

    private string FormatRangeLineDraft()
    {
        if (RangePickerMode == StatsRangePickerMode.Year)
        {
            var s = new DateOnly(PickerYear, 1, 1);
            var e = new DateOnly(PickerYear, 12, 31);
            return $"{FormatCn(s)}~{FormatCn(e)}";
        }

        if (DraftCustomStart is { } a && DraftCustomEnd is { } b)
        {
            var s = a <= b ? a : b;
            var e = a <= b ? b : a;
            return $"{FormatCn(s)}~{FormatCn(e)}";
        }

        return "请选择日期";
    }

    private static string FormatCn(DateOnly d) => $"{d.Year}年{d.Month:D2}月{d.Day:D2}日";

    private void TogglePerson(StatsPersonKind p)
    {
        if (SelectedPersons.Contains(p))
        {
            SelectedPersons.Remove(p);
        }
        else
        {
            SelectedPersons.Add(p);
        }

        RecomputeAggregates();
    }

    private IEnumerable<StatsPersonKind> OrderedSelectedPersons()
    {
        foreach (var p in new[] { StatsPersonKind.Yiyi, StatsPersonKind.Yiyi2, StatsPersonKind.Total })
        {
            if (SelectedPersons.Contains(p))
            {
                yield return p;
            }
        }
    }

    private IEnumerable<(StatsPersonKind Person, int ChartIndex)> OrderedSelectedPersonsIndexed()
    {
        var i = 0;
        foreach (var p in OrderedSelectedPersons())
        {
            yield return (p, i++);
        }
    }

    private static string GetPersonTitle(StatsPersonKind p) => p switch
    {
        StatsPersonKind.Yiyi => NameYiyi,
        StatsPersonKind.Yiyi2 => NameYiyi2,
        _ => "共计"
    };

    private static string Fmt(decimal v) => v.ToString("0.##");

    private static string FormatPct(decimal pct) => $"{pct:0.#}%";

    private static string BalanceClass(OverviewMetrics m) => m.Balance < 0m ? "expense" : "income";

    private OverviewMetrics GetOverview(StatsPersonKind p) =>
        OverviewMap.TryGetValue(p, out var m) ? m : default;

    private CategoryStatsResult GetCategoryResult(StatsPersonKind p, bool expenseMode) =>
        CategoryMap.TryGetValue((p, expenseMode), out var r)
            ? r
            : new CategoryStatsResult(0m, Array.Empty<MainCategoryRow>());

    private IReadOnlyList<MonthTrendPoint> GetTrendPoints(StatsPersonKind p, bool expenseMode) =>
        TrendMap.TryGetValue((p, expenseMode), out var pts) ? pts : Array.Empty<MonthTrendPoint>();

    private void ToggleCatExpand(long mainCategoryId)
    {
        var key = CatExpandKey(mainCategoryId);
        if (!ExpandedCategoryKeys.Add(key))
        {
            ExpandedCategoryKeys.Remove(key);
        }
    }

    private static string CatExpandKey(long mainCategoryId) => $"m{mainCategoryId}";

    private bool IsCatExpanded(long mainCategoryId) => ExpandedCategoryKeys.Contains(CatExpandKey(mainCategoryId));

    private sealed record TrendDatum(double Cx, double Cy, decimal Amount, int Year, int Month);

    private sealed record TrendChartModel(
        int SvgWidth,
        int SvgHeight,
        string DefsMarkup,
        string XLabelsMarkup,
        string ArrowMarkerId,
        double OriginX,
        double BaselineY,
        double YAxisTipY,
        double XAxisTipX,
        string PolylinePoints,
        string PolygonPoints,
        string YTopLabel,
        string YMidLabel,
        string YBottomLabel,
        string Caption,
        IReadOnlyList<TrendDatum> DataPoints,
        bool HasData);

    /// <summary>
    /// 纵轴范围：使数据最大值落在绘图区高度向上约 18% 处（相对 plotInnerH），避免折线离顶太远。
    /// </summary>
    private static (decimal AxisMin, decimal AxisMax) ComputeTrendAxisRange(IReadOnlyList<MonthTrendPoint> points)
    {
        // cy(dataMax) = plotTop + 0.18 * plotInnerH  ⇒  t = (dataMax-axisMin)/(axisMax-axisMin) = 0.82
        const decimal dataMaxT = 0.82m;

        if (points.Count == 0) return (0, 1);
        var dataMin = points.Min(p => p.Amount);
        var dataMax = points.Max(p => p.Amount);
        if (dataMax < dataMin) (dataMin, dataMax) = (dataMax, dataMin);

        var span = dataMax - dataMin;
        if (span <= 0)
        {
            var pad = dataMax != 0 ? Math.Max(Math.Abs(dataMax) * 0.08m, 0.01m) : 1m;
            return (dataMin - pad, dataMax + pad);
        }

        var padLo = span * 0.14m;
        var rawLo = dataMin - padLo;
        if (dataMin >= 0 && rawLo < 0) rawLo = 0;
        if (dataMin > 0 && rawLo >= dataMin - span * 0.02m)
        {
            rawLo = Math.Max(0, dataMin - span * 0.1m);
        }

        var rawHiGuess = dataMax + span * 0.25m;
        var targetTicks = 4m;
        var rough = (rawHiGuess - rawLo) / targetTicks;
        if (rough <= 0) rough = 1;
        var log = Math.Floor(Math.Log10((double)rough));
        var exp = (decimal)Math.Pow(10, log);
        var m = rough / exp;
        var niceM = m <= 1m ? 1m : m <= 2m ? 2m : m <= 5m ? 5m : 10m;
        var step = niceM * exp;
        var axisMin = Math.Floor(rawLo / step) * step;
        if (axisMin > dataMin) axisMin = Math.Floor(dataMin / step) * step;
        if (axisMin > dataMin) axisMin = dataMin - step;

        var axisMax = axisMin + (dataMax - axisMin) / dataMaxT;
        if (axisMax <= axisMin) axisMax = axisMin + step;
        if (axisMax < dataMax) axisMax = dataMax + step * 0.5m;

        return (axisMin, axisMax);
    }

    private TrendChartModel BuildTrendChart(
        IReadOnlyList<MonthTrendPoint> points,
        DateOnly rangeStart,
        DateOnly rangeEnd,
        int chartIndex)
    {
        var arrowId = $"st-arr-{chartIndex}";
        /* 每月水平占位；约减一个汉字宽，使月份更紧凑 */
        const double monthSlotPx = 34d;
        const double topPad = 10d;
        const double plotH = 168d;
        const double bottomPad = 34d;
        const double originX = 12d;
        const double rightMargin = 14d;
        const double xArrowReserve = 18d;
        var inv = CultureInfo.InvariantCulture;

        if (points.Count == 0)
        {
            var emptyH = (int)Math.Ceiling(topPad + plotH + bottomPad);
            return new TrendChartModel(
                280, emptyH, string.Empty, string.Empty, arrowId,
                originX, topPad + plotH, topPad + 2, 260,
                string.Empty, string.Empty, "0", "0", "0", "暂无数据",
                Array.Empty<TrendDatum>(), false);
        }

        var caption =
            $"{points[0].Year}年{points[0].Month:D2}月 — {points[^1].Year}年{points[^1].Month:D2}月";

        var (axisMin, axisMax) = ComputeTrendAxisRange(points);
        var axisSpan = axisMax - axisMin;
        if (axisSpan <= 0) axisSpan = 1;

        var yMidVal = (axisMin + axisMax) / 2m;
        var yTopLabel = FmtAxisTick(axisMax);
        var yMidLabel = FmtAxisTick(yMidVal);
        var yBottomLabel = FmtAxisTick(axisMin);

        var n = points.Count;
        var innerW = Math.Max(200d, n * monthSlotPx);
        var svgW = (int)Math.Ceiling(originX + innerW + rightMargin + xArrowReserve);
        var xAxisTipX = svgW - 6d;
        var xDataRight = xAxisTipX - xArrowReserve;
        var xDataLeft = originX + 4d;

        var plotTop = topPad + 18d;
        var baselineY = topPad + plotH;
        var plotInnerH = baselineY - plotTop;
        /* 纵轴箭头略短，最大值刻度在箭头下方，避免与箭头尖挤在一起 */
        var yAxisTipY = plotTop - 8d;
        var svgH = (int)Math.Ceiling(baselineY + bottomPad);

        double XAt(int i)
        {
            if (n <= 1) return (xDataLeft + xDataRight) / 2d;
            return xDataLeft + (xDataRight - xDataLeft) * (i / (double)(n - 1));
        }

        double YAt(decimal amt)
        {
            var t = (double)((amt - axisMin) / axisSpan);
            t = Math.Clamp(t, 0d, 1d);
            return baselineY - t * plotInnerH;
        }

        var polyPts = new List<string>();
        var dataList = new List<TrendDatum>();
        for (var i = 0; i < n; i++)
        {
            var pt = points[i];
            var cx = XAt(i);
            var cy = YAt(pt.Amount);
            polyPts.Add($"{cx.ToString("0.##", inv)},{cy.ToString("0.##", inv)}");
            dataList.Add(new TrendDatum(cx, cy, pt.Amount, pt.Year, pt.Month));
        }

        var polyline = string.Join(' ', polyPts);
        var firstX = XAt(0);
        var lastX = XAt(n - 1);
        var polygon =
            $"{polyline} {lastX.ToString("0.##", inv)},{baselineY.ToString("0.##", inv)} {firstX.ToString("0.##", inv)},{baselineY.ToString("0.##", inv)}";

        var sbDefs = new StringBuilder();
        sbDefs.Append("<defs>");
        sbDefs.Append("<marker id=\"")
            .Append(arrowId)
            .Append("\" viewBox=\"0 0 10 10\" refX=\"8\" refY=\"5\" markerWidth=\"5\" markerHeight=\"5\" orient=\"auto\">");
        sbDefs.Append("<path d=\"M0,0 L10,5 L0,10 Z\" fill=\"#94a3b8\"/></marker>");
        sbDefs.Append("</defs>");

        var multiYear = rangeStart.Year != rangeEnd.Year;
        var sbX = new StringBuilder();
        /* 横轴文字略下移，减轻与近轴数据圆点重叠 */
        var textY = baselineY + 8d;
        for (var i = 0; i < n; i++)
        {
            var p = points[i];
            var label = multiYear ? $"{p.Year}/{p.Month}" : $"{p.Month}月";
            var x = XAt(i).ToString("0.##", inv);
            sbX.Append("<text x=\"")
                .Append(x)
                .Append("\" y=\"")
                .Append(textY.ToString("0.##", inv))
                .Append("\" text-anchor=\"middle\" dominant-baseline=\"hanging\" fill=\"#64748b\" style=\"font-size:10.5px;font-weight:600\">")
                .Append(WebUtility.HtmlEncode(label))
                .Append("</text>");
        }

        return new TrendChartModel(
            svgW,
            svgH,
            sbDefs.ToString(),
            sbX.ToString(),
            arrowId,
            originX,
            baselineY,
            yAxisTipY,
            xAxisTipX,
            polyline,
            polygon,
            yTopLabel,
            yMidLabel,
            yBottomLabel,
            caption,
            dataList,
            true);
    }

    /// <summary>纵轴刻度：整数、略粗略，不显示小数。</summary>
    private static string FmtAxisTick(decimal v)
    {
        var rv = decimal.Round(v, 0, MidpointRounding.AwayFromZero);
        var a = Math.Abs(rv);
        if (a >= 10000m)
            return decimal.Round(rv / 10000m, 0, MidpointRounding.AwayFromZero).ToString("0") + "万";
        if (a >= 1000m)
            return (decimal.Round(rv / 100m, 0, MidpointRounding.AwayFromZero) * 100).ToString("0");
        return rv.ToString("0");
    }
}

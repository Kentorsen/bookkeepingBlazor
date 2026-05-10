using BookkeepingBlazor.Models;

namespace BookkeepingBlazor.Services;

/// <summary>
/// 统计页：在已拉取的账单列表上做区间、对象、收支维度的内存聚合。
/// 账单上的 <see cref="Bill.PayerRoleId"/> / <see cref="Bill.OwnerRoleId"/> 均为 roles 表主键。
/// </summary>
public static class StatsAggregation
{
    public static bool BillInInclusiveDateRange(Bill bill, DateOnly rangeStart, DateOnly rangeEnd)
    {
        if (!bill.BillDate.HasValue) return false;
        var d = DateOnly.FromDateTime(bill.BillDate.Value.Date);
        return d >= rangeStart && d <= rangeEnd;
    }

    /// <summary>
    /// 按统计对象筛选账单。依依/一一：本人付款 role 全额 + 「共同」role（<paramref name="sharedRoleId"/>）金额各一半；共计：原列表不变。
    /// </summary>
    public static IEnumerable<Bill> FilterByPerson(
        IEnumerable<Bill> bills,
        StatsPersonKind person,
        long yiyiRoleId,
        long yiyi2RoleId,
        long sharedRoleId)
    {
        if (person == StatsPersonKind.Total)
            return bills;

        if (sharedRoleId <= 0)
        {
            return person switch
            {
                StatsPersonKind.Yiyi => bills.Where(b => b.PayerRoleId == yiyiRoleId),
                StatsPersonKind.Yiyi2 => bills.Where(b => b.PayerRoleId == yiyi2RoleId),
                _ => bills
            };
        }

        return bills.SelectMany(b => BillsForPersonSlice(b, person, yiyiRoleId, yiyi2RoleId, sharedRoleId));
    }

    private static IEnumerable<Bill> BillsForPersonSlice(
        Bill b,
        StatsPersonKind person,
        long yiyiRoleId,
        long yiyi2RoleId,
        long sharedRoleId)
    {
        switch (person)
        {
            case StatsPersonKind.Yiyi:
                if (b.PayerRoleId == yiyiRoleId)
                    yield return b;
                else if (b.PayerRoleId == sharedRoleId)
                    yield return BillWithScaledAmount(b, 0.5m);
                break;
            case StatsPersonKind.Yiyi2:
                if (b.PayerRoleId == yiyi2RoleId)
                    yield return b;
                else if (b.PayerRoleId == sharedRoleId)
                    yield return BillWithScaledAmount(b, 0.5m);
                break;
        }
    }

    private static Bill BillWithScaledAmount(Bill b, decimal factor)
    {
        if (factor == 1m) return b;
        return new Bill
        {
            Id = b.Id,
            IoType = b.IoType,
            MainCategoryId = b.MainCategoryId,
            SubCategoryId = b.SubCategoryId,
            Title = b.Title,
            Amount = b.Amount * factor,
            OwnerRoleId = b.OwnerRoleId,
            PayerRoleId = b.PayerRoleId,
            BillDate = b.BillDate,
            IsExtra = b.IsExtra,
            MarkedPayerRoleId = b.MarkedPayerRoleId,
            IsDeleted = b.IsDeleted,
            CreatedAt = b.CreatedAt,
            CreatedBy = b.CreatedBy,
            UpdatedAt = b.UpdatedAt,
            UpdatedBy = b.UpdatedBy,
        };
    }

    public static OverviewMetrics ComputeOverview(IEnumerable<Bill> bills, short ioExpense, short ioIncome)
    {
        var list = bills as IList<Bill> ?? bills.ToList();
        var expense = list.Where(b => b.IoType == ioExpense).Sum(b => b.Amount);
        var income = list.Where(b => b.IoType == ioIncome).Sum(b => b.Amount);
        return new OverviewMetrics(expense, income, income - expense);
    }

    public static CategoryStatsResult ComputeCategoryStats(
        IEnumerable<Bill> bills,
        short ioType,
        IReadOnlyDictionary<long, string> mainNames,
        IReadOnlyDictionary<long, string> subNames)
    {
        var filtered = bills.Where(b => b.IoType == ioType).ToList();
        var total = filtered.Sum(b => b.Amount);
        if (total <= 0m)
        {
            return new CategoryStatsResult(total, Array.Empty<MainCategoryRow>());
        }

        var byMain = filtered
            .GroupBy(b => b.MainCategoryId)
            .Select(g =>
            {
                var mainName = mainNames.TryGetValue(g.Key, out var mn) ? mn : "未分类";
                var mainAmount = g.Sum(b => b.Amount);
                var bySub = g
                    .GroupBy(b => ResolveSubCategoryKey(b.SubCategoryId, subNames))
                    .Select(sg =>
                    {
                        var key = sg.Key;
                        var amt = sg.Sum(b => b.Amount);
                        var pct = amt / total * 100m;
                        return new SubCategoryRow(key.KeyId, key.Label, amt, pct, BarPercent(amt, total));
                    })
                    .OrderByDescending(x => x.Amount)
                    .ToList();

                var mainPct = mainAmount / total * 100m;
                return new MainCategoryRow(
                    g.Key,
                    mainName,
                    mainAmount,
                    mainPct,
                    BarPercent(mainAmount, total),
                    bySub);
            })
            .OrderByDescending(x => x.Amount)
            .ToList();

        return new CategoryStatsResult(total, byMain);
    }

    private static (long? KeyId, string Label) ResolveSubCategoryKey(
        long? subCategoryId,
        IReadOnlyDictionary<long, string> subNames)
    {
        if (subCategoryId is > 0 && subNames.ContainsKey(subCategoryId.Value))
        {
            return (subCategoryId.Value, subNames[subCategoryId.Value]);
        }

        return (null, "无子类别");
    }

    public static IReadOnlyList<MonthTrendPoint> ComputeMonthlyTrend(
        IEnumerable<Bill> bills,
        DateOnly rangeStart,
        DateOnly rangeEnd,
        short ioType)
    {
        var filtered = bills.Where(b => b.IoType == ioType).ToList();
        var points = new List<MonthTrendPoint>();
        var cursor = new DateOnly(rangeStart.Year, rangeStart.Month, 1);
        var endMonth = new DateOnly(rangeEnd.Year, rangeEnd.Month, 1);
        while (cursor <= endMonth)
        {
            var monthEnd = new DateOnly(cursor.Year, cursor.Month, DateTime.DaysInMonth(cursor.Year, cursor.Month));
            var effectiveStart = rangeStart > cursor ? rangeStart : cursor;
            var effectiveEnd = rangeEnd < monthEnd ? rangeEnd : monthEnd;

            decimal sum = 0m;
            foreach (var b in filtered)
            {
                if (!b.BillDate.HasValue) continue;
                var d = DateOnly.FromDateTime(b.BillDate.Value.Date);
                if (d >= effectiveStart && d <= effectiveEnd)
                {
                    sum += b.Amount;
                }
            }

            points.Add(new MonthTrendPoint(cursor.Year, cursor.Month, sum));
            cursor = cursor.AddMonths(1);
        }

        return points;
    }

    private static int BarPercent(decimal part, decimal total)
    {
        if (total <= 0m) return 0;
        var p = (int)Math.Round((double)(part / total * 100m));
        if (p < 1 && part > 0m) return 1;
        return Math.Clamp(p, 0, 100);
    }
}

public enum StatsPersonKind
{
    Yiyi,
    Yiyi2,
    Total
}

public readonly record struct OverviewMetrics(decimal Expense, decimal Income, decimal Balance);

public readonly record struct SubCategoryRow(long? SubCategoryId, string Name, decimal Amount, decimal PctOfTotal, int BarPct);

public readonly record struct MainCategoryRow(
    long MainCategoryId,
    string Name,
    decimal Amount,
    decimal PctOfTotal,
    int BarPct,
    IReadOnlyList<SubCategoryRow> SubRows);

public sealed record CategoryStatsResult(decimal DenominatorTotal, IReadOnlyList<MainCategoryRow> MainRows);

public readonly record struct MonthTrendPoint(int Year, int Month, decimal Amount);

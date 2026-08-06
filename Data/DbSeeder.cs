using Microsoft.EntityFrameworkCore;
using PanasonicPrototype.Api.Models;
using PanasonicPrototype.Api.Services;

namespace PanasonicPrototype.Api.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(
        AppDbContext db, BudgetApprovalService approvalService, BudgetPlanningService budgetPlanningService)
    {
        if (db.Stores.Any()) return;

        var categories = new[]
        {
            new ApplianceCategory { Name = "Television" },
            new ApplianceCategory { Name = "Air Conditioner" },
            new ApplianceCategory { Name = "Refrigerator" },
            new ApplianceCategory { Name = "Washing Machine" },
            new ApplianceCategory { Name = "Microwave Oven" },
            new ApplianceCategory { Name = "Operations Cost" },
        };
        db.ApplianceCategories.AddRange(categories);

        var stores = new[]
        {
            new Store { Name = "Panasonic Dubai Mall", City = "Dubai", RegionName = "UAE North", StoreManagerName = "Rashid Al Marri", RegionalManagerName = "Fatima Al Suwaidi" },
            new Store { Name = "Panasonic Abu Dhabi Corniche", City = "Abu Dhabi", RegionName = "UAE South", StoreManagerName = "Salim Al Kaabi", RegionalManagerName = "Fatima Al Suwaidi" },
            new Store { Name = "Panasonic Sharjah City Centre", City = "Sharjah", RegionName = "UAE North", StoreManagerName = "Huda Al Zaabi", RegionalManagerName = "Fatima Al Suwaidi" },
        };
        db.Stores.AddRange(stores);
        await db.SaveChangesAsync();

        // Demo login accounts - one Store Manager per store (matching the seeded
        // manager names) plus one Regional Manager with access to every store.
        // All demo accounts share the same password for convenience.
        const string demoPassword = "Demo@12345";
        var users = new[]
        {
            new User { Username = "rashid.manager", PasswordHash = PasswordHasher.Hash(demoPassword), DisplayName = "Rashid Al Marri", Role = UserRole.StoreManager, StoreId = stores[0].Id },
            new User { Username = "salim.manager", PasswordHash = PasswordHasher.Hash(demoPassword), DisplayName = "Salim Al Kaabi", Role = UserRole.StoreManager, StoreId = stores[1].Id },
            new User { Username = "huda.manager", PasswordHash = PasswordHasher.Hash(demoPassword), DisplayName = "Huda Al Zaabi", Role = UserRole.StoreManager, StoreId = stores[2].Id },
            new User { Username = "fatima.regional", PasswordHash = PasswordHasher.Hash(demoPassword), DisplayName = "Fatima Al Suwaidi", Role = UserRole.RegionalManager, StoreId = null },
        };
        db.Users.AddRange(users);

        // Default threshold set above every seeded single-expense amount (max 43,000)
        // so existing demo data isn't retroactively flagged for approval; the Regional
        // Manager can lower it from the Store Management screen to demo the feature.
        db.ApprovalSettings.Add(new ApprovalSettings { ExpenseApprovalThreshold = 50000m });
        await db.SaveChangesAsync();

        const int year = 2026;
        const int month = 7;

        // Revenue is split across the 5 sellable product categories (indices 0-4;
        // "Operations Cost" at index 5 is overhead, not a sold product) so
        // Margin % by Product can be computed per category. Weights are a fixed
        // demo assumption, not derived data.
        var productCategoryWeights = new[] { 0.30m, 0.25m, 0.20m, 0.15m, 0.10m };

        // Cost-of-sales as a fraction of each category's revenue (fixed demo
        // assumption, aligned to categories[0..4] = TV, AC, Fridge, Washing
        // Machine, Microwave), giving each product a distinct, believable margin.
        var productCostRatios = new[] { 0.68m, 0.63m, 0.58m, 0.60m, 0.55m };

        static decimal[] SplitByWeights(decimal total, decimal[] weights)
        {
            var amounts = new decimal[weights.Length];
            var runningTotal = 0m;
            for (var i = 0; i < weights.Length - 1; i++)
            {
                amounts[i] = Math.Round(total * weights[i], 2);
                runningTotal += amounts[i];
            }
            amounts[^1] = total - runningTotal;
            return amounts;
        }

        // Historical months (Apr-Jun) so the dashboard trend chart has more than
        // one data point. Budget/forecast/revenue trend upward into July, and each
        // month gets a single auto-approved "Operations Cost" expense kept under
        // budget so it doesn't trigger the approval workflow.
        var historicalMonths = new[]
        {
            new { Month = 4, BudgetByStore = new[] { 45000m, 30000m, 25000m }, ForecastRevByStore = new[] { 98000m, 74000m, 58000m }, ForecastExpByStore = new[] { 44000m, 29000m, 24000m }, RevenueByStore = new[] { 95000m, 70000m, 55000m }, ExpenseByStore = new[] { 38000m, 26000m, 21000m } },
            new { Month = 5, BudgetByStore = new[] { 47000m, 32000m, 26000m }, ForecastRevByStore = new[] { 105000m, 79000m, 61000m }, ForecastExpByStore = new[] { 46000m, 31000m, 25000m }, RevenueByStore = new[] { 102000m, 76000m, 59000m }, ExpenseByStore = new[] { 41000m, 28000m, 22000m } },
            new { Month = 6, BudgetByStore = new[] { 48000m, 33000m, 27000m }, ForecastRevByStore = new[] { 112000m, 84000m, 64000m }, ForecastExpByStore = new[] { 47000m, 32500m, 26000m }, RevenueByStore = new[] { 110000m, 82000m, 62000m }, ExpenseByStore = new[] { 43000m, 29000m, 23000m } },
        };

        foreach (var hist in historicalMonths)
        {
            for (var i = 0; i < stores.Length; i++)
            {
                db.Budgets.Add(new Budget { Store = stores[i], Year = year, Month = hist.Month, Amount = hist.BudgetByStore[i] });
                db.Forecasts.Add(new Forecast { Store = stores[i], Year = year, Month = hist.Month, ForecastedRevenue = hist.ForecastRevByStore[i], ForecastedExpense = hist.ForecastExpByStore[i] });

                var splitAmounts = SplitByWeights(hist.RevenueByStore[i], productCategoryWeights);
                for (var c = 0; c < splitAmounts.Length; c++)
                {
                    db.Revenues.Add(new Revenue
                    {
                        Store = stores[i],
                        Category = categories[c],
                        Date = new DateOnly(year, hist.Month, 20),
                        Amount = splitAmounts[c],
                        CostOfSales = Math.Round(splitAmounts[c] * productCostRatios[c], 2),
                        Description = $"{categories[c].Name} sales"
                    });
                }
            }
        }
        await db.SaveChangesAsync();

        foreach (var hist in historicalMonths)
        {
            for (var i = 0; i < stores.Length; i++)
            {
                await approvalService.CreateExpenseAsync(
                    stores[i].Id,
                    categories[5].Id,
                    "Operations cost (monthly)",
                    hist.ExpenseByStore[i],
                    new DateOnly(year, hist.Month, 15));
            }
        }

        var budgets = new[]
        {
            new Budget { Store = stores[0], Year = year, Month = month, Amount = 50000m },
            new Budget { Store = stores[1], Year = year, Month = month, Amount = 35000m },
            new Budget { Store = stores[2], Year = year, Month = month, Amount = 28000m },
        };
        db.Budgets.AddRange(budgets);

        // TargetProfitAmount set so Dubai Mall and Sharjah demonstrate MET, Abu Dhabi
        // demonstrates NOT MET (actual July net profit: ~89,000 / ~62,250 / ~60,900).
        var forecasts = new[]
        {
            new Forecast { Store = stores[0], Year = year, Month = month, ForecastedRevenue = 120000m, ForecastedExpense = 48000m, TargetProfitAmount = 80000m },
            new Forecast { Store = stores[1], Year = year, Month = month, ForecastedRevenue = 90000m, ForecastedExpense = 34000m, TargetProfitAmount = 70000m },
            new Forecast { Store = stores[2], Year = year, Month = month, ForecastedRevenue = 70000m, ForecastedExpense = 27000m, TargetProfitAmount = 55000m },
        };
        db.Forecasts.AddRange(forecasts);

        var revenues = new List<Revenue>();
        var revenueByStore = new[] { 118500m, 87250m, 65900m };
        var julyRevenueDays = new[] { 5, 9, 13, 17, 21 };
        for (var i = 0; i < stores.Length; i++)
        {
            var splitAmounts = SplitByWeights(revenueByStore[i], productCategoryWeights);
            for (var c = 0; c < splitAmounts.Length; c++)
            {
                revenues.Add(new Revenue
                {
                    Store = stores[i],
                    Category = categories[c],
                    Date = new DateOnly(year, month, julyRevenueDays[c]),
                    Amount = splitAmounts[c],
                    CostOfSales = Math.Round(splitAmounts[c] * productCostRatios[c], 2),
                    Description = $"{categories[c].Name} sales"
                });
            }
        }
        db.Revenues.AddRange(revenues);

        await db.SaveChangesAsync();

        // Budget Planning: fiscal-year (Apr-Mar) line-item budgets, one initial forecast
        // version per store, and a demo forecast-revision approval. FiscalYear=2026 covers
        // Apr-2026 -> Mar-2027, which contains the app's "current" demo month (Jul-2026).
        const int fiscalYear = 2026;

        // Base line items sized so their FY total is ~50,000/month for Dubai Mall (matching
        // its existing monthly opex Budget), scaled down for the smaller stores. One item
        // (OPX-STAFF) uses the transcript's own 100,000 example. Two items are deliberately
        // seasonal (fewer months, uneven spread) rather than even across all 12.
        var lineItemTemplates = new (string GLCode, string Description, decimal[] ApprovedByStore, int StartMonthOffset, int NoOfMonths)[]
        {
            ("OPX-STAFF", "Staff Overtime & Wages", new[] { 100000m, 70000m, 56000m }, 0, 12),
            ("OPX-MKTG", "Marketing & Promotions", new[] { 120000m, 84000m, 67000m }, 0, 4),
            ("OPX-MAINT", "Repair & Maintenance", new[] { 90000m, 63000m, 50000m }, 0, 12),
            ("OPX-UTIL", "Utilities", new[] { 72000m, 50000m, 40000m }, 0, 12),
            ("OPX-IT", "IT & POS Systems", new[] { 48000m, 34000m, 27000m }, 0, 12),
            ("OPX-LOGI", "Logistics & Freight", new[] { 72000m, 50000m, 40000m }, 0, 12),
            ("OPX-TRAIN", "Staff Training", new[] { 24000m, 17000m, 13000m }, 2, 2),
            ("OPX-MISC", "Miscellaneous Operations", new[] { 74000m, 52000m, 43000m }, 0, 12),
        };

        var fiscalYearStart = new DateOnly(fiscalYear, 4, 1);
        var lineItemIdByStoreAndCode = new Dictionary<(int StoreIdx, string GLCode), int>();

        for (var s = 0; s < stores.Length; s++)
        {
            foreach (var template in lineItemTemplates)
            {
                var approvedAmount = template.ApprovedByStore[s];
                var startDate = fiscalYearStart.AddMonths(template.StartMonthOffset);
                var endDate = startDate.AddMonths(template.NoOfMonths).AddDays(-1);

                var lineItem = await budgetPlanningService.CreateLineItemAsync(stores[s].Id, fiscalYear, new Dtos.UpsertLineItemRequest(
                    template.GLCode, template.Description, "Store Ops", "Operations Cost",
                    Units: 1, Rate: approvedAmount, StartDate: startDate, EndDate: endDate, NoOfMonths: template.NoOfMonths, ApprovedAmount: approvedAmount));

                lineItemIdByStoreAndCode[(s, template.GLCode)] = lineItem.Id;
            }
        }

        // Give each store a Total Allocated Budget slightly above its line items'
        // approved sum - added directly (not via CreateLineItemAsync) so seeding the
        // line items above isn't itself subject to the ceiling. Leaves enough
        // headroom for normal edits, while a large new line item will still breach
        // it, demonstrating the cross-GL-code validation.
        for (var s = 0; s < stores.Length; s++)
        {
            var approvedSum = lineItemTemplates.Sum(t => t.ApprovedByStore[s]);
            db.StoreAllocatedBudgets.Add(new StoreAllocatedBudget
            {
                StoreId = stores[s].Id,
                FiscalYear = fiscalYear,
                TotalAllocatedBudget = approvedSum + 50000m
            });
        }
        await db.SaveChangesAsync();

        // Initial forecast version per store = the budget split exactly (matches "the
        // initial forecast usually equals the budget"). Created directly since
        // LockForecastVersionAsync requires a prior current version to freeze.
        for (var s = 0; s < stores.Length; s++)
        {
            var storeLineItems = await db.BudgetLineItems
                .Include(li => li.Months)
                .Where(li => li.StoreId == stores[s].Id && li.FiscalYear == fiscalYear)
                .ToListAsync();

            var initialVersion = new ForecastVersion
            {
                StoreId = stores[s].Id,
                FiscalYear = fiscalYear,
                AsOfCalendarYear = fiscalYear,
                AsOfCalendarMonth = 3,
                Label = "Initial (locked Mar-2026)",
                CreatedAt = DateTime.UtcNow,
                IsCurrent = true
            };
            db.ForecastVersions.Add(initialVersion);
            await db.SaveChangesAsync();

            foreach (var li in storeLineItems)
            {
                foreach (var monthAmount in li.Months)
                {
                    db.ForecastVersionLines.Add(new ForecastVersionLine
                    {
                        ForecastVersionId = initialVersion.Id,
                        BudgetLineItemId = li.Id,
                        CalendarYear = monthAmount.CalendarYear,
                        CalendarMonth = monthAmount.CalendarMonth,
                        Amount = monthAmount.Amount
                    });
                }
            }
        }
        await db.SaveChangesAsync();

        // Expenses: a mix of under-budget (auto-approved) and over-budget
        // (triggers the 2-level Store Manager -> Regional Manager workflow) entries.
        var expenseSeeds = new (int StoreIdx, int CategoryIdx, string Description, decimal Amount, int Day, string? LineItemGLCode)[]
        {
            (0, 5, "Store staff overtime", 8000m, 2, "OPX-STAFF"),
            (0, 5, "Marketing display refresh", 12000m, 6, "OPX-MKTG"),
            (0, 4, "Demo unit stock replenishment", 9500m, 10, null),
            (0, 1, "AC showroom cooling unit repair", 22000m, 14, null), // 51,500 total vs 50,000 budget -> triggers approval
            (1, 5, "Store staff overtime", 6000m, 3, "OPX-STAFF"),
            (1, 2, "Refrigerator display damage write-off", 10000m, 9, null),
            (1, 3, "Washing machine demo unit purchase", 9000m, 15, null),
            (2, 5, "Store staff overtime", 5000m, 4, "OPX-STAFF"),
            (2, 0, "TV wall display installation", 24000m, 11, null), // 29,000 total vs 28,000 budget -> triggers approval
        };

        foreach (var seed in expenseSeeds)
        {
            int? lineItemId = seed.LineItemGLCode != null
                ? lineItemIdByStoreAndCode[(seed.StoreIdx, seed.LineItemGLCode)]
                : null;

            await approvalService.CreateExpenseAsync(
                stores[seed.StoreIdx].Id,
                categories[seed.CategoryIdx].Id,
                seed.Description,
                seed.Amount,
                new DateOnly(year, month, seed.Day),
                lineItemId);
        }

        // Headcount: light-touch planning rows, one per store, showing under/adequate/overstaffed.
        var headcounts = new[]
        {
            new Headcount { Store = stores[0], Year = year, Month = month, CurrentHeadcount = 8, ForecastedHeadcountNeed = 10 },
            new Headcount { Store = stores[1], Year = year, Month = month, CurrentHeadcount = 6, ForecastedHeadcountNeed = 6 },
            new Headcount { Store = stores[2], Year = year, Month = month, CurrentHeadcount = 5, ForecastedHeadcountNeed = 4 },
        };
        db.Headcounts.AddRange(headcounts);
        await db.SaveChangesAsync();

        // Dubai Mall gets a second, revised forecast version so the deviation-highlight
        // and version-history features have something to show on first run. The May-2026
        // Marketing forecast is bumped 50% above its budget (well past the 15% flag threshold).
        var dubaiMallId = stores[0].Id;
        await budgetPlanningService.LockForecastVersionAsync(dubaiMallId, fiscalYear, new DateOnly(fiscalYear, 4, 1), "As of Apr-2026");

        var dubaiMktgLineItemId = lineItemIdByStoreAndCode[(0, "OPX-MKTG")];
        var dubaiCurrentVersion = await db.ForecastVersions
            .Include(v => v.Lines)
            .FirstAsync(v => v.StoreId == dubaiMallId && v.FiscalYear == fiscalYear && v.IsCurrent);
        var mayMktgLine = dubaiCurrentVersion.Lines.First(l =>
            l.BudgetLineItemId == dubaiMktgLineItemId && l.CalendarYear == 2026 && l.CalendarMonth == 5);
        mayMktgLine.Amount = Math.Round(mayMktgLine.Amount * 1.5m, 2);
        await db.SaveChangesAsync();

        // One pending forecast-revision approval (Dubai Mall, Logistics line, an August
        // increase that pushes the line item's FY total past its approved cap) so the new
        // approval type is visible in the inbox without the presenter triggering it live.
        var dubaiLogiLineItemId = lineItemIdByStoreAndCode[(0, "OPX-LOGI")];
        await budgetPlanningService.ProposeForecastChangesAsync(
            dubaiMallId,
            fiscalYear,
            new List<Dtos.BudgetCellSaveDto> { new(dubaiLogiLineItemId, 2026, 8, 26000m) },
            new DateOnly(year, month, 15));
    }
}

using Microsoft.EntityFrameworkCore;
using PanasonicPrototype.Api.Models;

namespace PanasonicPrototype.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Store> Stores => Set<Store>();
    public DbSet<ApplianceCategory> ApplianceCategories => Set<ApplianceCategory>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<Approval> Approvals => Set<Approval>();
    public DbSet<Revenue> Revenues => Set<Revenue>();
    public DbSet<Forecast> Forecasts => Set<Forecast>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<DiscountApproval> DiscountApprovals => Set<DiscountApproval>();
    public DbSet<Headcount> Headcounts => Set<Headcount>();
    public DbSet<User> Users => Set<User>();
    public DbSet<ApprovalSettings> ApprovalSettings => Set<ApprovalSettings>();
    public DbSet<BudgetLineItem> BudgetLineItems => Set<BudgetLineItem>();
    public DbSet<BudgetLineItemMonth> BudgetLineItemMonths => Set<BudgetLineItemMonth>();
    public DbSet<ForecastVersion> ForecastVersions => Set<ForecastVersion>();
    public DbSet<ForecastVersionLine> ForecastVersionLines => Set<ForecastVersionLine>();
    public DbSet<ForecastRevisionApproval> ForecastRevisionApprovals => Set<ForecastRevisionApproval>();
    public DbSet<StoreAllocatedBudget> StoreAllocatedBudgets => Set<StoreAllocatedBudget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Store>()
            .HasMany(s => s.Budgets)
            .WithOne(b => b.Store)
            .HasForeignKey(b => b.StoreId);

        modelBuilder.Entity<Store>()
            .HasMany(s => s.Expenses)
            .WithOne(e => e.Store)
            .HasForeignKey(e => e.StoreId);

        modelBuilder.Entity<Store>()
            .HasMany(s => s.Revenues)
            .WithOne(r => r.Store)
            .HasForeignKey(r => r.StoreId);

        modelBuilder.Entity<Store>()
            .HasMany(s => s.Forecasts)
            .WithOne(f => f.Store)
            .HasForeignKey(f => f.StoreId);

        modelBuilder.Entity<Expense>()
            .HasOne(e => e.Category)
            .WithMany()
            .HasForeignKey(e => e.CategoryId);

        modelBuilder.Entity<Revenue>()
            .HasOne(r => r.Category)
            .WithMany()
            .HasForeignKey(r => r.CategoryId);

        modelBuilder.Entity<Expense>()
            .HasMany(e => e.Approvals)
            .WithOne(a => a.Expense)
            .HasForeignKey(a => a.ExpenseId);

        modelBuilder.Entity<Expense>()
            .Property(e => e.Status)
            .HasConversion<string>();

        modelBuilder.Entity<Approval>()
            .Property(a => a.Decision)
            .HasConversion<string>();

        modelBuilder.Entity<Budget>()
            .HasIndex(b => new { b.StoreId, b.Year, b.Month })
            .IsUnique();

        modelBuilder.Entity<Forecast>()
            .HasIndex(f => new { f.StoreId, f.Year, f.Month })
            .IsUnique();

        modelBuilder.Entity<Store>()
            .HasMany<Sale>()
            .WithOne(s => s.Store)
            .HasForeignKey(s => s.StoreId);

        modelBuilder.Entity<Sale>()
            .HasMany(s => s.Approvals)
            .WithOne(a => a.Sale)
            .HasForeignKey(a => a.SaleId);

        modelBuilder.Entity<Sale>()
            .Property(s => s.Status)
            .HasConversion<string>();

        modelBuilder.Entity<DiscountApproval>()
            .Property(a => a.Decision)
            .HasConversion<string>();

        modelBuilder.Entity<Store>()
            .HasMany<Headcount>()
            .WithOne(h => h.Store)
            .HasForeignKey(h => h.StoreId);

        modelBuilder.Entity<Headcount>()
            .HasIndex(h => new { h.StoreId, h.Year, h.Month })
            .IsUnique();

        modelBuilder.Entity<User>()
            .HasOne(u => u.Store)
            .WithMany()
            .HasForeignKey(u => u.StoreId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<User>()
            .Property(u => u.Role)
            .HasConversion<string>();

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username)
            .IsUnique();

        modelBuilder.Entity<Store>()
            .HasMany<BudgetLineItem>()
            .WithOne(li => li.Store)
            .HasForeignKey(li => li.StoreId);

        modelBuilder.Entity<BudgetLineItem>()
            .HasIndex(li => new { li.StoreId, li.FiscalYear, li.GLCode })
            .IsUnique();

        modelBuilder.Entity<BudgetLineItem>()
            .HasMany(li => li.Months)
            .WithOne(m => m.BudgetLineItem)
            .HasForeignKey(m => m.BudgetLineItemId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<BudgetLineItemMonth>()
            .HasIndex(m => new { m.BudgetLineItemId, m.CalendarYear, m.CalendarMonth })
            .IsUnique();

        modelBuilder.Entity<Store>()
            .HasMany<ForecastVersion>()
            .WithOne(v => v.Store)
            .HasForeignKey(v => v.StoreId);

        modelBuilder.Entity<ForecastVersion>()
            .HasMany(v => v.Lines)
            .WithOne(l => l.ForecastVersion)
            .HasForeignKey(l => l.ForecastVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ForecastVersionLine>()
            .HasOne(l => l.BudgetLineItem)
            .WithMany()
            .HasForeignKey(l => l.BudgetLineItemId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ForecastVersionLine>()
            .HasIndex(l => new { l.ForecastVersionId, l.BudgetLineItemId, l.CalendarYear, l.CalendarMonth })
            .IsUnique();

        modelBuilder.Entity<Expense>()
            .HasOne(e => e.BudgetLineItem)
            .WithMany()
            .HasForeignKey(e => e.BudgetLineItemId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<ForecastRevisionApproval>()
            .HasOne(a => a.Store)
            .WithMany()
            .HasForeignKey(a => a.StoreId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ForecastRevisionApproval>()
            .HasOne(a => a.BudgetLineItem)
            .WithMany()
            .HasForeignKey(a => a.BudgetLineItemId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ForecastRevisionApproval>()
            .Property(a => a.Decision)
            .HasConversion<string>();

        modelBuilder.Entity<Store>()
            .HasMany<StoreAllocatedBudget>()
            .WithOne(a => a.Store)
            .HasForeignKey(a => a.StoreId);

        modelBuilder.Entity<StoreAllocatedBudget>()
            .HasIndex(a => new { a.StoreId, a.FiscalYear })
            .IsUnique();
    }
}

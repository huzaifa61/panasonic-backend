using Microsoft.EntityFrameworkCore;
using PanasonicPrototype.Api.Data;
using PanasonicPrototype.Api.Models;

namespace PanasonicPrototype.Api.Services;

public class DiscountApprovalService
{
    private readonly AppDbContext _db;

    // Discount tiers, per the client's own example (20% default + 5% for a reputed
    // customer = 25% needs approval; anything beyond that is "exceptional" and
    // escalates further up the hierarchy).
    private const decimal AutoApproveThreshold = 20m;
    private const decimal ExceptionalThreshold = 25m;

    public DiscountApprovalService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Sale> CreateSaleAsync(int storeId, string customerName, decimal saleAmount, decimal requestedDiscountPercent, DateOnly date)
    {
        var store = await _db.Stores.FindAsync(storeId)
            ?? throw new ArgumentException($"Store {storeId} not found.");

        var needsApproval = requestedDiscountPercent > AutoApproveThreshold;
        var isExceptional = requestedDiscountPercent > ExceptionalThreshold;

        var sale = new Sale
        {
            StoreId = storeId,
            CustomerName = customerName,
            SaleAmount = saleAmount,
            RequestedDiscountPercent = requestedDiscountPercent,
            Date = date,
            CreatedAt = DateTime.UtcNow,
            Status = needsApproval ? SaleStatus.PendingLevel1Approval : SaleStatus.AutoApproved
        };

        _db.Sales.Add(sale);
        await _db.SaveChangesAsync();

        if (needsApproval)
        {
            // Both levels are created upfront for an exceptional discount (mirrors the
            // budget-approval pattern) so the full escalation chain is visible for audit,
            // but Level 2 only becomes actionable once Level 1 approves (see DecideDiscountApprovalAsync).
            _db.DiscountApprovals.Add(new DiscountApproval
            {
                SaleId = sale.Id,
                Level = 1,
                ApproverRole = "Store Manager",
                ApproverName = store.StoreManagerName,
                Decision = ApprovalDecision.Pending
            });

            if (isExceptional)
            {
                _db.DiscountApprovals.Add(new DiscountApproval
                {
                    SaleId = sale.Id,
                    Level = 2,
                    ApproverRole = "Regional Manager",
                    ApproverName = store.RegionalManagerName,
                    Decision = ApprovalDecision.Pending
                });
            }

            await _db.SaveChangesAsync();
        }

        return sale;
    }

    public async Task<DiscountApproval> DecideDiscountApprovalAsync(int approvalId, bool approve, string? comments)
    {
        var approval = await _db.DiscountApprovals
            .Include(a => a.Sale)
            .FirstOrDefaultAsync(a => a.Id == approvalId)
            ?? throw new ArgumentException($"Discount approval {approvalId} not found.");

        if (approval.Decision != ApprovalDecision.Pending)
        {
            throw new InvalidOperationException("This approval has already been decided.");
        }

        var sale = approval.Sale!;

        if (approval.Level == 2 && sale.Status != SaleStatus.PendingLevel2Approval)
        {
            throw new InvalidOperationException("Level 1 approval must be completed before Level 2 can act.");
        }
        if (approval.Level == 1 && sale.Status != SaleStatus.PendingLevel1Approval)
        {
            throw new InvalidOperationException("This sale is not awaiting Level 1 approval.");
        }

        approval.Decision = approve ? ApprovalDecision.Approved : ApprovalDecision.Rejected;
        approval.DecidedAt = DateTime.UtcNow;
        approval.Comments = comments;

        if (!approve)
        {
            sale.Status = SaleStatus.Rejected;
        }
        else if (approval.Level == 1)
        {
            var hasLevel2 = await _db.DiscountApprovals.AnyAsync(a => a.SaleId == sale.Id && a.Level == 2);
            sale.Status = hasLevel2 ? SaleStatus.PendingLevel2Approval : SaleStatus.Approved;
        }
        else
        {
            sale.Status = SaleStatus.Approved;
        }

        await _db.SaveChangesAsync();
        return approval;
    }
}

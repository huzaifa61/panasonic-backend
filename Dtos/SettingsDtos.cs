namespace PanasonicPrototype.Api.Dtos;

public record ApprovalSettingsDto(decimal ExpenseApprovalThreshold);

public record UpdateApprovalSettingsRequest(decimal ExpenseApprovalThreshold);

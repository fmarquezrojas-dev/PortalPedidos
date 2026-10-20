namespace Portal.Application.DTOs.Erp;

public sealed class ErpCustomerDto
{
    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public bool Active { get; init; }
}
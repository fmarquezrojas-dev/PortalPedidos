namespace ErpSimulator.Api.Contracts;

public class ErpItemDto
{
    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string UnitCode { get; init; } = string.Empty;

    public decimal UnitPrice { get; init; }

    public bool Active { get; init; }
}
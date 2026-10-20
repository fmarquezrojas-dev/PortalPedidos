namespace ErpSimulator.Api.Contracts;

public class ErpStockDto
{
    public string ItemCode { get; init; } = string.Empty;

    public string WarehouseCode { get; init; } = string.Empty;

    public decimal Available { get; init; }
}
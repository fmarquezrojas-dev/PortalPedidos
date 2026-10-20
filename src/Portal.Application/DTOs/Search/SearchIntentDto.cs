using System;
using Portal.Domain.Enums;
namespace Portal.Application.DTOs.Search;

public enum SearchArea { Unrecognized, Orders, Customers, Catalog }

public sealed class SearchIntentDto
{
    public SearchArea Area { get; init; }
    public string? Term { get; init; }
    public OrderStatus? OrderStatus { get; init; }
    public bool? CustomerIsActive { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public string Message { get; init; } = string.Empty;
}

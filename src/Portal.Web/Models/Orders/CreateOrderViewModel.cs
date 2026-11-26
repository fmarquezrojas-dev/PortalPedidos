using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Portal.Application.DTOs.Orders;

namespace Portal.Web.Models.Orders;

public sealed class CreateOrderViewModel
{
    public Guid DraftId { get; set; }

    public long CatalogVersion { get; set; }

    public string? RowVersion { get; set; }

    [Required(ErrorMessage = "Selecciona un cliente.")]
    [Display(Name = "Cliente")]
    public Guid? CustomerId { get; set; }

    [Required(ErrorMessage = "Indica la fecha solicitada.")]
    [DataType(DataType.Date)]
    [Display(Name = "Fecha solicitada")]
    public DateOnly? RequestedDate { get; set; }

    public List<CreateOrderLineViewModel> Lines { get; set; } = new();

    [Display(Name = "He revisado los precios actuales y el importe.")]
    public bool AcceptCurrentPrices { get; set; }

    [BindNever]
    [ValidateNever]
    public bool IsEdit { get; set; }

    [BindNever]
    [ValidateNever]
    public long? PublicNumber { get; set; }

    [BindNever]
    [ValidateNever]
    public decimal? SavedSubtotal { get; set; }

    [BindNever]
    [ValidateNever]
    public IReadOnlyDictionary<Guid, decimal> SavedPrices { get; set; } = new Dictionary<Guid, decimal>();

    [BindNever]
    [ValidateNever]
    public OrderDraftFormDataDto FormData { get; set; } = new();
}

public sealed class CreateOrderLineViewModel
{
    [Required(ErrorMessage = "Selecciona el producto de cada línea.")]
    public Guid? ProductId { get; set; }

    // Precio mostrado en el navegador, solo para explicar cambios al volver al formulario.
    public string? QuotedPrice { get; set; }

    [Range(
        1,
        9999,
        ErrorMessage = "La cantidad debe estar entre 1 y 9999.")]
    public int Quantity { get; set; } = 1;
}
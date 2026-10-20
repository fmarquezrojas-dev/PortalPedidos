using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace Portal.Web.Models.Orders;

public sealed class CancelOrderViewModel
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Falta la versión del pedido.")]
    public string RowVersion { get; set; } = string.Empty;

    [Required(ErrorMessage = "Indica el motivo de cancelación.")]
    [StringLength(
        250,
        MinimumLength = 5,
        ErrorMessage = "El motivo debe tener entre 5 y 250 caracteres.")]
    [Display(Name = "Motivo de cancelación")]
    public string Reason { get; set; } = string.Empty;

    public bool ConfirmCancellation { get; set; }

    [BindNever]
    [ValidateNever]
    public long PublicNumber { get; set; }

    [BindNever]
    [ValidateNever]
    public string CustomerName { get; set; } = string.Empty;

    [BindNever]
    [ValidateNever]
    public string Currency { get; set; } = string.Empty;

    [BindNever]
    [ValidateNever]
    public decimal Subtotal { get; set; }

    [BindNever]
    [ValidateNever]
    public bool CanCancel { get; set; }
}
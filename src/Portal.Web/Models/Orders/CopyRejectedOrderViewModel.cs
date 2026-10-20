using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Portal.Application.DTOs.Orders;

namespace Portal.Web.Models.Orders;

public class CopyRejectedOrderViewModel
{
    public Guid SourceOrderId { get; set; }

    public Guid DraftId { get; set; }

    public long CatalogVersion { get; set; }

    [Required(ErrorMessage = "Selecciona una fecha solicitada.")]
    [DataType(DataType.Date)]
    [Display(Name = "Nueva fecha solicitada")]
    public DateOnly? RequestedDate { get; set; }

    public bool ConfirmCopy { get; set; }

    [BindNever]
    [ValidateNever]
    public CopyRejectedOrderPreviewDto Preview { get; set; } = new();

    [BindNever]
    [ValidateNever]
    public bool RequiresReload { get; set; }
}
using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Portal.Application.DTOs.Integration;

namespace Portal.Web.Models.Integration;

public sealed class RecoverIntegrationViewModel
{
    public Guid MessageId { get; set; }

    [Required]
    [StringLength(32)]
    public string RowVersion { get; set; } = string.Empty;

    [Required(ErrorMessage = "Escribe el motivo de la recuperación.")]
    [StringLength(
        300,
        MinimumLength = 5,
        ErrorMessage = "El motivo debe tener entre 5 y 300 caracteres.")]
    [Display(Name = "Motivo de la recuperación")]
    public string Reason { get; set; } = string.Empty;

    public bool ConfirmRecovery { get; set; }

    [BindNever]
    [ValidateNever]
    public IntegrationRecoveryPreviewDto Preview { get; set; } = new();

    [BindNever]
    [ValidateNever]
    public bool RequiresReload { get; set; }
}
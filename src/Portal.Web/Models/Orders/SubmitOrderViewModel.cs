using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Portal.Application.DTOs.Orders;

namespace Portal.Web.Models.Orders;

public sealed class SubmitOrderViewModel
{
    public Guid Id { get; set; }

    [Required]
    public string RowVersion { get; set; } = string.Empty;

    public bool ConfirmSubmission { get; set; }

    [BindNever]
    [ValidateNever]
    public OrderDetailDto Details { get; set; } = new();

    [BindNever]
    [ValidateNever]
    public bool CanSubmit { get; set; }
}
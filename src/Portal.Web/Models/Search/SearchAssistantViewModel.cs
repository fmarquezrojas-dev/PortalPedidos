using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Portal.Application.DTOs.Search;
namespace Portal.Web.Models.Search;

public sealed class SearchAssistantViewModel
{
    [Required(ErrorMessage = "Escribe qué necesitas buscar.")]
    [StringLength(160, MinimumLength = 2, ErrorMessage = "Escribe entre 2 y 160 caracteres.")]
    public string Question { get; set; } = string.Empty;
    [BindNever] public SearchIntentDto? Intent { get; set; }
    [BindNever] public string? Destination { get; set; }
}

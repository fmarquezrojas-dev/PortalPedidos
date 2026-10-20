using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portal.Application.Interfaces.Customers;
using Portal.Web.Security;

namespace Portal.Web.Controllers;

[Authorize(Policy = AppPolicies.CreateOrders)]
[Route("customer-eligibility")]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class CustomerEligibilityController : Controller
{
    private readonly ICustomerEligibilityQuery _query;

    public CustomerEligibilityController(
        ICustomerEligibilityQuery query)
    {
        _query = query;
    }

    [HttpGet("{customerId:guid}")]
    public async Task<IActionResult> Get(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        if (customerId == Guid.Empty)
        {
            return BadRequest(new
            {
                code = "CUSTOMER_ID_INVALID",
                message = "El identificador del cliente no es válido."
            });
        }

        try
        {
            var result = await _query.GetAsync(
                customerId,
                cancellationToken);

            if (result is null)
            {
                return NotFound(new
                {
                    code = "CUSTOMER_UNAVAILABLE",
                    message =
                        "El cliente ya no está disponible. "
                        + "Actualiza el formulario."
                });
            }

            return Json(new
            {
                isEligible = result.IsEligible,
                code = result.Code,
                message = result.Message
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }
}
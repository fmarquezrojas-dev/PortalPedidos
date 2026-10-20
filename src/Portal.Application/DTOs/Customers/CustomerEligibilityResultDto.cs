namespace Portal.Application.DTOs.Customers;

public sealed class CustomerEligibilityResultDto
{
    public bool IsEligible { get; }

    public string? Code { get; }

    public string? Message { get; }

    private CustomerEligibilityResultDto(
        bool isEligible,
        string? code,
        string? message)
    {
        IsEligible = isEligible;
        Code = code;
        Message = message;
    }

    public static CustomerEligibilityResultDto Eligible()
    {
        return new CustomerEligibilityResultDto(
            isEligible: true,
            code: null,
            message: null);
    }

    public static CustomerEligibilityResultDto Unavailable()
    {
        return new CustomerEligibilityResultDto(
            isEligible: false,
            code: "CUSTOMER_UNAVAILABLE",
            message:
                "El cliente ya no está disponible. Revisa el pedido.");
    }

    public static CustomerEligibilityResultDto Inactive()
    {
        return new CustomerEligibilityResultDto(
            isEligible: false,
            code: "CUSTOMER_INACTIVE",
            message:
                "El cliente está inactivo y no puede utilizarse "
                + "para esta operación.");
    }
}
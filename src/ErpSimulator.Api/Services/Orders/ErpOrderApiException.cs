using System;

namespace ErpSimulator.Api.Services.Orders;

public sealed class ErpOrderApiException : Exception
{
    public ErpOrderApiException(
        int statusCode,
        string code,
        string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }

    public string Code { get; }
}
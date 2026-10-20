using System;

namespace Portal.Application.Exceptions;

public sealed class ErpClientException : Exception
{
    public string Code { get; }

    public ErpClientException(
        string code,
        string message)
        : base(message)
    {
        Code = code;
    }
}
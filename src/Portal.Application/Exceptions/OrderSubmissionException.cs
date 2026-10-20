using System;

namespace Portal.Application.Exceptions;

public sealed class OrderSubmissionException : Exception
{
    public string Code { get; }

    public OrderSubmissionException(
        string code,
        string message)
        : base(message)
    {
        Code = code;
    }
}
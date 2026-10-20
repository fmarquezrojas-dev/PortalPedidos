using System;

namespace Portal.Application.Exceptions;

public sealed class ErpOrderCommunicationException : Exception
{
    public ErpOrderCommunicationException(
        string code,
        string message,
        bool isTransient,
        bool outcomeUncertain,
        int? httpStatusCode = null,
        int? retryAfterSeconds = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        IsTransient = isTransient;
        OutcomeUncertain = outcomeUncertain;
        HttpStatusCode = httpStatusCode;
        RetryAfterSeconds = retryAfterSeconds;
    }

    public string Code { get; }

    public bool IsTransient { get; }

    public bool OutcomeUncertain { get; }

    public int? HttpStatusCode { get; }

    public int? RetryAfterSeconds { get; }
}
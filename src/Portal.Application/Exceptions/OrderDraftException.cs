using System;

namespace Portal.Application.Exceptions;

public sealed class OrderDraftException : Exception
{
    public OrderDraftException(string message)
        : base(message)
    {
    }
}
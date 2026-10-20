using System;

namespace Portal.Application.Exceptions;

public sealed class CustomerPortfolioException : Exception
{
    public CustomerPortfolioException(string message)
        : base(message)
    {
    }
}
using System;

namespace Portal.Application.Exceptions;

public sealed class SalesTeamException : Exception
{
    public SalesTeamException(string message)
        : base(message)
    {
    }
}
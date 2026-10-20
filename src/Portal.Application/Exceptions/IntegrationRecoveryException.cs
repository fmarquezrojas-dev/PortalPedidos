using System;

namespace Portal.Application.Exceptions;

public sealed class IntegrationRecoveryException : Exception
{
    public IntegrationRecoveryException(string message)
        : base(message)
    {
    }
}
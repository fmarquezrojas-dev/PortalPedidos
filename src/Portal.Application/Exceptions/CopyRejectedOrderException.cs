using System;

namespace Portal.Application.Exceptions;

public class CopyRejectedOrderException : Exception
{
    public CopyRejectedOrderException(string message)
        : base(message)
    {
    }
}
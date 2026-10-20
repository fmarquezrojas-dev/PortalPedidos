using System;
namespace Portal.Application.Exceptions;
public sealed class UserAdministrationException : Exception
{
    public bool IsConflict { get; }
    public UserAdministrationException(string message, bool isConflict = false) : base(message)
        => IsConflict = isConflict;
}

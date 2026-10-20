using System;

namespace Portal.Application.Exceptions;

public sealed class OrderDraftConflictException : Exception
{
    public OrderDraftConflictException()
        : base(
            "El pedido cambió desde que abriste el formulario. " +
            "Tus cambios no se guardaron. Recarga el pedido " +
            "y revisa la versión actual antes de editar nuevamente.")
    {
    }
}
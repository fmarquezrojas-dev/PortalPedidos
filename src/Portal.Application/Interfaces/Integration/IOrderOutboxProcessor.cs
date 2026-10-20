using System.Threading;
using System.Threading.Tasks;

namespace Portal.Application.Interfaces.Integration;

public interface IOrderOutboxProcessor
{
    /// <summary>
    /// Intenta tomar y procesar un mensaje disponible.
    /// Devuelve false cuando no hay mensajes disponibles.
    /// Devuelve true cuando se atendió un mensaje, aunque su
    /// resultado haya sido un rechazo o un reintento programado.
    /// </summary>
    Task<bool> ProcessNextAsync(
        CancellationToken cancellationToken = default);
}
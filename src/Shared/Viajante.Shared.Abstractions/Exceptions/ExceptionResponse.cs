using System.Net;

namespace Viajante.Shared.Abstractions.Exceptions
{
    public sealed record ExceptionResponse(object Response, HttpStatusCode StatusCode);
}

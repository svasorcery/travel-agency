using System;

namespace Viajante.Shared.Abstractions.Exceptions
{
    internal interface IExceptionToResponseMapper
    {
        ExceptionResponse Map(Exception exception);
    }
}

using System;

namespace Viajante.Shared.Abstractions.Exceptions
{
    public class ViajanteException : Exception
    {
        protected ViajanteException(string message) : base(message)
        {
        }
    }
}

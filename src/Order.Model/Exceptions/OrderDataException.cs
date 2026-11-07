using System;

namespace Order.Model.Exceptions
{
    public class OrderDataException : Exception
    {
        public OrderDataException(string message) : base(message)
        {
        }
    }
}

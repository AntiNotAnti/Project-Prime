using System;

namespace MphRead
{
    public class ProgramException : Exception
    {
        public ProgramException(string message) : base(message) { }
    }
}

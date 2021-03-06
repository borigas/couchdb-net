﻿namespace CouchDB.Driver.Exceptions
{
    /// <summary>
    /// The exception that is thrown when something is not found.
    /// </summary>
    public class CouchNotFoundException : CouchException
    {
        /// <summary>
        /// Creates a new instance of CouchNotFoundException.
        /// </summary>
        /// <param name="message">Error message</param>
        /// <param name="reason">Error reason</param>
        internal CouchNotFoundException(string message, string reason) : base(message, reason) { }
    }
}

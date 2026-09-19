namespace ERP_infrastructure.tenant
{
    /// <summary>
    /// Raised when the tenant for a request cannot be determined, or when its database
    /// configuration is missing or unusable. The message is written to be safe to log; the
    /// API maps this to a 400-level response and never echoes a connection string.
    /// </summary>
    public class TenantResolutionException : Exception
    {
        public TenantResolutionException(string message) : base(message)
        {
        }

        public TenantResolutionException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}

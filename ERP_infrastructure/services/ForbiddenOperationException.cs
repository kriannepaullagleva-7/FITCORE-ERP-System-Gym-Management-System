namespace ERP_infrastructure.services
{
    /// <summary>
    /// The caller is who they say they are, but is not allowed to do this.
    ///
    /// Distinct from <see cref="ValidationException"/>, which means "fix what you typed".
    /// Nothing the user retypes will make this succeed, so the API answers 403 rather than
    /// 400 and the desktop shows it as a refusal rather than a form error.
    ///
    /// Thrown by the services, not by the controllers. A rule enforced only at the HTTP edge
    /// is one a different caller can walk around; enforced here it holds for every caller.
    /// </summary>
    public class ForbiddenOperationException : Exception
    {
        public ForbiddenOperationException(string message) : base(message)
        {
        }
    }
}

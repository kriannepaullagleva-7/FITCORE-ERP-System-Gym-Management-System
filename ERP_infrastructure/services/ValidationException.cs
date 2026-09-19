namespace ERP_infrastructure.services
{
    // A rule the user can fix by changing what they typed, as opposed to a fault. The forms
    // show these as a plain warning instead of an error dialog, and they are distinct from
    // the InvalidOperationException EF Core raises for genuine data-access problems.
    public class ValidationException : Exception
    {
        public ValidationException(string message) : base(message)
        {
        }
    }
}

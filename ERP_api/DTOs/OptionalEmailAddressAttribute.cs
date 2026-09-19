using System.ComponentModel.DataAnnotations;

namespace ERP_api.DTOs
{
    /// <summary>
    /// Validates an email address only when one was supplied.
    ///
    /// The built-in <see cref="EmailAddressAttribute"/> treats an empty string as invalid, so
    /// applying it directly to an optional field makes the field effectively required: a
    /// member or employee with no email address on file could not be saved. This accepts
    /// null, empty and whitespace, and checks the format for anything else.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
    public sealed class OptionalEmailAddressAttribute : ValidationAttribute
    {
        private static readonly EmailAddressAttribute Inner = new();

        public OptionalEmailAddressAttribute()
            : base("Enter a valid email address.")
        {
        }

        public override bool IsValid(object? value)
        {
            if (value is null)
            {
                return true;
            }

            if (value is string text && string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            return Inner.IsValid(value);
        }
    }
}

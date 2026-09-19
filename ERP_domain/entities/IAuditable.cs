namespace ERP_domain.entities
{
    /// <summary>
    /// Marks a record whose last change is worth knowing about.
    ///
    /// <see cref="UpdatedAt"/> is stamped centrally by the DbContext when the row is modified,
    /// so no service has to remember to set it and none of them can disagree about what "now"
    /// means.
    ///
    /// There is deliberately no CreatedBy/UpdatedBy here. This deployment has no
    /// authentication yet, so there is no user to attribute a change to, and a column that
    /// would always be null records nothing while implying it records something. The fields
    /// that can be attributed to a person today - the cashier on a sale, the staff member on a
    /// stock movement or an expense - are explicit foreign keys on those entities instead.
    /// </summary>
    public interface IAuditable
    {
        DateTime CreatedAt { get; set; }

        /// <summary>Null until the record is first changed after being created.</summary>
        DateTime? UpdatedAt { get; set; }
    }
}

namespace ERP_infrastructure.repositories
{
    // What a member has on record. Used to decide whether deleting them is safe.
    public class MemberHistoryCounts
    {
        public int Subscriptions { get; init; }
        public int Payments { get; init; }
        public int Sales { get; init; }

        public int Total => Subscriptions + Payments + Sales;
        public bool HasAny => Total > 0;

        public string Describe()
        {
            var parts = new List<string>();

            if (Subscriptions > 0) parts.Add($"{Subscriptions} subscription(s)");
            if (Payments > 0) parts.Add($"{Payments} payment(s)");
            if (Sales > 0) parts.Add($"{Sales} sale(s)");

            return parts.Count == 0 ? "no history" : string.Join(", ", parts);
        }
    }
}

using ERP_domain.entities;

namespace ERP_infrastructure.services
{
    /// <summary>One setting, its current value, and where that value came from.</summary>
    public class TenantSettingView
    {
        public string Key { get; set; } = "";
        public string Category { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Description { get; set; } = "";
        public string Value { get; set; } = "";
        public string DefaultValue { get; set; } = "";
        public string Kind { get; set; } = "";

        /// <summary>False when nobody has changed it and the default is in force.</summary>
        public bool IsOverridden { get; set; }

        public string UpdatedBy { get; set; } = "";
        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>
    /// The settings a tenant administers for itself, under System Administration.
    ///
    /// Only departures from the default are stored, so changing a default in the catalogue
    /// reaches every tenant that never overrode it while a deliberate choice survives. A
    /// missing row is not a missing setting - it is the default.
    /// </summary>
    public interface ITenantSettingsService
    {
        Task<List<TenantSettingView>> GetAllAsync();

        Task<string> GetValueAsync(string key);

        Task<bool> GetBoolAsync(string key, bool fallback = false);

        Task<decimal> GetDecimalAsync(string key, decimal fallback = 0m);

        Task<int> GetIntAsync(string key, int fallback = 0);

        /// <summary>
        /// Stores a value, or removes the override when the value matches the default again.
        /// Validates against the setting's kind, so a number setting cannot be given a word.
        /// </summary>
        Task<TenantSettingView> SetValueAsync(string key, string value);

        /// <summary>Sets several at once, which is what the Settings screen does on save.</summary>
        Task<List<TenantSettingView>> SetManyAsync(IDictionary<string, string> values);

        /// <summary>Puts one setting back to its shipped default.</summary>
        Task<TenantSettingView> ResetAsync(string key);
    }
}

using System.Globalization;
using ERP_domain.entities;
using ERP_infrastructure.data;
using Microsoft.EntityFrameworkCore;

namespace ERP_infrastructure.services
{
    public class TenantSettingsService : ITenantSettingsService
    {
        private readonly TenantErpDbContext _context;
        private readonly ICurrentUserAccessor _actor;

        public TenantSettingsService(TenantErpDbContext context, ICurrentUserAccessor actor)
        {
            _context = context;
            _actor = actor;
        }

        public async Task<List<TenantSettingView>> GetAllAsync()
        {
            var stored = await LoadStoredAsync();

            return TenantSettingKeys.Catalogue
                .Select(definition =>
                {
                    stored.TryGetValue(definition.Key, out var row);

                    return new TenantSettingView
                    {
                        Key = definition.Key,
                        Category = definition.Category,
                        DisplayName = definition.DisplayName,
                        Description = definition.Description,
                        DefaultValue = definition.DefaultValue,
                        Kind = definition.Kind.ToString(),
                        Value = row?.Value ?? definition.DefaultValue,
                        IsOverridden = row is not null,
                        UpdatedBy = row?.UpdatedBy ?? "",
                        UpdatedAt = row?.UpdatedAt ?? row?.CreatedAt
                    };
                })
                .ToList();
        }

        public async Task<string> GetValueAsync(string key)
        {
            var definition = TenantSettingKeys.Find(key);
            if (definition is null) return "";

            var stored = await _context.TenantSettings
                .AsNoTracking()
                .Where(s => s.SettingKey == definition.Key)
                .Select(s => s.Value)
                .FirstOrDefaultAsync();

            return string.IsNullOrWhiteSpace(stored) ? definition.DefaultValue : stored;
        }

        public async Task<bool> GetBoolAsync(string key, bool fallback = false)
        {
            var value = await GetValueAsync(key);
            return bool.TryParse(value, out var parsed) ? parsed : fallback;
        }

        public async Task<decimal> GetDecimalAsync(string key, decimal fallback = 0m)
        {
            var value = await GetValueAsync(key);

            return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallback;
        }

        public async Task<int> GetIntAsync(string key, int fallback = 0)
        {
            var value = await GetValueAsync(key);

            return int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallback;
        }

        public async Task<TenantSettingView> SetValueAsync(string key, string value)
        {
            var results = await SetManyAsync(new Dictionary<string, string> { [key] = value });
            return results[0];
        }

        public async Task<List<TenantSettingView>> SetManyAsync(IDictionary<string, string> values)
        {
            if (values is null || values.Count == 0) return new List<TenantSettingView>();

            var actor = _actor.Current;
            var now = DateTime.UtcNow;
            var touched = new List<string>();

            foreach (var (rawKey, rawValue) in values)
            {
                var definition = TenantSettingKeys.Find(rawKey)
                    ?? throw new ValidationException($"'{rawKey}' is not a FitCore setting.");

                var value = Validate(definition, rawValue);

                var existing = await _context.TenantSettings
                    .FirstOrDefaultAsync(s => s.SettingKey == definition.Key);

                // Setting something back to its default removes the override rather than
                // storing the default twice. Otherwise a later change to the shipped default
                // would not reach a tenant who had only ever confirmed the old one.
                if (string.Equals(value, definition.DefaultValue, StringComparison.Ordinal))
                {
                    if (existing is not null) _context.TenantSettings.Remove(existing);
                }
                else if (existing is null)
                {
                    _context.TenantSettings.Add(new TenantSetting
                    {
                        SettingKey = definition.Key,
                        Value = value,
                        UpdatedByUserId = actor.AppUserId,
                        UpdatedBy = actor.Username ?? "",
                        CreatedAt = now
                    });
                }
                else
                {
                    existing.Value = value;
                    existing.UpdatedByUserId = actor.AppUserId;
                    existing.UpdatedBy = actor.Username ?? "";
                }

                touched.Add(definition.Key);
            }

            await _context.SaveChangesAsync();

            var all = await GetAllAsync();
            return all.Where(s => touched.Contains(s.Key, StringComparer.OrdinalIgnoreCase)).ToList();
        }

        public async Task<TenantSettingView> ResetAsync(string key)
        {
            var definition = TenantSettingKeys.Find(key)
                ?? throw new ValidationException($"'{key}' is not a FitCore setting.");

            return await SetValueAsync(definition.Key, definition.DefaultValue);
        }

        // ------------------------------------------------------------------ helpers

        private async Task<Dictionary<string, TenantSetting>> LoadStoredAsync()
        {
            var rows = await _context.TenantSettings.AsNoTracking().ToListAsync();

            return rows
                .GroupBy(r => r.SettingKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Checks the value against the setting's kind and returns it in canonical form, so
        /// "TRUE", "True" and "true" are all stored the same way and a later read does not
        /// have to be forgiving.
        /// </summary>
        private static string Validate(TenantSettingDefinition definition, string? raw)
        {
            var value = (raw ?? "").Trim();

            switch (definition.Kind)
            {
                case TenantSettingKind.Number:
                    if (!decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
                    {
                        throw new ValidationException($"{definition.DisplayName} must be a number.");
                    }

                    if (number < 0m)
                    {
                        throw new ValidationException($"{definition.DisplayName} cannot be negative.");
                    }

                    return number.ToString(CultureInfo.InvariantCulture);

                case TenantSettingKind.Boolean:
                    if (!bool.TryParse(value, out var flag))
                    {
                        throw new ValidationException($"{definition.DisplayName} must be on or off.");
                    }

                    return flag ? "true" : "false";

                default:
                    if (value.Length > 1000)
                    {
                        throw new ValidationException(
                            $"{definition.DisplayName} is too long. Keep it under 1000 characters.");
                    }

                    return value;
            }
        }
    }
}

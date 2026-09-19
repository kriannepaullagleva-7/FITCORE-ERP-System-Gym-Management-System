using System;

namespace ERP_domain.entities
{
    public class Device
    {
        public Guid DeviceId { get; set; }
        public int CompanyId { get; set; }
        public string DeviceCode { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }

        public Company Company { get; set; } = null!;
    }
}
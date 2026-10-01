using System.ComponentModel.DataAnnotations;
using ERP_infrastructure.services;

namespace ERP_api.DTOs
{
    public class CreateBranchDto
    {
        [Required, MaxLength(20)]
        public string Code { get; set; } = "";

        [Required, MaxLength(150)]
        public string Name { get; set; } = "";

        [MaxLength(250)]
        public string? Address { get; set; }

        [MaxLength(40)]
        public string? Phone { get; set; }

        [MaxLength(150)]
        public string? Email { get; set; }

        public DateTime? OpenedOn { get; set; }
    }

    public class UpdateBranchDto
    {
        [Required, MaxLength(20)]
        public string Code { get; set; } = "";

        [Required, MaxLength(150)]
        public string Name { get; set; } = "";

        [MaxLength(250)]
        public string? Address { get; set; }

        [MaxLength(40)]
        public string? Phone { get; set; }

        [MaxLength(150)]
        public string? Email { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class BranchTransferDto
    {
        public int FromBranchId { get; set; }
        public int ToBranchId { get; set; }

        /// <summary>
        /// Which standing records to move. Transactions are not an option here by design - see
        /// BranchService.TransferAsync for why moving one would falsify both branches' books.
        /// </summary>
        public BranchTransferScope Scope { get; set; } = BranchTransferScope.AllStandingRecords;
    }
}

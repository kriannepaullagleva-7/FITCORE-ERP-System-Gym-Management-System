using ERP_domain.entities;
using ERP_infrastructure.services;

namespace ERP_api.DTOs
{
    // Entities carry navigation properties in both directions, so returning them straight
    // from an endpoint serialises the whole object graph. These projections keep the
    // write endpoints returning the same shape their matching read endpoints do.
    public static class ApiMappings
    {
        public static SaleDto ToDto(this Sale sale) => new()
        {
            SaleId = sale.SaleId,
            MemberId = sale.MemberId,
            WalkInName = sale.WalkInName,
            SaleDate = sale.SaleDate,
            Subtotal = sale.Subtotal,
            Discount = sale.Discount,
            TotalAmount = sale.TotalAmount,
            Status = sale.Status,
            CashierEmployeeId = sale.CashierEmployeeId,
            ProcessedByUserId = sale.ProcessedByUserId,
            ProcessedBy = sale.ProcessedBy,
            Notes = sale.Notes,
            AmountTendered = sale.AmountTendered,
            ChangeGiven = sale.ChangeGiven,
            Items = sale.Items.Select(i => new SaleItemDto
            {
                SaleItemId = i.SaleItemId,
                ProductId = i.ProductId,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice
            }).ToList()
        };

        public static PaymentDto ToDto(this Payment payment) => new()
        {
            PaymentId = payment.PaymentId,
            MemberId = payment.MemberId,
            WalkInName = payment.WalkInName,
            SubscriptionId = payment.SubscriptionId,
            SaleId = payment.SaleId,
            Amount = payment.Amount,
            PaymentDate = payment.PaymentDate,
            Method = payment.Method,
            ReferenceNo = payment.ReferenceNo,
            Status = payment.Status,
            Notes = payment.Notes,
            Category = payment.Category,
            ProcessedByUserId = payment.ProcessedByUserId,
            ProcessedBy = payment.ProcessedBy,
            AmountTendered = payment.AmountTendered,
            ChangeGiven = payment.ChangeGiven
        };

        public static SubscriptionDto ToDto(this Subscription subscription) => new()
        {
            SubscriptionId = subscription.SubscriptionId,
            MemberId = subscription.MemberId,
            WalkInName = subscription.WalkInName,
            WalkInPhone = subscription.WalkInPhone,
            PlanId = subscription.PlanId,
            StartDate = subscription.StartDate,
            EndDate = subscription.EndDate,
            Status = subscription.Status
        };

        public static MemberDto ToDto(this Member member) => new()
        {
            MemberId = member.MemberId,
            FirstName = member.FirstName,
            LastName = member.LastName,
            Phone = member.Phone,
            Email = member.Email,
            JoinDate = member.JoinDate,
            Status = member.Status
        };

        public static MembershipPlanDto ToDto(this MembershipPlan plan) => new()
        {
            PlanId = plan.PlanId,
            PlanName = plan.PlanName,
            DurationMonths = plan.DurationMonths,
            Price = plan.Price,
            Description = plan.Description,
            IsActive = plan.IsActive
        };

        public static ProductDto ToDto(this Product product) => new()
        {
            ProductId = product.ProductId,
            ProductCode = product.ProductCode,
            ProductName = product.ProductName,
            Category = product.Category,
            CostPrice = product.CostPrice,
            UnitPrice = product.UnitPrice,
            IsActive = product.IsActive,
            CreatedAt = product.CreatedAt,
            UpdatedAt = product.UpdatedAt
        };

        public static SupplierDto ToDto(this Supplier supplier) => new()
        {
            SupplierId = supplier.SupplierId,
            SupplierCode = supplier.SupplierCode,
            SupplierName = supplier.SupplierName,
            ContactPerson = supplier.ContactPerson,
            ContactNumber = supplier.ContactNumber,
            EmailAddress = supplier.EmailAddress,
            Address = supplier.Address,
            IsActive = supplier.IsActive,
            CreatedAt = supplier.CreatedAt
        };

        public static EmployeeDto ToDto(this Employee employee) => new()
        {
            EmployeeId = employee.EmployeeId,
            EmployeeCode = employee.EmployeeCode,
            FirstName = employee.FirstName,
            LastName = employee.LastName,
            FullName = $"{employee.FirstName} {employee.LastName}".Trim(),
            Position = employee.Position,
            Department = employee.Department,
            Phone = employee.Phone,
            Email = employee.Email,
            HireDate = employee.HireDate,
            BasicSalary = employee.BasicSalary,
            HourlyRate = employee.HourlyRate,
            Status = employee.Status
        };

        public static EmployeeAccountResultDto ToDto(this EmployeeAccountResult result) => new()
        {
            Outcome = result.Outcome.ToString(),
            Message = result.Message,
            AccountUsable = result.AccountUsable
        };

        public static AuditEventDto ToDto(this AuditEventView view) => new()
        {
            AuditEventId = view.AuditEventId,
            OccurredAt = view.OccurredAt,
            Username = view.Username,
            RoleKey = view.RoleKey,
            Action = view.Action,
            Module = view.Module,
            EntityName = view.EntityName,
            EntityId = view.EntityId,
            OldValues = view.OldValues,
            NewValues = view.NewValues,
            Summary = view.Summary,
            IpAddress = view.IpAddress
        };

        public static AttendanceDto ToDto(this AttendanceView view) => new()
        {
            AttendanceId = view.AttendanceId,
            EmployeeId = view.EmployeeId,
            EmployeeCode = view.EmployeeCode,
            EmployeeName = view.EmployeeName,
            Position = view.Position,
            Date = view.Date,
            TimeIn = view.TimeIn,
            TimeOut = view.TimeOut,
            RegularHours = view.RegularHours,
            OvertimeHours = view.OvertimeHours,
            Status = view.Status,
            Notes = view.Notes,
            RecordedByUserId = view.RecordedByUserId,
            RecordedBy = view.RecordedBy,
            ModifiedByUserId = view.ModifiedByUserId,
            ModifiedBy = view.ModifiedBy
        };
    }
}

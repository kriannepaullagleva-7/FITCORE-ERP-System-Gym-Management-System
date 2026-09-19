using ERP_domain.entities;

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
            SaleDate = sale.SaleDate,
            Subtotal = sale.Subtotal,
            Discount = sale.Discount,
            TotalAmount = sale.TotalAmount,
            Status = sale.Status,
            CashierEmployeeId = sale.CashierEmployeeId,
            Notes = sale.Notes,
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
            SubscriptionId = payment.SubscriptionId,
            SaleId = payment.SaleId,
            Amount = payment.Amount,
            PaymentDate = payment.PaymentDate,
            Method = payment.Method,
            ReferenceNo = payment.ReferenceNo,
            Status = payment.Status,
            Notes = payment.Notes
        };

        public static SubscriptionDto ToDto(this Subscription subscription) => new()
        {
            SubscriptionId = subscription.SubscriptionId,
            MemberId = subscription.MemberId,
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

        public static CustomerDto ToDto(this Customer customer) => new()
        {
            CustomerId = customer.CustomerId,
            CustomerCode = customer.CustomerCode,
            CustomerName = customer.CustomerName,
            ContactNumber = customer.ContactNumber,
            EmailAddress = customer.EmailAddress,
            Address = customer.Address,
            IsActive = customer.IsActive,
            CreatedAt = customer.CreatedAt
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
            Status = employee.Status
        };
    }
}

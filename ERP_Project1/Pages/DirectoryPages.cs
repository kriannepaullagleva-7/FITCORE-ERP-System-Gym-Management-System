using ERP_Project1.Api;

namespace ERP_Project1
{
    // -------------------------------------------------------------------------- Customers

    internal sealed class CustomersPage : CrudPageBase<CustomerDto>
    {
        public CustomersPage(FitCoreSession session)
            : base(session, "Customers", "Walk-in and account customers, separate from gym members.",
                   "customer", "Name, code, phone or email")
        { }

        protected override string DeleteConsequence =>
            "The customer record is removed. Any sales already recorded are not affected. " +
            "This cannot be undone.";

        protected override string EmptyHeadline => "No customers yet";
        protected override string EmptyDetail =>
            "Customers are for people who buy from the counter without being gym members. " +
            "Members themselves live under Membership.";

        protected override async Task<List<CustomerDto>?> FetchAsync() =>
            Unwrap(await Session.Customers.GetAllAsync());

        protected override void DefineColumns()
        {
            Column(nameof(CustomerDto.CustomerCode), "Code", 70);
            Column(nameof(CustomerDto.CustomerName), "Name", 170);
            Column(nameof(CustomerDto.ContactNumber), "Phone", 90);
            Column(nameof(CustomerDto.EmailAddress), "Email", 140);
            Column(nameof(CustomerDto.Address), "Address", 150);
            FlagColumn(nameof(CustomerDto.IsActive), "Status", 70);
        }

        protected override bool Matches(CustomerDto c, string term) =>
            c.CustomerName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            c.CustomerCode.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (c.ContactNumber ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (c.EmailAddress ?? "").Contains(term, StringComparison.OrdinalIgnoreCase);

        protected override string DescribeForDelete(CustomerDto c) =>
            $"“{c.CustomerName}” ({c.CustomerCode})";

        private static List<FieldSpec> Fields(CustomerDto? c) => new()
        {
            new("code", "Customer code")
                { Required = true, Value = c?.CustomerCode, MaxLength = 50, Hint = "Must be unique." },
            new("name", "Customer name") { Required = true, Value = c?.CustomerName, MaxLength = 150 },
            new("phone", "Contact number") { Value = c?.ContactNumber, MaxLength = 30 },
            new("email", "Email address", FieldKind.Email) { Value = c?.EmailAddress, MaxLength = 150 },
            new("address", "Address", FieldKind.Multiline) { Value = c?.Address, MaxLength = 250 }
        };

        protected override Task<bool> OnAddAsync()
        {
            var saved = EditDialog.Run(this, "Add customer",
                "A customer code identifies them on receipts and must be unique.",
                Fields(null), async f =>
                {
                    var result = await Session.Customers.CreateAsync(new CreateCustomerDto
                    {
                        CustomerCode = f.First(x => x.Key == "code").Text,
                        CustomerName = f.First(x => x.Key == "name").Text,
                        ContactNumber = f.First(x => x.Key == "phone").Text,
                        EmailAddress = f.First(x => x.Key == "email").Text,
                        Address = f.First(x => x.Key == "address").Text
                    });
                    return result.IsSuccess ? null : result.ErrorMessage;
                }, "Create customer");

            return Task.FromResult(saved);
        }

        protected override Task<bool> OnEditAsync(CustomerDto c)
        {
            var fields = Fields(c);
            fields.Add(new FieldSpec("active", "Active", FieldKind.Check) { Value = c.IsActive });

            var saved = EditDialog.Run(this, $"Edit {c.CustomerName}", "", fields, async f =>
            {
                var result = await Session.Customers.UpdateAsync(c.CustomerId, new UpdateCustomerDto
                {
                    CustomerCode = f.First(x => x.Key == "code").Text,
                    CustomerName = f.First(x => x.Key == "name").Text,
                    ContactNumber = f.First(x => x.Key == "phone").Text,
                    EmailAddress = f.First(x => x.Key == "email").Text,
                    Address = f.First(x => x.Key == "address").Text,
                    IsActive = f.First(x => x.Key == "active").Flag
                });
                return result.IsSuccess ? null : result.ErrorMessage;
            }, "Save changes");

            return Task.FromResult(saved);
        }

        protected override async Task<string?> OnDeleteAsync(CustomerDto c)
        {
            var result = await Session.Customers.DeleteAsync(c.CustomerId);
            return result.IsSuccess ? null : result.ErrorMessage;
        }
    }

    // -------------------------------------------------------------------------- Suppliers

    internal sealed class SuppliersPage : CrudPageBase<SupplierDto>
    {
        public SuppliersPage(FitCoreSession session)
            : base(session, "Suppliers", "Who the gym buys stock from, and how to reach them.",
                   "supplier", "Name, code, contact or email")
        { }

        protected override string DeleteConsequence =>
            "The supplier record is removed. Stock movements that reference them are not " +
            "affected. This cannot be undone.";

        protected override string EmptyHeadline => "No suppliers yet";
        protected override string EmptyDetail =>
            "Record who you buy drinks, supplements and gear from so a stock-in can reference them.";

        protected override async Task<List<SupplierDto>?> FetchAsync() =>
            Unwrap(await Session.Suppliers.GetAllAsync());

        protected override void DefineColumns()
        {
            Column(nameof(SupplierDto.SupplierCode), "Code", 70);
            Column(nameof(SupplierDto.SupplierName), "Supplier", 160);
            Column(nameof(SupplierDto.ContactPerson), "Contact", 110);
            Column(nameof(SupplierDto.ContactNumber), "Phone", 90);
            Column(nameof(SupplierDto.EmailAddress), "Email", 130);
            FlagColumn(nameof(SupplierDto.IsActive), "Status", 70);
        }

        protected override bool Matches(SupplierDto s, string term) =>
            s.SupplierName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            s.SupplierCode.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (s.ContactPerson ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
            (s.EmailAddress ?? "").Contains(term, StringComparison.OrdinalIgnoreCase);

        protected override string DescribeForDelete(SupplierDto s) =>
            $"“{s.SupplierName}” ({s.SupplierCode})";

        private static List<FieldSpec> Fields(SupplierDto? s) => new()
        {
            new("code", "Supplier code")
                { Required = true, Value = s?.SupplierCode, MaxLength = 50, Hint = "Must be unique." },
            new("name", "Supplier name") { Required = true, Value = s?.SupplierName, MaxLength = 150 },
            new("person", "Contact person") { Value = s?.ContactPerson, MaxLength = 100 },
            new("phone", "Contact number") { Value = s?.ContactNumber, MaxLength = 30 },
            new("email", "Email address", FieldKind.Email) { Value = s?.EmailAddress, MaxLength = 150 },
            new("address", "Address", FieldKind.Multiline) { Value = s?.Address, MaxLength = 250 }
        };

        protected override Task<bool> OnAddAsync()
        {
            var saved = EditDialog.Run(this, "Add supplier", "", Fields(null), async f =>
            {
                var result = await Session.Suppliers.CreateAsync(new CreateSupplierDto
                {
                    SupplierCode = f.First(x => x.Key == "code").Text,
                    SupplierName = f.First(x => x.Key == "name").Text,
                    ContactPerson = f.First(x => x.Key == "person").Text,
                    ContactNumber = f.First(x => x.Key == "phone").Text,
                    EmailAddress = f.First(x => x.Key == "email").Text,
                    Address = f.First(x => x.Key == "address").Text
                });
                return result.IsSuccess ? null : result.ErrorMessage;
            }, "Create supplier");

            return Task.FromResult(saved);
        }

        protected override Task<bool> OnEditAsync(SupplierDto s)
        {
            var fields = Fields(s);
            fields.Add(new FieldSpec("active", "Active", FieldKind.Check) { Value = s.IsActive });

            var saved = EditDialog.Run(this, $"Edit {s.SupplierName}", "", fields, async f =>
            {
                var result = await Session.Suppliers.UpdateAsync(s.SupplierId, new UpdateSupplierDto
                {
                    SupplierCode = f.First(x => x.Key == "code").Text,
                    SupplierName = f.First(x => x.Key == "name").Text,
                    ContactPerson = f.First(x => x.Key == "person").Text,
                    ContactNumber = f.First(x => x.Key == "phone").Text,
                    EmailAddress = f.First(x => x.Key == "email").Text,
                    Address = f.First(x => x.Key == "address").Text,
                    IsActive = f.First(x => x.Key == "active").Flag
                });
                return result.IsSuccess ? null : result.ErrorMessage;
            }, "Save changes");

            return Task.FromResult(saved);
        }

        protected override async Task<string?> OnDeleteAsync(SupplierDto s)
        {
            var result = await Session.Suppliers.DeleteAsync(s.SupplierId);
            return result.IsSuccess ? null : result.ErrorMessage;
        }
    }
}

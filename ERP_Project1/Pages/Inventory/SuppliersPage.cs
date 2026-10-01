using ERP_Project1.Api;

namespace ERP_Project1
{
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
            new("phone", "Contact number", FieldKind.Phone) { Value = s?.ContactNumber, MaxLength = 30 },
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

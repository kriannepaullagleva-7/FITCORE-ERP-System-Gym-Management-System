using ERP_Project1.Api;

namespace ERP_Project1
{
    /// <summary>One priced line on a receipt. Quantity is optional - payroll has none.</summary>
    internal sealed class ReceiptLine
    {
        public string Description { get; init; } = "";
        public decimal? Quantity { get; init; }
        public decimal? UnitPrice { get; init; }
        public decimal Amount { get; init; }
    }

    /// <summary>A labelled figure in the totals block, e.g. "Discount" / 50.00.</summary>
    internal sealed record ReceiptTotal(string Label, decimal Value, bool Emphasise = false);

    /// <summary>A label/value pair in the header grid, e.g. "Cashier" / "Kris Santos".</summary>
    internal sealed record ReceiptFact(string Label, string Value);

    /// <summary>
    /// Everything a FitCore receipt shows, whatever kind of transaction produced it.
    ///
    /// One shape for all four - membership, sales, payment and payroll - is what makes the
    /// receipts look like one system. Each builder below fills it from the DTOs that screen
    /// already holds, so no new server call is needed to print what the operator is looking at.
    /// </summary>
    internal sealed class ReceiptModel
    {
        public string Title { get; init; } = "RECEIPT";

        /// <summary>The human reference, e.g. "SALE-25" or "PAY-108".</summary>
        public string DocumentNo { get; init; } = "";

        public DateTime IssuedAt { get; init; } = DateTime.Now;

        /// <summary>Membership, Sales, Payment or Payroll - drives the accent colour.</summary>
        public string TransactionType { get; init; } = "";

        public string Status { get; init; } = "";

        public string CompanyName { get; init; } = "";
        public string TierName { get; init; } = "";

        /// <summary>"Member", "Employee" or "Customer" - who the document is addressed to.</summary>
        public string PartyLabel { get; init; } = "Member";
        public string PartyName { get; init; } = "";

        public List<ReceiptFact> Facts { get; init; } = new();
        public List<ReceiptLine> Lines { get; init; } = new();
        public List<ReceiptTotal> Totals { get; init; } = new();

        /// <summary>
        /// What was owed, what was handed over, and the change given back - first-class rather
        /// than buried in <see cref="Totals"/>, so every receipt type positions and formats them
        /// identically. Null where the transaction carries no cash-tender concept (a payslip).
        /// </summary>
        public decimal? AmountDue { get; init; }
        public decimal? AmountPaid { get; init; }
        public decimal? ChangeGiven { get; init; }

        public string FooterNote { get; init; } = "";
    }

    /// <summary>
    /// Turns what a screen already has into a receipt.
    ///
    /// Deliberately client-side: every figure here has just come from the API on the same
    /// screen, so rebuilding it on the server would mean a second round trip and a second
    /// copy of the same arithmetic. What the server owns - the totals, the cashier, the
    /// status - is carried through unchanged; nothing is recomputed here.
    /// </summary>
    internal static class ReceiptBuilder
    {
        private static string Company(FitCoreSession session) =>
            session.CurrentUser?.CompanyName ?? "FitCore Gym";

        /// <summary>
        /// The tier line, unless the company name already ends with it - several tenants are
        /// named "FitCore Gym - Micro Enterprise", and printing it twice reads as a bug.
        /// </summary>
        private static string Tier(FitCoreSession session)
        {
            var tier = session.CurrentUser?.EnterpriseTier;
            if (string.IsNullOrWhiteSpace(tier)) return "";

            var line = tier + " Enterprise";

            return Company(session).Contains(line, StringComparison.OrdinalIgnoreCase)
                ? ""
                : line;
        }

        private static string OrDash(string? value) =>
            string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

        // ------------------------------------------------------------------ sales

        public static ReceiptModel ForSale(
            FitCoreSession session, SaleViewDto sale, IReadOnlyList<SaleLineDto> lines,
            IReadOnlyList<PaymentViewDto> payments)
        {
            var paid = payments.Where(p => p.Status == "Completed").Sum(p => p.Amount);
            var method = payments.Count > 0 ? payments[^1].Method : "";

            return new ReceiptModel
            {
                Title = "SALES RECEIPT",
                DocumentNo = $"SALE-{sale.SaleId}",
                IssuedAt = sale.SaleDate,
                TransactionType = "Sales",
                Status = sale.Status,
                CompanyName = Company(session),
                TierName = Tier(session),
                PartyLabel = "Member",
                PartyName = OrDash(sale.MemberName),

                Facts =
                {
                    new("Cashier", OrDash(string.IsNullOrWhiteSpace(sale.CashierName)
                        ? sale.ProcessedBy : sale.CashierName)),
                    new("Payment method", OrDash(method)),
                    new("Payment status", OrDash(sale.PaymentStatus)),
                    new("Items", $"{sale.ItemCount} line(s), {sale.TotalQuantity} unit(s)")
                },

                Lines = lines.Select(l => new ReceiptLine
                {
                    Description = $"{l.ProductName} ({l.ProductCode})",
                    Quantity = l.Quantity,
                    UnitPrice = l.UnitPrice,
                    Amount = l.Subtotal
                }).ToList(),

                Totals =
                {
                    new("Subtotal", sale.Subtotal),
                    new("Discount", sale.Discount),
                    new("Total", sale.TotalAmount, Emphasise: true),
                    new("Paid", paid),
                    new("Balance", sale.Balance)
                },

                AmountDue = sale.TotalAmount,
                AmountPaid = sale.AmountTendered ?? (paid > 0 ? paid : null),
                ChangeGiven = sale.ChangeGiven,

                FooterNote = sale.Balance > 0
                    ? "This sale is not fully settled. The balance is payable at the counter."
                    : "Thank you for your purchase."
            };
        }

        // ------------------------------------------------------------------ payment

        public static ReceiptModel ForPayment(FitCoreSession session, PaymentViewDto payment)
        {
            var isSale = payment.Category == "Sales";

            return new ReceiptModel
            {
                Title = "PAYMENT RECEIPT",
                DocumentNo = $"PAY-{payment.PaymentId}",
                IssuedAt = payment.PaymentDate,
                TransactionType = payment.Category,
                Status = payment.Status,
                CompanyName = Company(session),
                TierName = Tier(session),

                // The member is always named on the document even for a counter sale, where
                // the grid shows "Others" - a receipt is the one place the real party belongs.
                PartyLabel = "Received from",
                PartyName = OrDash(payment.MemberName),

                Facts =
                {
                    new("Category", OrDash(payment.Category)),
                    new("Applies to", OrDash(payment.AppliesTo)),
                    new("Payment method", OrDash(payment.Method)),
                    new("Reference", OrDash(payment.ReferenceNo)),
                    new("Received by", OrDash(payment.ProcessedBy))
                },

                Lines =
                {
                    new ReceiptLine
                    {
                        Description = isSale
                            ? $"Settlement of sale #{payment.SaleId}"
                            : payment.SubscriptionId.HasValue
                                ? $"Membership dues — {OrDash(payment.PlanName)}"
                                : "General payment",
                        Amount = payment.Amount
                    }
                },

                Totals = { new("Amount received", payment.Amount, Emphasise: true) },

                AmountDue = payment.Amount,
                AmountPaid = payment.AmountTendered ?? payment.Amount,
                ChangeGiven = payment.ChangeGiven,

                FooterNote = string.IsNullOrWhiteSpace(payment.Notes)
                    ? "Thank you. Please retain this receipt."
                    : payment.Notes
            };
        }

        // ------------------------------------------------------------------ membership

        public static ReceiptModel ForSubscription(
            FitCoreSession session,
            string memberName,
            MembershipPlanDto plan,
            SubscriptionDto subscription,
            PaymentViewDto? payment,
            string action)
        {
            var model = new ReceiptModel
            {
                Title = "MEMBERSHIP RECEIPT",
                DocumentNo = $"SUB-{subscription.SubscriptionId}",
                IssuedAt = payment?.PaymentDate ?? DateTime.Now,
                TransactionType = "Membership",
                Status = payment is null ? "Unpaid" : payment.Status,
                CompanyName = Company(session),
                TierName = Tier(session),
                PartyLabel = "Member",
                PartyName = OrDash(memberName),

                AmountDue = plan.Price,
                AmountPaid = payment?.AmountTendered ?? payment?.Amount,
                ChangeGiven = payment?.ChangeGiven,

                Facts =
                {
                    new("Transaction", action),
                    new("Plan", OrDash(plan.PlanName)),
                    new("Term", $"{plan.DurationMonths} month(s)"),
                    new("Cover", $"{subscription.StartDate:d MMM yyyy} – {subscription.EndDate:d MMM yyyy}"),
                    new("Payment method", OrDash(payment?.Method)),
                    new("Received by", OrDash(payment?.ProcessedBy))
                },

                Lines =
                {
                    new ReceiptLine
                    {
                        Description = $"{plan.PlanName} — {plan.DurationMonths} month(s)",
                        Quantity = 1,
                        UnitPrice = plan.Price,
                        Amount = plan.Price
                    }
                },

                FooterNote = payment is null
                    ? "This membership has not been paid for and is not yet active."
                    : $"Membership runs to {subscription.EndDate:d MMMM yyyy}."
            };

            model.Totals.Add(new ReceiptTotal("Plan price", plan.Price));
            model.Totals.Add(new ReceiptTotal("Paid", payment?.Amount ?? 0m, Emphasise: true));
            model.Totals.Add(new ReceiptTotal("Balance", plan.Price - (payment?.Amount ?? 0m)));

            return model;
        }

        /// <summary>
        /// A membership receipt reprinted from the overview row, for a subscription sold
        /// earlier. Everything it needs is already on that row, so reopening an old receipt
        /// costs no extra server call.
        /// </summary>
        public static ReceiptModel ForMembership(FitCoreSession session, MembershipOverviewDto row)
        {
            var paid = row.AmountPaid;
            var price = row.PlanPrice;

            return new ReceiptModel
            {
                Title = "MEMBERSHIP RECEIPT",
                DocumentNo = row.SubscriptionId is null ? "SUB-—" : $"SUB-{row.SubscriptionId}",
                IssuedAt = row.StartDate ?? DateTime.Now,
                TransactionType = "Membership",
                Status = OrDash(row.PaymentStatus),
                CompanyName = Company(session),
                TierName = Tier(session),
                PartyLabel = "Member",
                PartyName = OrDash(row.FullName),

                Facts =
                {
                    new("Plan", OrDash(row.PlanName)),
                    new("Cover", row.StartDate is null || row.ExpiryDate is null
                        ? "—"
                        : $"{row.StartDate:d MMM yyyy} – {row.ExpiryDate:d MMM yyyy}"),
                    new("Days remaining", row.DaysRemaining?.ToString() ?? "—"),
                    new("Membership status", OrDash(row.MembershipStatus))
                },

                Lines =
                {
                    new ReceiptLine
                    {
                        Description = $"{OrDash(row.PlanName)} membership",
                        Quantity = 1,
                        UnitPrice = price,
                        Amount = price
                    }
                },

                Totals =
                {
                    new("Plan price", price),
                    new("Paid", paid, Emphasise: true),
                    new("Balance", row.Balance)
                },

                AmountDue = price,
                AmountPaid = paid > 0 ? paid : null,

                FooterNote = row.Balance > 0
                    ? "This membership is not fully paid. The balance is due at reception."
                    : $"Membership runs to {row.ExpiryDate:d MMMM yyyy}."
            };
        }

        // ------------------------------------------------------------------ payroll

        public static ReceiptModel ForPayroll(FitCoreSession session, PayrollDto run)
        {
            var model = new ReceiptModel
            {
                Title = "PAYSLIP",
                DocumentNo = $"PR-{run.PayrollId}",
                IssuedAt = run.PaidDate ?? run.PeriodEnd,
                TransactionType = "Payroll",
                Status = run.Status,
                CompanyName = Company(session),
                TierName = Tier(session),
                PartyLabel = "Employee",
                PartyName = $"{OrDash(run.EmployeeName)} ({OrDash(run.EmployeeCode)})",

                Facts =
                {
                    new("Position", OrDash(run.Position)),
                    new("Pay period", $"{run.PeriodStart:d MMM yyyy} – {run.PeriodEnd:d MMM yyyy}"),
                    new("Paid on", run.PaidDate is null ? "—" : $"{run.PaidDate:d MMM yyyy}"),
                    new("Generated by", OrDash(run.ProcessedBy)),
                    new("Last modified by", OrDash(run.LastModifiedBy))
                },

                FooterNote = string.IsNullOrWhiteSpace(run.Notes)
                    ? "Computer-generated payslip. No signature required."
                    : run.Notes
            };

            // Attendance-generated runs show the hours and rate behind the pay; a manual run
            // (RegularHours is zero) shows the flat basic salary instead - the two inputs a
            // run can actually be built from, per PayrollService.
            if (run.RegularHours > 0 || run.HourlyRate > 0)
            {
                model.Lines.Add(new ReceiptLine
                {
                    Description = $"Regular pay ({run.RegularHours:0.##} h @ {run.HourlyRate:N2})",
                    Amount = run.RegularPay
                });
            }
            else
            {
                model.Lines.Add(new ReceiptLine { Description = "Basic salary", Amount = run.BasicSalary });
            }

            model.Lines.Add(new ReceiptLine { Description = "Allowances", Amount = run.Allowances });
            model.Lines.Add(new ReceiptLine
            {
                Description = $"Overtime ({run.OvertimeHours:0.##} h @ {run.OvertimeRate:N2})",
                Amount = run.OvertimePay
            });

            model.Totals.Add(new ReceiptTotal("Gross pay", run.GrossPay, Emphasise: true));

            // Each statutory line only when a real amount is behind it, so a manually-entered
            // run - which has no SSS/PhilHealth/Pag-IBIG/tax breakdown - does not show four
            // zero rows above its one real deduction figure.
            if (run.SssDeduction > 0) model.Totals.Add(new ReceiptTotal("SSS", -run.SssDeduction));
            if (run.PhilHealthDeduction > 0) model.Totals.Add(new ReceiptTotal("PhilHealth", -run.PhilHealthDeduction));
            if (run.PagIbigDeduction > 0) model.Totals.Add(new ReceiptTotal("Pag-IBIG", -run.PagIbigDeduction));
            if (run.WithholdingTax > 0) model.Totals.Add(new ReceiptTotal("Withholding tax", -run.WithholdingTax));

            var otherDeductions = run.SssDeduction + run.PhilHealthDeduction +
                run.PagIbigDeduction + run.WithholdingTax > 0
                ? run.OtherDeductions
                : run.Deductions;

            if (otherDeductions > 0) model.Totals.Add(new ReceiptTotal("Other deductions", -otherDeductions));

            model.Totals.Add(new ReceiptTotal("Net pay", run.NetPay, Emphasise: true));

            // The employer's own contributions are stated as a fact rather than a total.
            // They are not deducted from this employee and must never look as though they
            // were - they are what the gym pays on top, and an employee reading their payslip
            // should be able to see the figure without being confused about whose money it is.
            if (run.EmployerContributions > 0m)
            {
                model.Facts.Add(new("Employer contributions",
                    $"{run.EmployerContributions:N2} (paid by the company, not deducted)"));

                model.Facts.Add(new("Total cost to employer",
                    $"{run.GrossPay + run.EmployerContributions:N2}"));
            }

            return model;
        }
    }
}

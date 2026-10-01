namespace ERP_infrastructure.services
{
    /// <summary>An attendance row flattened with the employee name, for the Attendance screen.</summary>
    public class AttendanceView
    {
        public int AttendanceId { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeCode { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public string Position { get; set; } = "";

        public DateTime Date { get; set; }
        public DateTime? TimeIn { get; set; }
        public DateTime? TimeOut { get; set; }
        public decimal RegularHours { get; set; }
        public decimal OvertimeHours { get; set; }
        public string Status { get; set; } = "Present";
        public string Notes { get; set; } = "";

        public int? RecordedByUserId { get; set; }
        public string RecordedBy { get; set; } = "";
        public int? ModifiedByUserId { get; set; }
        public string ModifiedBy { get; set; } = "";
    }

    /// <summary>Totals for one employee over one period, what Payroll Calculation reads.</summary>
    public class AttendancePeriodSummary
    {
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; } = "";
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public int DaysPresent { get; set; }
        public int DaysAbsent { get; set; }
        public int DaysLate { get; set; }
        public int DaysOnLeave { get; set; }
        public decimal TotalRegularHours { get; set; }
        public decimal TotalOvertimeHours { get; set; }
    }
}

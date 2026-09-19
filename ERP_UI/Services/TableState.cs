namespace ERP_UI.Services
{
    /// <summary>
    /// The parts of a table's sort/page state the shared UI controls need, without the row
    /// type. Keeping this non-generic lets Pagination and SortableHeader be plain components
    /// rather than generic ones that every call site has to type-annotate.
    /// </summary>
    public interface ITableState
    {
        string? SortColumn { get; }
        bool Descending { get; }
        int Page { get; set; }
        int PageSize { get; set; }
        int TotalCount { get; }
        int TotalPages { get; }
        int FirstRowOnPage { get; }
        int LastRowOnPage { get; }

        void ToggleSort(string column);
        void GoToPage(int page);
    }

    /// <summary>
    /// Sorting and paging for a table that has already been fetched.
    ///
    /// The lists these pages show are one row per member, product or sale - hundreds at most
    /// for a single gym - so they are fetched once and sorted and paged here. Anything that
    /// genuinely grows without bound (sales, payments, expenses, stock movements) is narrowed
    /// by date on the server first, so this is never asked to hold an unbounded result set.
    /// </summary>
    public class TableState<T> : ITableState
    {
        private readonly Dictionary<string, Func<T, object?>> _columns = new(StringComparer.OrdinalIgnoreCase);

        public string? SortColumn { get; private set; }
        public bool Descending { get; private set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public int TotalCount { get; private set; }

        public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);

        public int FirstRowOnPage => TotalCount == 0 ? 0 : ((Page - 1) * PageSize) + 1;

        public int LastRowOnPage => Math.Min(Page * PageSize, TotalCount);

        public TableState(string? defaultSort = null, bool descending = false, int pageSize = 10)
        {
            SortColumn = defaultSort;
            Descending = descending;
            PageSize = pageSize;
        }

        /// <summary>
        /// Registers how a named column is sorted. A column with no selector registered is
        /// simply not sortable, which is the right outcome for an actions column.
        /// </summary>
        public TableState<T> Column(string name, Func<T, object?> selector)
        {
            _columns[name] = selector;
            return this;
        }

        public bool IsSortable(string column) => _columns.ContainsKey(column);

        public void ToggleSort(string column)
        {
            if (!_columns.ContainsKey(column)) return;

            if (string.Equals(SortColumn, column, StringComparison.OrdinalIgnoreCase))
            {
                Descending = !Descending;
            }
            else
            {
                SortColumn = column;
                Descending = false;
            }

            // A re-sort invalidates where you were in the list, so go back to the first page.
            Page = 1;
        }

        public void GoToPage(int page)
        {
            Page = Math.Clamp(page, 1, TotalPages);
        }

        /// <summary>
        /// Sorts and pages the rows. Also records the total and clamps the current page, so a
        /// filter that shrinks the list cannot strand the user on a page that no longer exists.
        /// </summary>
        public List<T> Apply(IEnumerable<T> source)
        {
            var rows = source as IList<T> ?? source.ToList();

            TotalCount = rows.Count;

            if (Page > TotalPages) Page = TotalPages;
            if (Page < 1) Page = 1;

            IEnumerable<T> query = rows;

            if (SortColumn is not null && _columns.TryGetValue(SortColumn, out var selector))
            {
                query = Descending
                    ? query.OrderByDescending(selector)
                    : query.OrderBy(selector);
            }

            return query
                .Skip((Page - 1) * PageSize)
                .Take(PageSize)
                .ToList();
        }
    }
}

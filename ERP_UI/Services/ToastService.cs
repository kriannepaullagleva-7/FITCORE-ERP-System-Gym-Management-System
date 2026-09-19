namespace ERP_UI.Services
{
    public enum ToastTone
    {
        Info,
        Success,
        Warning,
        Error
    }

    public class Toast
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Title { get; init; } = "";
        public string Message { get; init; } = "";
        public ToastTone Tone { get; init; } = ToastTone.Info;
    }

    /// <summary>
    /// Transient confirmations and failures, shown in the corner of the shell.
    ///
    /// Pages raise a toast after a successful write so the user gets an acknowledgement
    /// without a dialog to dismiss. Failures that the user needs to act on stay on the page
    /// as an inline alert instead; this is only for things that can safely scroll away.
    /// </summary>
    public class ToastService
    {
        private readonly List<Toast> _toasts = new();

        public IReadOnlyList<Toast> Toasts => _toasts;

        /// <summary>Raised whenever the list changes, so the host can re-render.</summary>
        public event Action? OnChange;

        public void Success(string message, string title = "Saved") => Show(message, title, ToastTone.Success);

        public void Error(string message, string title = "Something went wrong") =>
            Show(message, title, ToastTone.Error);

        public void Warning(string message, string title = "Check this") =>
            Show(message, title, ToastTone.Warning);

        public void Info(string message, string title = "") => Show(message, title, ToastTone.Info);

        public void Show(string message, string title, ToastTone tone)
        {
            var toast = new Toast { Message = message, Title = title, Tone = tone };
            _toasts.Add(toast);
            OnChange?.Invoke();

            // Errors stay until dismissed; everything else clears itself.
            if (tone != ToastTone.Error)
            {
                _ = DismissLaterAsync(toast);
            }
        }

        public void Dismiss(Guid id)
        {
            var found = _toasts.FirstOrDefault(t => t.Id == id);
            if (found is null) return;

            _toasts.Remove(found);
            OnChange?.Invoke();
        }

        private async Task DismissLaterAsync(Toast toast)
        {
            await Task.Delay(TimeSpan.FromSeconds(4));
            Dismiss(toast.Id);
        }
    }
}

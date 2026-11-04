namespace MuslimMatrimony.Models
{
    /// <summary>
    /// A simple model used to pass error information to the Error view.
    /// </summary>
    public class ErrorViewModel
    {
        /// <summary>
        /// Gets or sets the unique identifier for the current request.
        /// This is useful for tracking and debugging purposes.
        /// </summary>
        public string? RequestId { get; set; }

        /// <summary>
        /// A convenience property that determines whether the RequestId should be displayed.
        /// The RequestId is only shown if it is not null or empty.
        /// </summary>
        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
    }
}

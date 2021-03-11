namespace Quartz
{
    public class JobFactoryOptions
    {
        /// <summary>
        /// Whether to use scopes when building job instances, enables injection of scoped services like
        /// Entity Framework's DbContext.
        /// </summary>
        public bool CreateScope { get; set; }
    }
}
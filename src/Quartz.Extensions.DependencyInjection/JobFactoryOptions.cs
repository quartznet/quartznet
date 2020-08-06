namespace Quartz
{
    public class JobFactoryOptions
    {
        /// <summary>
        /// When DI has not configured with job, can we call default constructor if it's present.
        /// </summary>
        public bool AllowDefaultConstructor { get; set; }
    }
}
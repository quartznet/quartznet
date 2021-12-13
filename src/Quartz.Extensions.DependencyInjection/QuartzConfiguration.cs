using Microsoft.Extensions.Options;

namespace Quartz
{
    /// <summary>
    /// This class is responsible for ensuring that configuration is valid.
    /// </summary>
    internal sealed class QuartzConfiguration : IPostConfigureOptions<QuartzOptions>
    {
        public void PostConfigure(string name, QuartzOptions options)
        {
        }
    }
}
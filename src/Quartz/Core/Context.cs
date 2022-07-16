namespace Quartz.Core
{
    internal static class Context
    {
        public static readonly AsyncLocal<Guid?> CallerId = new AsyncLocal<Guid?>();
    }
}
namespace Quartz.Tests.Integration
{
    public static class TestConstants
    {
        // we cannot use trusted connection as it's not available for Linux provider
        public static string DefaultSqlServerConnectionString = "Server=(local);Database=quartz;User Id=quartznet;Password=quartznet;";

#if !BINARY_SERIALIZATION
        public const string DefaultSerializerType = "json";
#else
        public const string DefaultSerializerType = "binary";
#endif

#if NETSTANDARD_DBPROVIDERS
        public const string DefaultSqlServerProvider = "SqlServer-41";
#else
        public const string DefaultSqlServerProvider = "SqlServer-20";
#endif
    }
}
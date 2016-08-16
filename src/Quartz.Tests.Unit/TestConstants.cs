namespace Quartz.Tests.Unit
{
    public static class TestConstants
    {
#if NETCORE
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
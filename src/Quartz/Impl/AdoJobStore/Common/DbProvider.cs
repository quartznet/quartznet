using System;
using System.Collections;
using System.Collections.Specialized;
using System.Data;
using System.Text;

using Quartz.Util;

namespace Quartz.Impl.AdoJobStore.Common
{
    /// <summary>
    ///     
    /// </summary>
    public class DbProvider : IDbProvider
    {
        private const string QUARTZ_PROPERTY_GROUP_DB_PROVIDER = "quartz.dbprovider";
        private const string QUARTZ_DB_PROVIDER_RESOURCE_NAME = "Quartz.Impl.AdoJobStore.Common.dbproviders.properties";

        private string connectionString;
        private DbMetadata dbMetadata;
        private static readonly Hashtable dbMetadataLookup = new Hashtable();

        static DbProvider()
        {
            // parse metadata
            PropertiesParser pp = PropertiesParser.ReadFromEmbeddedAssemblyResource(QUARTZ_DB_PROVIDER_RESOURCE_NAME);
            string[] providers = pp.GetPropertyGroups(QUARTZ_PROPERTY_GROUP_DB_PROVIDER);
            foreach (string providerName in providers)
            {
                DbMetadata metadata = new DbMetadata();
                NameValueCollection props = pp.GetPropertyGroup(QUARTZ_PROPERTY_GROUP_DB_PROVIDER + "." + providerName, true);
                ObjectUtils.SetObjectProperties(metadata, props);
                metadata.Init();
                dbMetadataLookup[providerName] = metadata;
            }
        }

        public DbProvider(string dbProviderName, string connectionString)
        {
            this.connectionString = connectionString;
            dbMetadata = (DbMetadata) dbMetadataLookup[dbProviderName];
            if (dbMetadata == null)
            {
                throw new ArgumentException(string.Format("Invalid DB provider name: {0}{1}{2}", dbProviderName, Environment.NewLine, GenerateValidProviderNamesInfo()));
            }
        }

        private string GenerateValidProviderNamesInfo()
        {
            StringBuilder sb = new StringBuilder("Valid DB Provider names are:").Append(Environment.NewLine);
            foreach (string providerName in dbMetadataLookup.Keys)
            {
                sb.Append("\t").Append(providerName).Append(Environment.NewLine);
            }
            return sb.ToString();
        }

        public IDbCommand CreateCommand()
        {
            return (IDbCommand) ObjectUtils.InstantiateType(dbMetadata.CommandType); 
        }

        public object CreateCommandBuilder()
        {
            throw new NotImplementedException();
        }

        public IDbConnection CreateConnection()
        {
            IDbConnection conn = (IDbConnection)ObjectUtils.InstantiateType(dbMetadata.ConnectionType);
            conn.ConnectionString = ConnectionString;
            return conn;
        }

        public IDbDataParameter CreateParameter()
        {
            return (IDbDataParameter) ObjectUtils.InstantiateType(dbMetadata.ParameterType);
        }

        public virtual string ConnectionString
        {
            get { return connectionString; }
            set { connectionString = value; }
        }

        public DbMetadata Metadata
        {
            get { return dbMetadata; }
        }

        public void Shutdown()
        {

        }
    }
}

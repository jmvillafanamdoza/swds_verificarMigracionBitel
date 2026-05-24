using MySql.Data.MySqlClient;
using Microsoft.Extensions.Configuration;
using System.Data;

namespace Aiwara.Scheduler.Da.VerificacionMigracionBitel
{
    public class ConnectionFactory
    {
        private readonly IConfiguration _configuration;

        public ConnectionFactory(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public IDbConnection GetConnection()
        {
            var connectionString = _configuration.GetConnectionString("DBConnection_Bitel");
            var connection = new MySqlConnection(connectionString);
            connection.Open();
            return connection;
        }
    }
}

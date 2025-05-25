using MySql.Data.MySqlClient;
using System.Threading.Tasks;

namespace drupaltowp
{
    public class DatabaseConnection
    {
        private readonly MySqlConnection _connection;
        private readonly string connectionString = "Server=localhost;Database=comunicarseweb;User ID=root;Password=;Port=3306;";

        public DatabaseConnection()
        {
            _connection = new MySqlConnection(connectionString);
        }

        public async Task ConnectAsync()
        {
            if (_connection.State != System.Data.ConnectionState.Open)
            {
                await _connection.OpenAsync();
            }
        }

        public async Task DisconnectAsync()
        {
            if (_connection.State != System.Data.ConnectionState.Closed)
            {
                await _connection.CloseAsync();
            }
        }

        public MySqlConnection Connection => _connection;
    }
}
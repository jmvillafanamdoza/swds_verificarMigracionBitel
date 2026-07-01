using Dapper;
using System.Data;
using Entity = Aiwara.Scheduler.Be.VerificacionMigracionBitel;

namespace Aiwara.Scheduler.Da.VerificacionMigracionBitel
{
    public class Repository : IRepository
    {
        private readonly ConnectionFactory _connectionFactory;

        public Repository(ConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        #region Metodos Lectura

        public async Task<IEnumerable<Entity.VerificacionMigracion>> getListVerificaciones()
        {
            using IDbConnection db = _connectionFactory.GetConnection();

            var resultado = await db.QueryAsync<Entity.VerificacionMigracion>(
                "SP_GET_MIGRACIONES_VERIFICACION_BITEL",
                commandType: CommandType.StoredProcedure
            );

            return resultado;
        }

        #endregion

        #region Metodos Escritura

        public async Task<bool> UpdMigracionObservadoAActivado(string celular, string accion = "OK")
        {
            using IDbConnection db = _connectionFactory.GetConnection();

            var parametros = new DynamicParameters();
            parametros.Add("p_celular_migracion", celular);
            parametros.Add("p_accion", accion); // Pasamos la acción al SP

            await db.ExecuteAsync(
                "SP_UPD_MIGRACION_OBSERVADO_A_ACTIVADO_BITEL",
                parametros,
                commandType: CommandType.StoredProcedure
            );

            return true;
        }

        #endregion
    }
}
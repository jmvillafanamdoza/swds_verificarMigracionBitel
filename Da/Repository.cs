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

        /// <summary>
        /// Obtiene la lista de números celulares pendientes de verificar.
        /// TODO: Reemplazar "SP_GET_LISTA_VERIFICACION_MIGRACION" con el nombre real del SP.
        ///       El SP debe retornar al menos la columna "celular".
        /// </summary>
        public async Task<IEnumerable<Entity.VerificacionMigracion>> getListVerificaciones()
        {
            using IDbConnection db = _connectionFactory.GetConnection();

            // TODO: Reemplazar con el nombre real del SP
            var parametros = new DynamicParameters();
            // parametros.Add("p_estado", "N"); // Ejemplo: solo pendientes

            var resultado = await db.QueryAsync<Entity.VerificacionMigracion>(
                // TODO: Reemplazar "SP_GET_LISTA_VERIFICACION_MIGRACION" con el nombre real del SP
                "SP_GET_LISTA_VERIFICACION_MIGRACION",
                parametros,
                commandType: CommandType.StoredProcedure
            );

            return resultado;
        }

        #endregion

        #region Metodos Escritura

        /// <summary>
        /// Registra el resultado de la verificación en la base de datos.
        /// TODO: Reemplazar "SP_INSERT_LOG_VERIFICACION_MIGRACION" con el nombre real del SP.
        ///       El SP recibe: p_celular, p_estado, p_paso, p_mensaje, p_fecha_registro.
        /// </summary>
        public async Task<bool> insertVerificacionLog(Entity.VerificacionMigracionLog log)
        {
            using IDbConnection db = _connectionFactory.GetConnection();

            var parametros = new DynamicParameters();
            parametros.Add("p_celular",          log.celular);
            parametros.Add("p_estado",           log.estado);
            parametros.Add("p_paso",             log.paso);
            parametros.Add("p_mensaje",          log.mensaje);
            parametros.Add("p_fecha_registro",   log.fechaRegistro);
            parametros.Add("p_mensaje_salida",   dbType: DbType.String, direction: ParameterDirection.Output, size: 500);

            // TODO: Reemplazar "SP_INSERT_LOG_VERIFICACION_MIGRACION" con el nombre real del SP
            await db.ExecuteAsync(
                "SP_INSERT_LOG_VERIFICACION_MIGRACION",
                parametros,
                commandType: CommandType.StoredProcedure
            );

            var mensajeSalida = parametros.Get<string>("p_mensaje_salida");
            return !string.IsNullOrWhiteSpace(mensajeSalida) && mensajeSalida.Contains("OK");
        }

        #endregion
    }
}

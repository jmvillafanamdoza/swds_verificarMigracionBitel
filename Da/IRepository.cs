using Entity = Aiwara.Scheduler.Be.VerificacionMigracionBitel;

namespace Aiwara.Scheduler.Da.VerificacionMigracionBitel
{
    public interface IRepository
    {
        #region Metodos Lectura

        // TODO: Reemplazar con el SP real que retorna la lista de números a verificar
        Task<IEnumerable<Entity.VerificacionMigracion>> getListVerificaciones();

        #endregion

        #region Metodos Escritura

        // TODO: Reemplazar con el SP real que actualiza el resultado de la verificación
        Task<bool> insertVerificacionLog(Entity.VerificacionMigracionLog log);

        #endregion
    }
}

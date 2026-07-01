using Entity = Aiwara.Scheduler.Be.VerificacionMigracionBitel;

namespace Aiwara.Scheduler.Da.VerificacionMigracionBitel
{
    public interface IRepository
    {
        #region Metodos Lectura

        Task<IEnumerable<Entity.VerificacionMigracion>> getListVerificaciones();

        #endregion

        #region Metodos Escritura

        Task<bool> UpdMigracionObservadoAActivado(string celular, string accion = "OK");

        #endregion
    }
}
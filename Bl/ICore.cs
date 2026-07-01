using Entity = Aiwara.Scheduler.Be.VerificacionMigracionBitel;

namespace Aiwara.Scheduler.Bl.VerificacionMigracionBitel
{
    public interface ICore
    {
        #region Metodos Lectura

        Task<IEnumerable<Entity.VerificacionMigracion>> getListVerificaciones();

        #endregion

        #region Metodos Escritura

        Task<bool> updMigracionObservadoAActivado(string celular, string accion);

        #endregion
    }
}
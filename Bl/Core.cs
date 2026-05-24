using Data   = Aiwara.Scheduler.Da.VerificacionMigracionBitel;
using Entity = Aiwara.Scheduler.Be.VerificacionMigracionBitel;

namespace Aiwara.Scheduler.Bl.VerificacionMigracionBitel
{
    public class Core : ICore
    {
        private readonly Data.IRepository _repository;

        public Core(Data.IRepository repository)
        {
            _repository = repository;
        }

        #region Metodos Lectura

        public async Task<IEnumerable<Entity.VerificacionMigracion>> getListVerificaciones()
        {
            return await _repository.getListVerificaciones();
        }

        #endregion

        #region Metodos Escritura

        public async Task<bool> insertVerificacionLog(Entity.VerificacionMigracionLog log)
        {
            return await _repository.insertVerificacionLog(log);
        }

        #endregion
    }
}

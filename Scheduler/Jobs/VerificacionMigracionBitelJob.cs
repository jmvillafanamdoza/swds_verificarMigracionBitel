using Aiwara.Scheduler.VerificacionMigracionBitel.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Quartz;
using Logic = Aiwara.Scheduler.Bl.VerificacionMigracionBitel;

namespace Aiwara.Scheduler.VerificacionMigracionBitel.Jobs
{
    [DisallowConcurrentExecution]
    public class VerificacionMigracionBitelJob : IJob
    {
        private readonly Logic.ICore                              _core;
        private readonly ILogger<VerificacionMigracionBitelJob>   _logger;
        private readonly IConfiguration                           _configuration;

        public VerificacionMigracionBitelJob(
            Logic.ICore core,
            ILogger<VerificacionMigracionBitelJob> logger,
            IConfiguration configuration)
        {
            _core          = core;
            _logger        = logger;
            _configuration = configuration;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            _logger.LogInformation("========================================");
            _logger.LogInformation("Inicio Job VerificacionMigracionBitel: {Fecha}", DateTime.Now);

            // ── 1. Obtener lista desde BD ─────────────────────────────────────
            var listaVerificaciones = await _core.getListVerificaciones();

            if (listaVerificaciones == null || !listaVerificaciones.Any())
            {
                _logger.LogInformation("No hay verificaciones pendientes. Job finalizado.");
                _logger.LogInformation("========================================");
                return;
            }

            var lista = listaVerificaciones.ToList();
            _logger.LogInformation("Verificaciones pendientes: {Count}", lista.Count);

            // ── 2. Iniciar portal Bitel ───────────────────────────────────────
            await using var portalService = new BitelPortalService(
                _configuration,
                _logger as ILogger<BitelPortalService>
                    ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<BitelPortalService>.Instance,
                _core
            );

            bool loginOk = await portalService.LoginAsync();
            if (!loginOk)
            {
                _logger.LogError("Login fallido. Job cancelado.");
                _logger.LogInformation("========================================");
                return;
            }

            // ── 3. Procesar cada número ────────────────────────────────────────
            int procesados  = 0;
            int otraTienda  = 0;
            int errores     = 0;
            int browserClosed = 0;

            foreach (var verificacion in lista)
            {
                try
                {
                    _logger.LogInformation("----------------------------------------");
                    _logger.LogInformation("Procesando celular: {Celular}", verificacion.celular);

                    var (exitoso, mensaje) = await portalService.VerificarCelularAsync(verificacion.celular);

                    if (mensaje == "BROWSER_CLOSED")
                    {
                        browserClosed++;
                        _logger.LogWarning("Browser cerrado para {Celular} — contando sin error BD.", verificacion.celular);
                        break; // Salir del loop, el browser ya no sirve
                    }

                    if (exitoso)
                    {
                        procesados++;
                        _logger.LogInformation("Estado >> OK para {Celular}", verificacion.celular);
                    }
                    else if (mensaje.Contains("Migrado por otra tienda"))
                    {
                        otraTienda++;
                        _logger.LogWarning("Estado >> OTRA TIENDA para {Celular}: {Msg}", verificacion.celular, mensaje);
                    }
                    else
                    {
                        errores++;
                        _logger.LogWarning("Estado >> ERROR para {Celular}: {Msg}", verificacion.celular, mensaje);
                    }

                    // Pequeña pausa entre números para no saturar el portal
                    await Task.Delay(1500);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error inesperado procesando celular: {Celular}", verificacion.celular);
                    errores++;
                }
            }

            // ── Resumen ────────────────────────────────────────────────────────
            _logger.LogInformation("========================================");
            _logger.LogInformation("Resumen: OK={Ok} | OtraTienda={Tienda} | Errores={Err} | BrowserClosed={Bc} | Total={Total}",
                procesados, otraTienda, errores, browserClosed, lista.Count);
            _logger.LogInformation("Fin Job VerificacionMigracionBitel: {Fecha}", DateTime.Now);
            _logger.LogInformation("========================================");
        }
    }
}

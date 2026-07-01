using Aiwara.Scheduler.VerificacionMigracionBitel.Services;
using Microsoft.Extensions.Logging;
using Quartz;
using Logic = Aiwara.Scheduler.Bl.VerificacionMigracionBitel;
using Entity = Aiwara.Scheduler.Be.VerificacionMigracionBitel;

namespace Aiwara.Scheduler.VerificacionMigracionBitel.Jobs
{
    [DisallowConcurrentExecution]
    public class VerificacionMigracionBitelJob : IJob
    {
        private readonly Logic.ICore _core;
        private readonly ILogger<VerificacionMigracionBitelJob> _logger;
        private readonly IServiceProvider _serviceProvider;

        public VerificacionMigracionBitelJob(
            Logic.ICore core,
            ILogger<VerificacionMigracionBitelJob> logger,
            IServiceProvider serviceProvider)
        {
            _core = core;
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            _logger.LogInformation("========================================");
            _logger.LogInformation("Inicio Job VerificacionMigracionBitel: {Fecha}", DateTime.Now);

            // ── 1. Obtener lista desde BD ─────────────────────────────────────
            _logger.LogInformation("Obteniendo lista de verificaciones pendientes...");
            var listaVerificaciones = (await _core.getListVerificaciones()).ToList();

            if (!listaVerificaciones.Any())
            {
                _logger.LogInformation("No hay verificaciones pendientes. Job finalizado.");
                _logger.LogInformation("========================================");
                return;
            }

            _logger.LogInformation("Verificaciones pendientes: {Count}", listaVerificaciones.Count);

            int procesados = 0;
            int otraTienda = 0;
            int errores = 0;

            // ── 2. Procesar cada número secuencialmente ───────────────────────
            foreach (var verificacion in listaVerificaciones)
            {
                _logger.LogInformation("----------------------------------------");
                _logger.LogInformation("Procesando celular: {Celular}", verificacion.celular);

                await using var portalService = _serviceProvider
                    .GetRequiredService<BitelPortalService>();

                bool procesadoOk = false;
                int maxReintentos = 2;

                for (int reintento = 1; reintento <= maxReintentos; reintento++)
                {
                    if (reintento > 1)
                    {
                        _logger.LogWarning("Reintento {R}/{Max} con login fresco para {Celular}...",
                            reintento, maxReintentos, verificacion.celular);
                        await portalService.ForzarNuevoLoginAsync();
                    }

                    try
                    {
                        using var cts = new CancellationTokenSource();
                        var tareaItem = ProcesarItemAsync(verificacion, portalService);
                        var tareaTimeout = Task.Delay(TimeSpan.FromMinutes(2), cts.Token);
                        var completada = await Task.WhenAny(tareaItem, tareaTimeout);

                        if (completada == tareaTimeout)
                        {
                            _logger.LogWarning("Timeout 2 min para {Celular} — pasando al siguiente...",
                                verificacion.celular);
                            await portalService.ForzarNuevoLoginAsync();
                            break;
                        }

                        cts.Cancel();
                        var (exitoso, mensaje) = await tareaItem;

                        if (exitoso)
                        {
                            procesados++;
                            _logger.LogInformation("[{Celular}] Estado >> OK", verificacion.celular);
                        }
                        else if (mensaje.Contains("Migrado por otra tienda"))
                        {
                            otraTienda++;
                            _logger.LogWarning("[{Celular}] Estado >> OTRA TIENDA: {Msg}",
                                verificacion.celular, mensaje);
                        }
                        else
                        {
                            errores++;
                            _logger.LogWarning("[{Celular}] Estado >> ERROR: {Msg}",
                                verificacion.celular, mensaje);
                        }

                        procesadoOk = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error procesando celular: {Celular}", verificacion.celular);
                        await portalService.ForzarNuevoLoginAsync();
                        break;
                    }
                }

                if (!procesadoOk)
                    errores++;

                await Task.Delay(1500);
            }

            // ── Resumen ───────────────────────────────────────────────────────
            _logger.LogInformation("========================================");
            _logger.LogInformation("Resumen: OK={Ok} | OtraTienda={Tienda} | Errores={Err} | Total={Total}",
                procesados, otraTienda, errores, listaVerificaciones.Count);
            _logger.LogInformation("Fin Job VerificacionMigracionBitel: {Fecha}", DateTime.Now);
            _logger.LogInformation("========================================");
        }

        // ════════════════════════════════════════════════════════════════════════
        // Procesar un número — igual patrón que el otro bot
        // ════════════════════════════════════════════════════════════════════════
        private async Task<(bool exitoso, string mensaje)> ProcesarItemAsync(
            Entity.VerificacionMigracion verificacion,
            BitelPortalService portalService)
        {
            // ── PASO 1: Login ─────────────────────────────────────────────────
            bool loginOk = await portalService.EnsureLoggedInAsync();
            if (!loginOk)
            {
                _logger.LogWarning("Sin sesión para celular: {Celular}", verificacion.celular);
                await portalService.ForzarNuevoLoginAsync();
                return (false, "Login fallido");
            }

            // ── PASO 2-6: Verificar número ────────────────────────────────────
            var (exitoso, mensaje) = await portalService.VerificarCelularAsync(verificacion.celular);

            if (!exitoso)
            {
                await portalService.ForzarNuevoLoginAsync();
            }

            return (exitoso, mensaje);
        }
    }
}
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Logic  = Aiwara.Scheduler.Bl.VerificacionMigracionBitel;
using Entity = Aiwara.Scheduler.Be.VerificacionMigracionBitel;

namespace Aiwara.Scheduler.VerificacionMigracionBitel.Services
{
    public class BitelPortalService : IAsyncDisposable
    {
        private readonly IConfiguration                    _configuration;
        private readonly ILogger<BitelPortalService>       _logger;
        private readonly Logic.ICore                       _core;

        private IPlaywright? _playwright;
        private IBrowser?    _browser;
        private IPage?       _page;

        // ── Constantes ────────────────────────────────────────────────────────
        private const string URL_LOGIN     = "http://cm.bitel.com.pe:8046/BCCS_CM/authenticateAction.do?className=AuthenticateDAO&methodName=actionLogin";
        private const string URL_POSTPAGO  = "http://cm.bitel.com.pe:8046/BCCS_CM/manageSubscriber.do?_vt=7234e4aeaf6dc7688f046171250a3940";
        private const string TIENDA_ESPERADA = "VTPBC28";
        private const string ESTADO_ESPERADO = "Normal";
        private const int    TIMEOUT_MS    = 30_000;

        public BitelPortalService(
            IConfiguration configuration,
            ILogger<BitelPortalService> logger,
            Logic.ICore core)
        {
            _configuration = configuration;
            _logger        = logger;
            _core          = core;
        }

        // ── Helper: guardar log en BD ─────────────────────────────────────────
        private async Task GuardarLogAsync(string celular, string estado, string paso, string mensaje)
        {
            var log = new Entity.VerificacionMigracionLog
            {
                celular       = celular,
                estado        = estado,
                paso          = paso,
                mensaje       = mensaje,
                fechaRegistro = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
            await _core.insertVerificacionLog(log);
        }

        // ════════════════════════════════════════════════════════════════════════
        // PASO 1 — Iniciar navegador y hacer Login
        // ════════════════════════════════════════════════════════════════════════
        public async Task<bool> LoginAsync()
        {
            _logger.LogInformation("PASO 1 — Iniciando navegador...");

            _playwright = await Playwright.CreateAsync();
            _browser    = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless       = false,
                ExecutablePath = _configuration["Playwright:ExecutablePath"]
                                 ?? @"C:\Program Files\Google\Chrome\Application\chrome.exe"
            });

            var context = await _browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1366, Height = 768 }
            });

            _page = await context.NewPageAsync();
            _page.SetDefaultTimeout(TIMEOUT_MS);

            _logger.LogInformation("PASO 1 — Navegando a login: {Url}", URL_LOGIN);
            await _page.GotoAsync(URL_LOGIN, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

            // Ingresar credenciales
            var usuario  = _configuration["BitelCredentials:Username"] ?? string.Empty;
            var password = _configuration["BitelCredentials:Password"]  ?? string.Empty;

            await _page.FillAsync("input[name='username'], input[name='userName'], input[name='userId']", usuario);
            await _page.FillAsync("input[type='password']", password);

            var btnLogin = _page.Locator("input[type='submit'], button[type='submit']");
            await btnLogin.ClickAsync();

            // Esperar menú principal
            try
            {
                await _page.WaitForSelectorAsync("#module-menu, .x-toolbar", 
                    new PageWaitForSelectorOptions { Timeout = TIMEOUT_MS });

                _logger.LogInformation("PASO 1 — Login exitoso. URL: {Url}", _page.Url);
                return true;
            }
            catch
            {
                _logger.LogWarning("PASO 1 — Login fallido para usuario: {User}", usuario);
                return false;
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        // VERIFICAR UN NÚMERO — flujo completo
        // ════════════════════════════════════════════════════════════════════════
        public async Task<(bool exitoso, string mensaje)> VerificarCelularAsync(string celular)
        {
            if (_page == null || _page.IsClosed)
                return (false, "BROWSER_CLOSED");

            try
            {
                // ── PASO 2: Ir a Cliente Móvil Postpago ──────────────────────────
                _logger.LogInformation("[{Celular}] PASO 2 — Navegando a Cliente móvil postpago...", celular);

                await _page.GotoAsync(URL_POSTPAGO, 
                    new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = TIMEOUT_MS });

                await _page.WaitForSelectorAsync("fieldset", 
                    new PageWaitForSelectorOptions { Timeout = TIMEOUT_MS });

                // ── PASO 3: Marcar checkboxes de tratamiento de datos ────────────
                _logger.LogInformation("[{Celular}] PASO 3 — Verificando checkboxes de tratamiento de datos...", celular);

                var checkboxes = await _page.QuerySelectorAllAsync("input[name='term_checkbox']");
                foreach (var chk in checkboxes)
                {
                    var checked_ = await chk.IsCheckedAsync();
                    if (!checked_)
                    {
                        await chk.ClickAsync();
                        await _page.WaitForTimeoutAsync(300);
                    }
                }

                if (checkboxes.Count > 0)
                {
                    // Esperar que se habiliten los campos
                    await _page.WaitForFunctionAsync(
                        "() => { const el = document.getElementById('searchSubBean.isdn'); return el && !el.disabled; }",
                        null,
                        new PageWaitForFunctionOptions { Timeout = 5_000 }
                    ).ContinueWith(_ => { }); // ignorar timeout si no hay checkboxes

                    _logger.LogInformation("[{Celular}] PASO 3 — Checkboxes marcados OK.", celular);
                }

                // ── PASO 4: Ingresar ISDN y buscar ──────────────────────────────
                _logger.LogInformation("[{Celular}] PASO 4 — Ingresando número y buscando...", celular);

                var campoIsdn = _page.Locator("#searchSubBean\\.isdn");
                await campoIsdn.EvaluateAsync("el => el.removeAttribute('disabled')");
                await campoIsdn.FillAsync(celular);

                // Limpiar otros campos
                foreach (var id in new[] { "searchSubBean\\.imsi", "searchSubBean\\.custId",
                                           "searchSubBean\\.idNo", "searchSubBean\\.contractNo" })
                {
                    var campo = _page.Locator($"#{id}");
                    if (await campo.CountAsync() > 0)
                        await campo.EvaluateAsync("el => el.value = ''");
                }

                await _page.ClickAsync("#myBtn");

                // Esperar que aparezca la tabla de resultados
                try
                {
                    await _page.WaitForSelectorAsync("#lstRow tbody tr",
                        new PageWaitForSelectorOptions { Timeout = TIMEOUT_MS });
                }
                catch
                {
                    string mensajeError = "Número no encontrado en el sistema postpago";
                    _logger.LogWarning("[{Celular}] PASO 4 — {Msg}", celular, mensajeError);
                    await GuardarLogAsync(celular, "E", "PASO_4_BUSCAR", mensajeError);
                    return (false, mensajeError);
                }

                // ── PASO 5: Click en el número para ver detalle ──────────────────
                _logger.LogInformation("[{Celular}] PASO 5 — Cargando detalle del suscriptor...", celular);

                var linkSuscriptor = _page.Locator("#lstRow a").First;
                if (await linkSuscriptor.CountAsync() == 0)
                {
                    string mensajeError = "No se encontró el link del suscriptor en la tabla de resultados";
                    _logger.LogWarning("[{Celular}] PASO 5 — {Msg}", celular, mensajeError);
                    await GuardarLogAsync(celular, "E", "PASO_5_DETALLE", mensajeError);
                    return (false, mensajeError);
                }

                await linkSuscriptor.ClickAsync();

                // Esperar que cargue el detalle via AJAX (aparece mobileForm.status)
                try
                {
                    await _page.WaitForSelectorAsync("#mobileForm\\.status",
                        new PageWaitForSelectorOptions { Timeout = TIMEOUT_MS });
                }
                catch
                {
                    string mensajeError = "El detalle del suscriptor no cargó (timeout esperando #mobileForm.status)";
                    _logger.LogWarning("[{Celular}] PASO 5 — {Msg}", celular, mensajeError);
                    await GuardarLogAsync(celular, "E", "PASO_5_DETALLE", mensajeError);
                    return (false, mensajeError);
                }

                // ── PASO 6: Verificar campos ─────────────────────────────────────
                _logger.LogInformation("[{Celular}] PASO 6 — Verificando campos...", celular);

                var estadoBloqueo = (await _page.InputValueAsync("#mobileForm\\.status")).Trim();
                var fechaFirma    = (await _page.InputValueAsync("#mobileForm\\.effectDate")).Trim();
                var codigoTienda  = (await _page.InputValueAsync("#mobileForm\\.shopCode")).Trim();

                _logger.LogInformation("[{Celular}]   Estado bloqueo : {Val}", celular, estadoBloqueo);
                _logger.LogInformation("[{Celular}]   Fecha firma    : {Val}", celular, fechaFirma);
                _logger.LogInformation("[{Celular}]   Código tienda  : {Val}", celular, codigoTienda);

                // ── Validación 1: Estado = Normal ────────────────────────────────
                if (estadoBloqueo != ESTADO_ESPERADO)
                {
                    string mensajeError = $"Estado inesperado: '{estadoBloqueo}' (esperado: '{ESTADO_ESPERADO}')";
                    _logger.LogWarning("[{Celular}] PASO 6 — {Msg}", celular, mensajeError);
                    await GuardarLogAsync(celular, "E", "PASO_6_VERIFICAR_ESTADO", mensajeError);
                    return (false, mensajeError);
                }

                // ── Validación 2: Fecha de firma con valor ────────────────────────
                if (string.IsNullOrWhiteSpace(fechaFirma))
                {
                    string mensajeError = "Fecha de firma vacía o sin valor";
                    _logger.LogWarning("[{Celular}] PASO 6 — {Msg}", celular, mensajeError);
                    await GuardarLogAsync(celular, "E", "PASO_6_VERIFICAR_FECHA", mensajeError);
                    return (false, mensajeError);
                }

                // ── Validación 3: Código de tienda ───────────────────────────────
                if (codigoTienda != TIENDA_ESPERADA)
                {
                    string mensajeOtraTienda = $"Migrado por otra tienda: '{codigoTienda}' (esperada: '{TIENDA_ESPERADA}')";
                    _logger.LogWarning("[{Celular}] PASO 6 — {Msg}", celular, mensajeOtraTienda);
                    await GuardarLogAsync(celular, "T", "PASO_6_VERIFICAR_TIENDA", mensajeOtraTienda);
                    return (false, mensajeOtraTienda);
                }

                // ── Todo OK ──────────────────────────────────────────────────────
                string mensajeOk = $"Verificación exitosa - Estado: {estadoBloqueo}, Tienda: {codigoTienda}, Fecha: {fechaFirma}";
                _logger.LogInformation("[{Celular}] PASO 6 — OK: {Msg}", celular, mensajeOk);
                await GuardarLogAsync(celular, "S", "PASO_6_VERIFICACION_OK", mensajeOk);
                return (true, mensajeOk);
            }
            catch (Exception ex) when (ex.Message.Contains("Target page, context or browser has been closed"))
            {
                _logger.LogWarning("[{Celular}] Browser cerrado inesperadamente — contando +1 sin error BD.", celular);
                return (false, "BROWSER_CLOSED");
            }
            catch (Exception ex)
            {
                string mensajeError = $"Error inesperado: {ex.Message}";
                _logger.LogError(ex, "[{Celular}] Error inesperado en VerificarCelularAsync", celular);

                if (_page != null && !_page.IsClosed)
                    await GuardarLogAsync(celular, "E", "PASO_ERROR_GENERAL", mensajeError);

                return (false, mensajeError);
            }
        }

        public IPage? GetPage() => _page;

        public async ValueTask DisposeAsync()
        {
            if (_browser is not null)
                await _browser.CloseAsync();
            _playwright?.Dispose();
        }
    }
}

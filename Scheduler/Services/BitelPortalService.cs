using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Logic = Aiwara.Scheduler.Bl.VerificacionMigracionBitel;

namespace Aiwara.Scheduler.VerificacionMigracionBitel.Services
{
    public class BitelPortalService : IAsyncDisposable
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<BitelPortalService> _logger;
        private readonly Logic.ICore _core;

        private IPlaywright? _playwright;
        private IBrowser? _browser;
        private IBrowserContext? _context;
        private IPage? _page;
        private bool _isLoggedIn = false;

        private const string URL_LOGIN = "http://cm.bitel.com.pe:8046/BCCS_CM/authenticateAction.do?className=AuthenticateDAO&methodName=actionLogin";
        private const string TIENDA_ESPERADA = "VTPBC28";
        private const string ESTADO_ESPERADO = "Normal";

        public BitelPortalService(
            IConfiguration configuration,
            ILogger<BitelPortalService> logger,
            Logic.ICore core)
        {
            _configuration = configuration;
            _logger = logger;
            _core = core;
        }

        // ════════════════════════════════════════════════════════════════════════
        // EnsureLoggedInAsync
        // ════════════════════════════════════════════════════════════════════════
        public async Task<bool> EnsureLoggedInAsync()
        {
            const int MAX_REINTENTOS = 3;
            const int ESPERA_SEGUNDOS = 1;

            if (_isLoggedIn && _page != null && !_page.IsClosed)
            {
                _logger.LogInformation("Sesión activa. Reutilizando browser.");
                return true;
            }

            for (int intento = 1; intento <= MAX_REINTENTOS; intento++)
            {
                _logger.LogInformation("Login intento {Intento}/{Max}...", intento, MAX_REINTENTOS);

                try
                {
                    if (_browser != null)
                    {
                        await _browser.CloseAsync();
                        _browser = null;
                        _page = null;
                        _isLoggedIn = false;
                        _playwright?.Dispose();
                        _playwright = null;
                    }

                    bool ok = await LoginAsync();

                    if (ok)
                    {
                        _logger.LogInformation("Login exitoso en intento {Intento}.", intento);
                        return true;
                    }

                    _logger.LogWarning("Intento {Intento}/{Max} fallido. Reintentando en {Seg}s...",
                        intento, MAX_REINTENTOS, ESPERA_SEGUNDOS);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Excepción en intento {Intento}/{Max}: {Mensaje}",
                        intento, MAX_REINTENTOS, ex.Message);
                }

                if (intento < MAX_REINTENTOS)
                    await Task.Delay(TimeSpan.FromSeconds(ESPERA_SEGUNDOS));
            }

            _logger.LogError("Login fallido después de {Max} intentos.", MAX_REINTENTOS);
            return false;
        }

        // ════════════════════════════════════════════════════════════════════════
        // ForzarNuevoLoginAsync
        // ════════════════════════════════════════════════════════════════════════
        public async Task ForzarNuevoLoginAsync()
        {
            try
            {
                if (_browser != null)
                {
                    await _browser.CloseAsync();
                    _browser = null;
                }
                _playwright?.Dispose();
                _playwright = null;
                _page = null;
                _isLoggedIn = false;
                _logger.LogInformation("Browser cerrado. Listo para nuevo login.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error al forzar nuevo login: {Msg}", ex.Message);
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        // LoginAsync
        // ════════════════════════════════════════════════════════════════════════
        private async Task<bool> LoginAsync()
        {
            var usuario = _configuration["BitelCredentials:Username"] ?? string.Empty;
            var password = _configuration["BitelCredentials:Password"] ?? string.Empty;

            var chromePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
            if (!File.Exists(chromePath))
                chromePath = @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe";

            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                ExecutablePath = chromePath,
                Args = new[] { "--ignore-certificate-errors" }
            });

            _context = await _browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1366, Height = 768 },
                IgnoreHTTPSErrors = true,
                AcceptDownloads = true
            });

            _page = await _context.NewPageAsync();

            _logger.LogInformation("LoginAsync — Navegando a: {Url}", URL_LOGIN);
            await _page.GotoAsync(URL_LOGIN, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 30_000
            });

            await _page.WaitForSelectorAsync(
                "input[name='username'], input[name='userName'], input[name='userId']",
                new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });

            await _page.ClickAsync("input[name='username'], input[name='userName'], input[name='userId']");
            await _page.FillAsync("input[name='username'], input[name='userName'], input[name='userId']", "");
            await _page.TypeAsync("input[name='username'], input[name='userName'], input[name='userId']",
                usuario, new PageTypeOptions { Delay = 50 });

            string valorUsuario = await _page.InputValueAsync(
                "input[name='username'], input[name='userName'], input[name='userId']");
            _logger.LogInformation("LoginAsync — Usuario ingresado: '{Val}'", valorUsuario);

            await _page.ClickAsync("input[type='password']");
            await _page.FillAsync("input[type='password']", "");
            await _page.TypeAsync("input[type='password']", password, new PageTypeOptions { Delay = 50 });

            await Task.Delay(1_000);

            string valUser = await _page.InputValueAsync(
                "input[name='username'], input[name='userName'], input[name='userId']");
            string valPass = await _page.InputValueAsync("input[type='password']");

            if (string.IsNullOrEmpty(valUser) || string.IsNullOrEmpty(valPass))
            {
                _logger.LogWarning("LoginAsync — Campos vacíos. user='{U}' pass='{P}'",
                    valUser, string.IsNullOrEmpty(valPass) ? "VACIO" : "OK");
                _isLoggedIn = false;
                return false;
            }

            _logger.LogInformation("LoginAsync — Click submit...");
            await _page.ClickAsync("input[type='submit'], button[type='submit']");

            await _page.WaitForLoadStateAsync(LoadState.NetworkIdle,
                new PageWaitForLoadStateOptions { Timeout = 30_000 });

            try
            {
                await _page.WaitForSelectorAsync(
                    "a[href*='logout'], a:has-text('Salir')",
                    new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });

                _logger.LogInformation("LoginAsync — Login exitoso. Detectado enlace Salir.");
                _isLoggedIn = true;
                return true;
            }
            catch
            {
                _logger.LogWarning("LoginAsync — No se detectó sesión activa.");
                _isLoggedIn = false;
                return false;
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        // VerificarCelularAsync — navega por menú, no por URL directa
        // ════════════════════════════════════════════════════════════════════════
        public async Task<(bool exitoso, string mensaje)> VerificarCelularAsync(string celular)
        {
            if (_page == null || _page.IsClosed)
                return (false, "BROWSER_CLOSED");

            try
            {
                // ── PASO 2: Navegar por menú a Cliente Móvil Postpago ─────────────
                // No usar URL directa con _vt= porque ese token expira
                // Navegar via hover en "Gestión postpago" y click en "Cliente móvil postpago"
                _logger.LogInformation("[{Celular}] PASO 2 — Navegando por menú a Cliente móvil postpago...", celular);

                // Cerrar cualquier menú abierto primero
                await _page.Keyboard.PressAsync("Escape");
                await Task.Delay(300);

                // Click en "Gestión postpago"
                _logger.LogInformation("[{Celular}] Navegando: Click en Gestión postpago...", celular);
                await _page.ClickAsync("button.x-btn-text:has-text('Gestión postpago')");

                // Esperar y hover en "Gestión de clientes postpago"
                await _page.WaitForSelectorAsync(
                    ".x-menu-item:has-text('Gestión de clientes postpago')",
                    new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
                _logger.LogInformation("[{Celular}] Navegando: Hover en Gestión de clientes postpago...", celular);
                await _page.HoverAsync(".x-menu-item:has-text('Gestión de clientes postpago')");

                // Esperar y click en "Cliente móvil postpago" con espera de navegación
                await _page.WaitForSelectorAsync(
                    ".x-menu-item:has-text('Cliente móvil postpago')",
                    new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
                _logger.LogInformation("[{Celular}] Navegando: Click en Cliente móvil postpago...", celular);
                await _page.RunAndWaitForNavigationAsync(async () =>
                {
                    await _page.ClickAsync(".x-menu-item:has-text('Cliente móvil postpago')");
                },
                new PageRunAndWaitForNavigationOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = 30_000
                });

                _logger.LogInformation("[{Celular}] PASO 2 — Página postpago cargada. URL: {Url}",
                    celular, _page.Url);

                // ── PASO 3: Marcar checkboxes de tratamiento de datos ────────────
                _logger.LogInformation("[{Celular}] PASO 3 — Verificando checkboxes...", celular);

                var checkboxes = await _page.QuerySelectorAllAsync("input[name='term_checkbox']");
                foreach (var chk in checkboxes)
                {
                    if (!await chk.IsCheckedAsync())
                    {
                        await chk.ClickAsync();
                        await Task.Delay(400);
                    }
                }

                if (checkboxes.Count > 0)
                {
                    await _page.WaitForFunctionAsync(
                        "() => { const el = document.getElementById('searchSubBean.isdn'); return el && !el.disabled; }",
                        null,
                        new PageWaitForFunctionOptions { Timeout = 8_000 }
                    ).ContinueWith(_ => { });

                    _logger.LogInformation("[{Celular}] PASO 3 — Checkboxes marcados OK.", celular);
                }

                await Task.Delay(500);

                // ── PASO 4: Ingresar ISDN y buscar ──────────────────────────────
                _logger.LogInformation("[{Celular}] PASO 4 — Ingresando número...", celular);

                var campoIsdn = await _page.QuerySelectorAsync("#searchSubBean\\.isdn");
                if (campoIsdn != null)
                {
                    await campoIsdn.EvaluateAsync("el => el.removeAttribute('disabled')");
                    await campoIsdn.ClickAsync();
                }

                await _page.FillAsync("#searchSubBean\\.isdn", "");
                await _page.TypeAsync("#searchSubBean\\.isdn", celular, new PageTypeOptions { Delay = 80 });

                string valorIsdn = await _page.InputValueAsync("#searchSubBean\\.isdn");
                _logger.LogInformation("[{Celular}] PASO 4 — ISDN ingresado: '{Val}'", celular, valorIsdn);

                foreach (var id in new[] { "searchSubBean.imsi", "searchSubBean.custId",
                                           "searchSubBean.idNo",  "searchSubBean.contractNo" })
                {
                    var campo = await _page.QuerySelectorAsync($"#{id}");
                    if (campo != null)
                        await campo.EvaluateAsync("el => el.value = ''");
                }

                await Task.Delay(400);
                await _page.ClickAsync("#myBtn");

                try
                {
                    await _page.WaitForSelectorAsync("#lstRow tbody tr",
                        new PageWaitForSelectorOptions { Timeout = 30_000 });
                }
                catch
                {
                    string mensajeError = "Número no encontrado en el sistema postpago";
                    _logger.LogWarning("[{Celular}] PASO 4 — {Msg}", celular, mensajeError);
                    return (false, mensajeError);
                }

                // ── PASO 5: Click en el número para cargar detalle ───────────────
                _logger.LogInformation("[{Celular}] PASO 5 — Cargando detalle del suscriptor...", celular);

                var linkSuscriptor = _page.Locator("#lstRow a").First;
                if (await linkSuscriptor.CountAsync() == 0)
                {
                    string mensajeError = "No se encontró el link del suscriptor en la tabla";
                    _logger.LogWarning("[{Celular}] PASO 5 — {Msg}", celular, mensajeError);
                    return (false, mensajeError);
                }

                await linkSuscriptor.ClickAsync();

                try
                {
                    await _page.WaitForSelectorAsync("#mobileForm\\.status",
                        new PageWaitForSelectorOptions { Timeout = 30_000 });
                }
                catch
                {
                    string mensajeError = "El detalle del suscriptor no cargó (timeout #mobileForm.status)";
                    _logger.LogWarning("[{Celular}] PASO 5 — {Msg}", celular, mensajeError);
                    return (false, mensajeError);
                }

                await Task.Delay(500);

                // ── PASO 6: Verificar campos ─────────────────────────────────────
                _logger.LogInformation("[{Celular}] PASO 6 — Verificando campos...", celular);

                var estadoBloqueo = (await _page.InputValueAsync("#mobileForm\\.status")).Trim();
                var fechaFirma = (await _page.InputValueAsync("#mobileForm\\.effectDate")).Trim();
                var codigoTienda = (await _page.InputValueAsync("#mobileForm\\.shopCode")).Trim();

                _logger.LogInformation("[{Celular}]   Estado bloqueo : {Val}", celular, estadoBloqueo);
                _logger.LogInformation("[{Celular}]   Fecha firma    : {Val}", celular, fechaFirma);
                _logger.LogInformation("[{Celular}]   Código tienda  : {Val}", celular, codigoTienda);

                // Validación 1: Estado = Normal
                if (estadoBloqueo != ESTADO_ESPERADO)
                {
                    string mensajeError = $"Estado inesperado: '{estadoBloqueo}' (esperado: '{ESTADO_ESPERADO}')";
                    _logger.LogWarning("[{Celular}] PASO 6 — {Msg}", celular, mensajeError);
                    await _core.updMigracionObservadoAActivado(celular, "RECHAZADO_ESTADO");
                    return (false, mensajeError);
                }

                // Validación 2: Fecha de firma con valor
                if (string.IsNullOrWhiteSpace(fechaFirma))
                {
                    string mensajeError = "Fecha de firma vacía o sin valor";
                    _logger.LogWarning("[{Celular}] PASO 6 — {Msg}", celular, mensajeError);
                    await _core.updMigracionObservadoAActivado(celular, "NO_TIENE_FECHA");
                    return (false, mensajeError);
                }

                // Validación 3: Código de tienda
                if (codigoTienda != TIENDA_ESPERADA)
                {
                    string mensajeOtraTienda = $"Migrado por otra tienda: '{codigoTienda}' (esperada: '{TIENDA_ESPERADA}')";
                    _logger.LogWarning("[{Celular}] PASO 6 — {Msg}", celular, mensajeOtraTienda);
                    await _core.updMigracionObservadoAActivado(celular, "OTRA_TIENDA");
                    return (false, mensajeOtraTienda);
                }

                // ── Todo OK: actualizar estado en BD ─────────────────────────────
                _logger.LogInformation("[{Celular}] PASO 6 — Verificación OK. Actualizando BD...", celular);
                await _core.updMigracionObservadoAActivado(celular, "OK");
                _logger.LogInformation("[{Celular}] PASO 6 — BD actualizada: estado_venta = ACTIVADO", celular);

                return (true, $"Verificación exitosa - Estado: {estadoBloqueo}, Tienda: {codigoTienda}, Fecha: {fechaFirma}");
            }
            catch (Exception ex) when (ex.Message.Contains("Target page, context or browser has been closed"))
            {
                _logger.LogWarning("[{Celular}] Browser cerrado inesperadamente.", celular);
                return (false, "BROWSER_CLOSED");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Celular}] Error inesperado en VerificarCelularAsync", celular);
                return (false, $"Error inesperado: {ex.Message}");
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_browser is not null)
                await _browser.CloseAsync();
            _playwright?.Dispose();
        }
    }
}
using Aiwara.Scheduler.VerificacionMigracionBitel.Jobs;
using Aiwara.Scheduler.VerificacionMigracionBitel.Services;
using Quartz;
using Serilog;
using Data = Aiwara.Scheduler.Da.VerificacionMigracionBitel;
using Logic = Aiwara.Scheduler.Bl.VerificacionMigracionBitel;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File(
        path: Path.Combine(AppContext.BaseDirectory, "Logger", "log-.txt"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30)
    .CreateLogger();

try
{
    Log.Information("Iniciando SWDS-VERIFICACION-MIGRACION-BITEL...");

    var builder = Host.CreateApplicationBuilder(args);

    builder.Logging.ClearProviders();
    builder.Logging.AddSerilog();

    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "SWDS-VERIFICACION-MIGRACION-BITEL";
    });

    // ── Inyección de dependencias ─────────────────────────────────────────
    builder.Services.AddSingleton<Data.ConnectionFactory>();
    builder.Services.AddScoped<Data.IRepository, Data.Repository>();
    builder.Services.AddScoped<Logic.ICore, Logic.Core>();

    // BitelPortalService como Transient — cada vez que se pide se crea uno nuevo
    // igual que en el otro bot con GetRequiredService<BitelPortalService>()
    builder.Services.AddTransient<BitelPortalService>();

    // ── Quartz Scheduler ──────────────────────────────────────────────────
    builder.Services.AddQuartz(q =>
    {
        var jobKey = new JobKey("verificacion_migracion_bitel_job", "group_verificacion_migracion_bitel");

        q.AddJob<VerificacionMigracionBitelJob>(opts => opts
            .WithIdentity(jobKey)
            .DisallowConcurrentExecution());

        // Cada 30 minutos todos los días sin restricción de horario
        q.AddTrigger(opts => opts
            .ForJob(jobKey)
            .WithIdentity("verificacion_migracion_bitel_trigger", "group_verificacion_migracion_bitel")
            .StartNow()
            .WithSimpleSchedule(x => x
                .WithIntervalInMinutes(30)
                .RepeatForever()));
    });

    builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

    var host = builder.Build();
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "El host terminó inesperadamente.");
}
finally
{
    Log.CloseAndFlush();
}
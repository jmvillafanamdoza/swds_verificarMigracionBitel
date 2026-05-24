# swds_verificarMigracionBitel
Servicio automático de Verificación de Migración Bitel — .NET 8 Worker Service

## Arquitectura (igual al proyecto swds_registroMigracionBitel)

```
swds_verificarMigracionBitel/
├── Be/         → Entidades (VerificacionMigracion, VerificacionMigracionLog)
├── Da/         → Acceso a datos MySQL con Dapper (Repository, ConnectionFactory)
├── Bl/         → Lógica de negocio (Core, ICore)
├── Scheduler/  → Host del servicio
│   ├── Services/BitelPortalService.cs   ← Toda la lógica de navegación Playwright
│   ├── Jobs/VerificacionMigracionBitelJob.cs
│   ├── Program.cs
│   └── appsettings.json
└── swds_verificarMigracionBitel.sln
```

## Flujo del bot (6 pasos por número)

```
PASO 1 → Login en cm.bitel.com.pe:8046
PASO 2 → Navegar a: Gestión postpago > Cliente móvil postpago
PASO 3 → Marcar los 2 checkboxes de tratamiento de datos (automático)
PASO 4 → Ingresar ISDN y buscar
PASO 5 → Click en el número para cargar detalle del suscriptor
PASO 6 → Verificar:
           ✅ Estado bloqueo/terminación = "Normal"
           ✅ Fecha de firma = tiene valor (no vacía)
           ✅ Código de tienda = "VTPBC28"
           ⚠️  Si tienda diferente → guarda estado "T" en BD + log "migrado por otra tienda"
```

## Stored Procedures pendientes (TODO en Da/Repository.cs)

### SP 1 — Lista de verificaciones pendientes
- Reemplazar: SP_GET_LISTA_VERIFICACION_MIGRACION
- Debe retornar columna: celular

### SP 2 — Insertar resultado
- Reemplazar: SP_INSERT_LOG_VERIFICACION_MIGRACION
- Parámetros IN:  p_celular, p_estado, p_paso, p_mensaje, p_fecha_registro
- Parámetro OUT: p_mensaje_salida (debe contener "OK" si fue exitoso)
- Estado: "S"=OK, "T"=Otra tienda, "E"=Error

## Compilar y publicar

```bash
dotnet publish -c Release -o C:\Bot_verificacion\publish
```

## Instalar como Windows Service

```cmd
sc create "SWDS-VERIFICACION-MIGRACION-BITEL" binPath="C:\Bot_verificacion\publish\Aiwara.Scheduler.VerificacionMigracionBitel.exe" start=auto
sc start "SWDS-VERIFICACION-MIGRACION-BITEL"
```

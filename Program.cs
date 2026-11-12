using Microsoft.AspNetCore.Authentication.Cookies;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using PickingApp.Data;
using PickingApp.Middleware;
using PickingApp.Services;

// El builder reúne la configuración (appsettings.json) y el contenedor de servicios antes de crear la aplicación
var builder = WebApplication.CreateBuilder(args);

// Configuración de MySQL con Entity Framework Core.
// La cadena de conexión está en appsettings.json → ConnectionStrings:DefaultConnection.
// La versión del servidor se detecta una sola vez al arrancar: detectarla dentro de AddDbContext abría una
// conexión cada vez que se creaba el contexto y fallaba antes de llegar al controlador si MySQL estaba apagado.
// Si MySQL no responde al arrancar se asume MySQL 8, y cada petición muestra el aviso de base de datos no disponible.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
ServerVersion serverVersion;
try
{
    serverVersion = ServerVersion.AutoDetect(connectionString);
}
catch (MySqlException)
{
    serverVersion = new MySqlServerVersion(new Version(8, 0, 0));
}
// Cada petición HTTP recibe su propio ApplicationDbContext; los controladores lo piden en su constructor
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(connectionString, serverVersion));

// Servicios de la aplicación (inyección de dependencias).
// Cada interfaz se asocia a su implementación: quien pide IPickingEngine en su constructor recibe un PickingEngine.
builder.Services.AddHttpContextAccessor();
// Reloj del sistema: PickingEngine lo recibe para las reglas de jornada (las pruebas usan uno fijo)
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IExcelService, ExcelService>();
builder.Services.AddScoped<IPickingEngine, PickingEngine>();
builder.Services.AddSingleton<IReportService, ReportService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<DatosDemostracion>();

// Configuración de Autenticación por Cookies (RNF-06: cierre por inactividad).
// La duración real la fija el inicio de sesión con el parámetro MinutosInactividadSesion;
// este valor solo aplica si una sesión se emite sin expiración propia.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(ParametrosSistema.Numericos[ParametrosSistema.MinutosInactividadSesion].Defecto);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Events.OnValidatePrincipal = SesionUsuario.ValidarAsync;
    });

// Límite por IP para las acciones que envían o validan códigos de verificación por correo:
// frena la fuerza bruta repartida entre varias cuentas y el envío masivo de correos.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("CodigoVerificacion", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(15),
                QueueLimit = 0
            }));

    // Intentos de inicio de sesión por IP. El bloqueo por intentos fallidos es por cuenta y no aplica al
    // Administrador (para no dejar el sistema sin quien lo administre); este límite frena probar contraseñas sin fin.
    // shortcut: 20 cada 5 min por IP; si toda la bodega sale a internet por la misma IP y se queda corto, súbelo.
    options.AddPolicy("InicioSesion", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0
            }));

    // Límite general contra la saturación del servidor: por usuario con sesión, o por IP sin ella.
    // Holgado para el uso normal (navegar, filtrar, gráficas); los archivos estáticos no cuentan
    // porque UseStaticFiles responde antes de UseRateLimiter.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.User.Identity?.IsAuthenticated == true
                ? "usuario:" + httpContext.User.Identity.Name
                : "ip:" + (httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconocida"),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 300,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
        await context.HttpContext.Response.WriteAsync(
            "Demasiadas solicitudes seguidas desde esta conexión. Espera unos minutos e inténtalo de nuevo.", cancellationToken);
    };
});

// Soporte para MVC: controladores en Controllers/ y vistas Razor en Views/
builder.Services.AddControllersWithViews();

var app = builder.Build();

// Inicialización y siembra de base de datos (Seed): aplica las migraciones pendientes y crea los datos iniciales
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        await DbInitializer.SeedAsync(context, datosDeEjemplo: app.Environment.IsDevelopment());
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Ocurrió un error al inicializar la base de datos MySQL.");
    }
}

// Configuración del pipeline HTTP: cada petición pasa por estos middlewares en el orden en que se registran
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Una respuesta de error sin contenido (p. ej. el NotFound de un registro de otra bodega) se muestra con la
// pantalla Home/Estado y el mismo código; la dirección que pidió la persona no cambia.
app.UseStatusCodePagesWithReExecute("/Home/Estado/{0}");

app.UseHttpsRedirection();

// Cabeceras de seguridad en todas las respuestas: ninguna otra página puede mostrar PickFlow dentro de un marco
// (evita que la disfracen para engañar clics) y el navegador no adivina tipos de archivo distintos al declarado.
// Única excepción: la orden de picking (PedidosController.Imprimir) admite marcos de la misma app para imprimirse.
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

app.UseStaticFiles();

// Antes de UseRouting para que la página de aviso pase de nuevo por el enrutamiento
app.UseMiddleware<ErrorBaseDatosMiddleware>();

app.UseRouting();

// UseAuthentication lee la cookie de sesión y llena User (nombre, correo, rol);
// UseAuthorization aplica los [Authorize(Roles = ...)] de los controladores.
// UseRateLimiter va después de UseAuthentication para que el límite general sepa quién hace la petición.
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

// Ruta por defecto: /Controlador/Accion/id. Por ejemplo, /Pedidos/Detalle/5 llama a PedidosController.Detalle(5)
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
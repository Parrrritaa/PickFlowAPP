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
// AddScoped crea una instancia por petición HTTP.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IExcelService, ExcelService>();
builder.Services.AddScoped<IPickingEngine, PickingEngine>();
builder.Services.AddScoped<IReportService, ReportService>();
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
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
        await context.HttpContext.Response.WriteAsync(
            "Demasiados intentos desde esta conexión. Espera unos minutos e inténtalo de nuevo.", cancellationToken);
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
        await DbInitializer.SeedAsync(context);
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

app.UseHttpsRedirection();
app.UseStaticFiles();

// Antes de UseRouting para que la página de aviso pase de nuevo por el enrutamiento
app.UseMiddleware<ErrorBaseDatosMiddleware>();

app.UseRouting();
app.UseRateLimiter();

// UseAuthentication lee la cookie de sesión y llena User (nombre, correo, rol);
// UseAuthorization aplica los [Authorize(Roles = ...)] de los controladores
app.UseAuthentication();
app.UseAuthorization();

// Ruta por defecto: /Controlador/Accion/id. Por ejemplo, /Pedidos/Detalle/5 llama a PedidosController.Detalle(5)
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
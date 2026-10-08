using System.Data.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace PickingApp.Middleware
{
    // Atrapa los errores de base de datos que ningún controlador manejó y, en lugar de la traza técnica,
    // muestra al usuario una página que explica qué pasó y qué puede hacer. El detalle queda en el log.
    public class ErrorBaseDatosMiddleware
    {
        private const string RutaAviso = "/Home/ErrorBaseDatos";
        private readonly RequestDelegate _next;
        private readonly ILogger<ErrorBaseDatosMiddleware> _logger;

        public ErrorBaseDatosMiddleware(RequestDelegate next, ILogger<ErrorBaseDatosMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex) when (ErroresBaseDatos.EsErrorDeBaseDatos(ex) && !context.Response.HasStarted)
            {
                _logger.LogError(ex, "Error de base de datos al procesar {Metodo} {Ruta}.", context.Request.Method, context.Request.Path);
                await MostrarAvisoAsync(context, ex);
            }
        }

        // Vuelve a ejecutar el pipeline hacia la página de aviso, igual que UseExceptionHandler,
        // para que la vista se renderice con MVC sin redirigir ni perder el código de estado.
        private async Task MostrarAvisoAsync(HttpContext context, Exception ex)
        {
            var rutaOriginal = context.Request.Path;

            context.Features.Set<IExceptionHandlerPathFeature>(new ExceptionHandlerFeature { Error = ex, Path = rutaOriginal });
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.SetEndpoint(null);
            context.Features.Get<IRouteValuesFeature>()?.RouteValues.Clear();
            context.Request.Path = RutaAviso;

            try
            {
                await _next(context);
            }
            finally
            {
                context.Request.Path = rutaOriginal;
            }
        }
    }

    public static class ErroresBaseDatos
    {
        public static bool EsErrorDeBaseDatos(Exception ex) =>
            Cadena(ex).Any(e => e is DbException or DbUpdateException);

        // El servidor está apagado, no acepta las credenciales o la base de datos no existe:
        // en los tres casos la aplicación no puede leer ni guardar nada hasta que se corrija.
        public static bool EsFallaDeConexion(Exception ex) =>
            Cadena(ex).OfType<MySqlException>().Any(e => e.ErrorCode is
                MySqlErrorCode.UnableToConnectToHost or
                MySqlErrorCode.AccessDenied or
                MySqlErrorCode.UnknownDatabase);

        // Un índice único rechazó el registro (otro lo guardó primero)
        public static bool EsClaveDuplicada(Exception ex) =>
            Cadena(ex).OfType<MySqlException>().Any(e => e.ErrorCode == MySqlErrorCode.DuplicateKeyEntry);

        private static IEnumerable<Exception> Cadena(Exception ex)
        {
            for (Exception? actual = ex; actual != null; actual = actual.InnerException)
            {
                yield return actual;
            }
        }
    }
}

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PickingApp.Data;

namespace PickingApp.Services
{
    // Parámetros enteros que el Administrador edita en Configuración (CU-39): valor por defecto y rango permitido.
    // Se leen de la base de datos en cada uso, así un cambio aplica sin reiniciar la aplicación.
    // Un valor ausente o fuera de rango se ignora y se usa el valor por defecto.
    public static class ParametrosSistema
    {
        public const string MinutosInactividadSesion = "MinutosInactividadSesion";
        public const string MaxIntentosFallidos = "MaxIntentosFallidos";
        public const string MargenInicioMinutos = "MargenInicioMinutos";
        public const string MesesExperienciaAdvertencia = "MesesExperienciaAdvertencia";
        public const string MaxUbicacionesAdvertencia = "MaxUbicacionesAdvertencia";

        public sealed record Parametro(string Nombre, int Defecto, int Minimo, int Maximo);

        public static readonly IReadOnlyDictionary<string, Parametro> Enteros = new Dictionary<string, Parametro>
        {
            [MinutosInactividadSesion] = new("Cierre de sesión por inactividad", 10, 1, 60),
            [MaxIntentosFallidos] = new("Intentos fallidos antes de bloqueo", 5, 3, 10),
            [MargenInicioMinutos] = new("Margen de inicio", 2, 0, 15),
            [MesesExperienciaAdvertencia] = new("Meses de experiencia para advertencia", 6, 1, 24),
            [MaxUbicacionesAdvertencia] = new("Ubicaciones para advertencia", 10, 1, 50),
        };

        public static async Task<int> ObtenerAsync(ApplicationDbContext context, string clave)
        {
            var parametro = Enteros[clave];
            var texto = await context.ConfiguracionesSistema
                .Where(c => c.Clave == clave)
                .Select(c => c.Valor)
                .FirstOrDefaultAsync();
            return EsValido(parametro, texto, out var valor) ? valor : parametro.Defecto;
        }

        public static bool EsValido(Parametro parametro, string? texto, out int valor)
        {
            return int.TryParse(texto?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out valor)
                && valor >= parametro.Minimo && valor <= parametro.Maximo;
        }
    }
}

using Microsoft.EntityFrameworkCore;
using PickingApp.Data;

namespace PickingApp.Services
{
    // Reglas de datos del empleado. Se aplican al registrar o editar y otra vez al aprobar una solicitud,
    // porque entre ambos momentos otro registro pudo tomar la identificación o el correo, o cambiar el
    // máximo de horas de jornada.
    public static class ReglasEmpleado
    {
        public sealed record Error(string Campo, string Mensaje);

        public static string DescribirJornada(TimeSpan entrada, TimeSpan salida) =>
            $"{entrada:hh\\:mm} - {salida:hh\\:mm}";

        // Un turno que termina antes de empezar cruza la medianoche
        public static double DuracionJornadaHoras(TimeSpan entrada, TimeSpan salida)
        {
            var duracion = salida - entrada;
            if (duracion <= TimeSpan.Zero) duracion += TimeSpan.FromHours(24);
            return duracion.TotalHours;
        }

        public static string? NormalizarCorreo(string? correo) =>
            string.IsNullOrWhiteSpace(correo) ? null : correo.Trim().ToLower();

        // empleadoId: null al registrar; el Id del empleado al editar (su propia identificación y correo no cuentan)
        public static async Task<Error?> ValidarAsync(ApplicationDbContext context, string identificacion, string? correo,
            TimeSpan entrada, TimeSpan salida, int? empleadoId)
        {
            var maxHoras = await ParametrosSistema.ObtenerDecimalAsync(context, ParametrosSistema.MaxHorasJornada);
            var duracion = DuracionJornadaHoras(entrada, salida);
            if (duracion > maxHoras)
            {
                return new("HorarioSalida", $"La jornada laboral no puede superar las {maxHoras:0.#} horas (duración indicada: {duracion:0.#} horas).");
            }

            if (empleadoId == null && await context.Empleados.AnyAsync(e => e.Identificacion == identificacion))
            {
                return new("Identificacion", "Ya existe un empleado registrado con esta identificación.");
            }

            if (correo != null && !await CorreoDisponibleAsync(context, correo, empleadoId))
            {
                return new("Correo", "Ese correo ya está en uso por otro empleado o por un usuario del sistema.");
            }

            return null;
        }

        // Un correo no puede repetirse entre Empleados ni con ningún Usuario del sistema, aunque sean tablas distintas
        public static async Task<bool> CorreoDisponibleAsync(ApplicationDbContext context, string correo, int? empleadoIdExcluir)
        {
            return !await context.Empleados.AnyAsync(e => e.Correo == correo && e.Id != (empleadoIdExcluir ?? 0))
                && !await context.Usuarios.AnyAsync(u => u.Correo == correo);
        }
    }
}

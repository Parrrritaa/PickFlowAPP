using Microsoft.EntityFrameworkCore;
using PickingApp.Data;
using PickingApp.Models;

namespace PickingApp.Services
{
    // Evita dos solicitudes pendientes sobre el mismo empleado, ubicación o registro: al aprobar la segunda,
    // los valores "anteriores" que muestra ya no serían los reales. Los datos de cada solicitud están en JSON,
    // así que se leen las pendientes de esos tipos (son pocas) y se comparan en memoria.
    public static class SolicitudesPendientes
    {
        public static async Task<Solicitud?> DeEmpleadoAsync(ApplicationDbContext context, int empleadoId)
        {
            var pendientes = await PendientesAsync(context, TiposSolicitud.EdicionEmpleado, TiposSolicitud.InactivacionEmpleado, TiposSolicitud.Traslado);
            return pendientes.FirstOrDefault(s => s.TipoSolicitudId switch
            {
                TiposSolicitud.EdicionEmpleado => s.LeerDatos<SolicitudEdicionEmpleado>().NuevosDatos.Id == empleadoId,
                TiposSolicitud.InactivacionEmpleado => s.LeerDatos<SolicitudInactivacionEmpleado>().EmpleadoId == empleadoId,
                _ => s.LeerDatos<SolicitudTraslado>() is { EsEmpleado: true } traslado && traslado.PersonaId == empleadoId
            });
        }

        public static async Task<Solicitud?> DeTrasladoDeUsuarioAsync(ApplicationDbContext context, int usuarioId)
        {
            var pendientes = await PendientesAsync(context, TiposSolicitud.Traslado);
            return pendientes.FirstOrDefault(s => s.LeerDatos<SolicitudTraslado>() is { EsEmpleado: false } traslado && traslado.PersonaId == usuarioId);
        }

        public static async Task<Solicitud?> DeRegistroAsync(ApplicationDbContext context, string identificacion)
        {
            var pendientes = await PendientesAsync(context, TiposSolicitud.RegistroEmpleado);
            return pendientes.FirstOrDefault(s => s.LeerDatos<DatosEmpleadoSolicitud>().Identificacion.Trim() == identificacion);
        }

        public static async Task<Solicitud?> DeUbicacionAsync(ApplicationDbContext context, int ubicacionId)
        {
            var pendientes = await PendientesAsync(context, TiposSolicitud.EdicionUbicacion, TiposSolicitud.EliminacionUbicacion);
            return pendientes.FirstOrDefault(s => s.TipoSolicitudId == TiposSolicitud.EdicionUbicacion
                ? s.LeerDatos<SolicitudEdicionUbicacion>().UbicacionId == ubicacionId
                : s.LeerDatos<SolicitudEliminacionUbicacion>().UbicacionId == ubicacionId);
        }

        // sobre: "este empleado", "esta ubicación"...
        public static string Mensaje(Solicitud pendiente, string sobre) =>
            $"Ya hay una solicitud pendiente sobre {sobre} (SOL-{pendiente.Id}: {pendiente.NombreTipo}). " +
            "Espere a que la respondan antes de pedir otro cambio.";

        private static Task<List<Solicitud>> PendientesAsync(ApplicationDbContext context, params int[] tipos) =>
            context.Solicitudes
                .Where(s => s.Estado == EstadosSolicitud.Pendiente && tipos.Contains(s.TipoSolicitudId))
                .ToListAsync();
    }
}

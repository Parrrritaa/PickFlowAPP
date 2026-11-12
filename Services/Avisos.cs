using Microsoft.EntityFrameworkCore;
using PickingApp.Data;
using PickingApp.Models;

namespace PickingApp.Services
{
    // Crea las notificaciones de la campana. Un aviso a un rol se copia a cada usuario de ese rol, para que cada uno
    // la marque leída sin ocultársela a los demás. Solo se agregan al contexto: el SaveChanges de la acción las guarda.
    // El Administrador es global; los demás roles solo reciben avisos de su bodega (sin bodegaId no le llega a nadie).
    public static class Avisos
    {
        public static void AlUsuario(ApplicationDbContext context, int usuarioId, string titulo, string mensaje, string tipo, string? enlace = null) =>
            context.Notificaciones.Add(Crear(usuarioId, titulo, mensaje, tipo, enlace));

        public static async Task AlRolAsync(ApplicationDbContext context, int rolId, string titulo, string mensaje, string tipo,
            string? enlace = null, int? bodegaId = null)
        {
            var destino = context.Usuarios.Where(u => u.RolId == rolId);
            if (rolId != Roles.IdAdministrador) destino = destino.Where(u => u.BodegaId != null && u.BodegaId == bodegaId);
            var usuarios = await destino.Select(u => u.Id).ToListAsync();
            context.Notificaciones.AddRange(usuarios.Select(id => Crear(id, titulo, mensaje, tipo, enlace)));
        }

        private static Notificacion Crear(int usuarioId, string titulo, string mensaje, string tipo, string? enlace) => new()
        {
            UsuarioDestinoId = usuarioId,
            Titulo = titulo,
            Mensaje = mensaje,
            Tipo = tipo,
            Enlace = enlace,
            FechaCreacion = DateTime.Now
        };
    }
}

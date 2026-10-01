using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace PickingApp.Models
{
    // Solicitud de un Supervisor o Auxiliar que el Administrador debe aprobar o rechazar.
    // Los datos propuestos viajan en DatosJson y solo se aplican al aprobarla (SolicitudesController.Aprobar).
    public class Solicitud
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int TipoSolicitudId { get; set; } // Id fijo de TiposSolicitud

        // Se carga siempre junto con la solicitud (AutoInclude en ApplicationDbContext)
        [ForeignKey("TipoSolicitudId")]
        public virtual TipoSolicitud? TipoSolicitud { get; set; }

        [NotMapped]
        public string NombreTipo => TipoSolicitud?.Nombre ?? string.Empty;

        [Required]
        public int UsuarioSolicitanteId { get; set; }

        [ForeignKey("UsuarioSolicitanteId")]
        public virtual Usuario? UsuarioSolicitante { get; set; }

        [Required]
        public string Estado { get; set; } = EstadosSolicitud.Pendiente; // Valores en EstadosSolicitud

        public DateTime FechaSolicitud { get; set; } = DateTime.Now;

        [Required]
        public string DatosJson { get; set; } = "{}";

        public int? UsuarioRespondeId { get; set; }

        [ForeignKey("UsuarioRespondeId")]
        public virtual Usuario? UsuarioResponde { get; set; }

        public DateTime? FechaRespuesta { get; set; }

        [StringLength(500)]
        public string? MotivoRechazo { get; set; }

        // T es la clase de SolicitudDatos.cs que corresponde a TipoSolicitud
        public T LeerDatos<T>() where T : new() => JsonSerializer.Deserialize<T>(DatosJson) ?? new T();

        public static Solicitud Nueva(int tipoSolicitudId, int usuarioSolicitanteId, object datos) => new()
        {
            TipoSolicitudId = tipoSolicitudId,
            UsuarioSolicitanteId = usuarioSolicitanteId,
            Estado = EstadosSolicitud.Pendiente,
            FechaSolicitud = DateTime.Now,
            DatosJson = JsonSerializer.Serialize(datos)
        };
    }
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PickingApp.Models
{
    // Catálogo de observaciones para responder solicitudes. Cada una aplica a un tipo de solicitud y a una decisión
    // (aprobar o rechazar): la pantalla de revisión solo ofrece las que corresponden. No se editan ni se borran,
    // se desactivan, para que una solicitud ya respondida siga mostrando el texto que se eligió.
    public class ObservacionSolicitud
    {
        // Valor de la opción "Otro" en la lista: no es una fila del catálogo, abre el cuadro de texto libre
        public const string ValorOtro = "otro";

        public const int MinimoTextoLibre = 10;
        public const int MinimoLetrasTextoLibre = 5;
        public const string MensajeTextoLibre = "La observación debe tener al menos 10 caracteres y estar escrita con palabras.";

        [Key]
        public int Id { get; set; }

        [Required]
        public int TipoSolicitudId { get; set; }

        [ForeignKey("TipoSolicitudId")]
        public virtual TipoSolicitud? TipoSolicitud { get; set; }

        [Required]
        public string Decision { get; set; } = DecisionesSolicitud.Rechazar; // Valores en DecisionesSolicitud

        [Required]
        [StringLength(150)]
        public string Texto { get; set; } = string.Empty;

        public bool Activa { get; set; } = true;

        [NotMapped]
        public string NombreTipo => TipoSolicitud?.Nombre ?? string.Empty;

        // Texto libre de "Otro": evita que se responda con un punto o con caracteres sueltos.
        // shortcut: no detecta frases sin sentido hechas con letras; se acepta porque la lista cubre los casos comunes
        public static bool TextoLibreValido(string? texto)
        {
            texto = texto?.Trim() ?? string.Empty;
            return texto.Length >= MinimoTextoLibre && texto.Count(char.IsLetter) >= MinimoLetrasTextoLibre;
        }
    }

    public static class DecisionesSolicitud
    {
        public const string Aprobar = "Aprobar";
        public const string Rechazar = "Rechazar";

        public static readonly string[] Todas = { Aprobar, Rechazar };
    }

    // Observaciones iniciales por tipo de solicitud. Se siembran en ApplicationDbContext; el Administrador
    // agrega o desactiva las demás desde Sistema > Observaciones.
    public static class ObservacionesIniciales
    {
        public static readonly ObservacionSolicitud[] Catalogo =
        {
            Nueva(1, TiposSolicitud.RegistroEmpleado, DecisionesSolicitud.Rechazar, "Identificación duplicada"),
            Nueva(2, TiposSolicitud.RegistroEmpleado, DecisionesSolicitud.Rechazar, "Datos incompletos o incorrectos"),
            Nueva(3, TiposSolicitud.RegistroEmpleado, DecisionesSolicitud.Rechazar, "No se requiere personal adicional"),
            Nueva(4, TiposSolicitud.RegistroEmpleado, DecisionesSolicitud.Aprobar, "Datos verificados"),
            Nueva(5, TiposSolicitud.RegistroEmpleado, DecisionesSolicitud.Aprobar, "Incorporación autorizada"),

            Nueva(6, TiposSolicitud.EdicionEmpleado, DecisionesSolicitud.Rechazar, "Los cambios no están justificados"),
            Nueva(7, TiposSolicitud.EdicionEmpleado, DecisionesSolicitud.Rechazar, "Datos incorrectos"),
            Nueva(8, TiposSolicitud.EdicionEmpleado, DecisionesSolicitud.Rechazar, "Afecta pedidos en curso"),
            Nueva(9, TiposSolicitud.EdicionEmpleado, DecisionesSolicitud.Aprobar, "Cambios verificados"),

            Nueva(10, TiposSolicitud.InactivacionEmpleado, DecisionesSolicitud.Rechazar, "Motivo insuficiente"),
            Nueva(11, TiposSolicitud.InactivacionEmpleado, DecisionesSolicitud.Rechazar, "Tiene pedidos en curso"),
            Nueva(12, TiposSolicitud.InactivacionEmpleado, DecisionesSolicitud.Rechazar, "Requiere revisión con Recursos Humanos"),
            Nueva(13, TiposSolicitud.InactivacionEmpleado, DecisionesSolicitud.Aprobar, "Inactivación justificada"),

            Nueva(14, TiposSolicitud.EdicionUbicacion, DecisionesSolicitud.Rechazar, "Ubicación en uso por pedidos activos"),
            Nueva(15, TiposSolicitud.EdicionUbicacion, DecisionesSolicitud.Rechazar, "Datos de ubicación incorrectos"),
            Nueva(16, TiposSolicitud.EdicionUbicacion, DecisionesSolicitud.Rechazar, "El producto no corresponde"),
            Nueva(17, TiposSolicitud.EdicionUbicacion, DecisionesSolicitud.Aprobar, "Cambios verificados"),

            Nueva(18, TiposSolicitud.EliminacionUbicacion, DecisionesSolicitud.Rechazar, "Ubicación en uso"),
            Nueva(19, TiposSolicitud.EliminacionUbicacion, DecisionesSolicitud.Rechazar, "Tiene producto asignado"),
            Nueva(20, TiposSolicitud.EliminacionUbicacion, DecisionesSolicitud.Rechazar, "Se recomienda inactivar en vez de eliminar"),
            Nueva(21, TiposSolicitud.EliminacionUbicacion, DecisionesSolicitud.Aprobar, "Eliminación verificada"),

            // Id altos: las bases en uso pueden tener ya observaciones agregadas por el Administrador con los siguientes
            Nueva(1001, TiposSolicitud.Traslado, DecisionesSolicitud.Rechazar, "Motivo insuficiente"),
            Nueva(1002, TiposSolicitud.Traslado, DecisionesSolicitud.Rechazar, "La bodega de destino no requiere personal"),
            Nueva(1003, TiposSolicitud.Traslado, DecisionesSolicitud.Rechazar, "Se necesita en la bodega actual"),
            Nueva(1004, TiposSolicitud.Traslado, DecisionesSolicitud.Aprobar, "Traslado autorizado")
        };

        private static ObservacionSolicitud Nueva(int id, int tipo, string decision, string texto) =>
            new() { Id = id, TipoSolicitudId = tipo, Decision = decision, Texto = texto, Activa = true };
    }
}

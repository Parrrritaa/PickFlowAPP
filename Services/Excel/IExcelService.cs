using PickingApp.Models;

namespace PickingApp.Services
{
    // Resultado de validar el Excel: errores por fila y pedidos listos para guardar
    public class ExcelValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
        public List<Pedido> PedidosValidados { get; set; } = new List<Pedido>();
        public int TotalFilas { get; set; }
        public int TotalPedidos { get; set; }

        // Detalle por verificación (resultado de validación de CU-23). Las filas con un error
        // no siguen a las verificaciones siguientes, así que estas solo cubren las filas que llegaron.
        public bool ArchivoLegible { get; set; }
        public bool EstructuraValida { get; set; }
        public int FilasConCantidadInvalida { get; set; }
        public int FilasConBodegaNoRegistrada { get; set; }
        // Filas de una bodega que no es la del Supervisor que carga el archivo
        public int FilasDeOtraBodega { get; set; }
        public int FilasConProductoNoRegistrado { get; set; }
        public int FilasConPedidoDuplicado { get; set; }

        // Un nombre distinto al registrado no es un error del archivo: el usuario confirma en pantalla (Incidencias)
        public int FilasConNombreDistinto { get; set; }
        public List<IncidenciaExcel> Incidencias { get; set; } = new();

        // Clave de la incidencia → por qué no se pudo aplicar la decisión enviada
        public Dictionary<string, string> ErroresResolucion { get; set; } = new();

        // Productos que la bodega del pedido no tiene: la línea no entra y el pedido queda con una novedad
        // "No disponible". No impiden la carga; se muestran como aviso antes de confirmar.
        public int FilasNoDisponibles { get; set; }
        public List<string> NoDisponibles { get; set; } = new();
    }

    public static class TiposIncidenciaExcel
    {
        // El producto existe, pero el nombre del archivo no es el registrado para ese código
        public const string NombreDistinto = "nombre-distinto";
    }

    // Filas del Excel con el mismo producto y el mismo nombre distinto al registrado; se confirman con una sola decisión.
    public class IncidenciaExcel
    {
        public string Clave { get; set; } = string.Empty;
        public string Tipo { get; set; } = string.Empty;
        public string Titulo { get; set; } = string.Empty;
        public string Detalle { get; set; } = string.Empty;

        public string CodigoProducto { get; set; } = string.Empty;
        public string NombreProducto { get; set; } = string.Empty;

        public List<int> Filas { get; set; } = new();
        public List<string> Pedidos { get; set; } = new();
    }

    // Decisión del usuario sobre una incidencia (campos resoluciones[i].* del formulario de CargarExcel)
    public class ResolucionExcel
    {
        public const string OpcionNombreRegistrado = "nombre-registrado";

        public string? Clave { get; set; }
        public string? Opcion { get; set; }
    }

    // Contrato del servicio de Excel; la implementación está en ExcelService.cs
    public interface IExcelService
    {
        // resoluciones: decisiones sobre las incidencias (vacío en la primera validación).
        // bodegaDelUsuario: la del Supervisor, que solo carga pedidos de su bodega; null para el Administrador.
        Task<ExcelValidationResult> ValidarArchivoExcelAsync(Stream stream, IReadOnlyList<ResolucionExcel> resoluciones, int? bodegaDelUsuario);
    }
}

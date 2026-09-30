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
        public int FilasConPedidoDuplicado { get; set; }

        // Filas con un problema de producto o ubicación que el usuario resuelve en pantalla (Incidencias):
        // no son errores del archivo, así que siguen a las verificaciones siguientes.
        public int FilasConProductoNoRegistrado { get; set; }
        public int FilasConUbicacionNoRegistrada { get; set; }
        public int FilasConProductoIncorrecto { get; set; }

        public List<IncidenciaExcel> Incidencias { get; set; } = new();

        // Clave de la incidencia → por qué no se pudo aplicar la decisión enviada
        public Dictionary<string, string> ErroresResolucion { get; set; } = new();

        // Productos y ubicaciones que se crean o modifican al guardar, para la auditoría
        public List<string> Cambios { get; set; } = new();
    }

    public static class TiposIncidenciaExcel
    {
        public const string ProductoNoRegistrado = "producto-no-registrado";
        public const string UbicacionNoRegistrada = "ubicacion-no-registrada";
        public const string UbicacionVacia = "ubicacion-vacia";
        public const string ProductoIncorrecto = "producto-incorrecto";
        public const string UbicacionInactiva = "ubicacion-inactiva";
    }

    // Filas del Excel que comparten un mismo problema de producto o ubicación y se resuelven con una sola decisión.
    // Un producto no registrado agrupa todas sus filas; los demás casos, las filas del mismo producto y ubicación.
    public class IncidenciaExcel
    {
        public string Clave { get; set; } = string.Empty;
        public string Tipo { get; set; } = string.Empty;
        public string Titulo { get; set; } = string.Empty;
        public string Detalle { get; set; } = string.Empty;

        public string CodigoProducto { get; set; } = string.Empty;
        public string NombreProducto { get; set; } = string.Empty;

        // Ubicación escrita en el Excel. Pasillo/Estante/Nivel quedan vacíos si no eran números.
        public int BodegaId { get; set; }
        public string Pasillo { get; set; } = string.Empty;
        public string Estante { get; set; } = string.Empty;
        public string Nivel { get; set; } = string.Empty;
        public bool UbicacionExiste { get; set; }

        // "Corregir" deja lista la ubicación del Excel: le asigna el producto si está vacía y la activa si está inactiva.
        // Modifica una ubicación existente, por eso solo lo hace el Administrador.
        public string? TextoCorregir { get; set; }

        // Ubicaciones activas del producto donde se pueden recoger estas filas
        public List<OpcionUbicacionExcel> Ubicaciones { get; set; } = new();

        public List<int> Filas { get; set; } = new();
        public List<string> Pedidos { get; set; } = new();
    }

    public class OpcionUbicacionExcel
    {
        public int Id { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public int BodegaId { get; set; }
        public string Bodega { get; set; } = string.Empty;
    }

    // Decisión del usuario sobre una incidencia (campos resoluciones[i].* del formulario de CargarExcel)
    public class ResolucionExcel
    {
        public const string OpcionNueva = "nueva";
        public const string OpcionCorregir = "corregir";

        public string? Clave { get; set; }

        // Id de una ubicación del producto, "nueva" o "corregir"
        public string? Opcion { get; set; }

        // Ubicación nueva
        public int? BodegaId { get; set; }
        public string? Pasillo { get; set; }
        public string? Estante { get; set; }
        public string? Nivel { get; set; }

        // Producto no registrado
        public string? NombreProducto { get; set; }
        public string? CategoriaProductoId { get; set; }
        public string? UnidadesPorCaja { get; set; }
    }

    // Contrato del servicio de Excel; la implementación está en ExcelService.cs
    public interface IExcelService
    {
        // resoluciones: decisiones sobre las incidencias (vacío en la primera validación).
        // puedeEditarUbicaciones: permite la opción "corregir" (Administrador).
        Task<ExcelValidationResult> ValidarArchivoExcelAsync(Stream stream, IReadOnlyList<ResolucionExcel> resoluciones, bool puedeEditarUbicaciones);
    }
}

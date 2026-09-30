namespace PickingApp.Models.ViewModels
{
    // Datos del bloque "Producto" en Crear/Editar ubicación (Views/Bodegas/_SeleccionProducto.cshtml).
    // La ubicación usa un producto existente o registra uno nuevo; nunca renombra uno existente.
    public class SeleccionProductoViewModel
    {
        public const string ModoExistente = "existente";
        public const string ModoNuevo = "nuevo";

        public string Modo { get; set; } = ModoExistente;
        public int? ProductoId { get; set; }

        // Campos del modo "nuevo", tal como los escribió el usuario (se devuelven si hay errores)
        public string? NumeroProducto { get; set; }
        public string? NombreProducto { get; set; }
        public string? CategoriaProductoId { get; set; }
        public string? UnidadesPorCaja { get; set; } = "1";

        public List<ProductoOpcion> Productos { get; set; } = new();
        public List<CategoriaProducto> Categorias { get; set; } = new();
        public string NombreCategoriaPredeterminada { get; set; } = "por defecto";
    }

    public class ProductoOpcion
    {
        public int Id { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string? Categoria { get; set; }
        public int UnidadesPorCaja { get; set; }
        public int Ubicaciones { get; set; }
    }
}

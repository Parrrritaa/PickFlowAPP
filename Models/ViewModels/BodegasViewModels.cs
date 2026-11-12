namespace PickingApp.Models.ViewModels
{
    // Views/Bodegas/Index.cshtml
    public class BodegasIndexViewModel
    {
        public List<Bodega> Bodegas { get; init; } = new();

        // Cantidad de ubicaciones de cada bodega, por Id de bodega
        public Dictionary<int, int> UbicacionesPorBodega { get; init; } = new();
        public string? FiltroEstado { get; init; }

        // Búsqueda de un producto: null si no se buscó nada
        public string? BusquedaProducto { get; init; }
        public List<Ubicacion>? ResultadosProducto { get; init; }
    }

    // Views/Bodegas/Detalle.cshtml: la bodega elegida (con sus ubicaciones ya filtradas) y la lista de todas a la izquierda
    public class BodegaDetalleViewModel
    {
        public Bodega Bodega { get; init; } = null!;
        public List<Bodega> TodasBodegas { get; init; } = new();
        public Dictionary<int, int> UbicacionesPorBodega { get; init; } = new();
        public string? FiltroUbicacion { get; init; }
        public string? FiltroProducto { get; init; }
    }

    // Views/Bodegas/CrearUbicacion.cshtml y EditarUbicacion.cshtml.
    // Los campos de la ubicación viajan como "Ubicacion.Pasillo", "Ubicacion.Estante"...; los del producto como "seleccion.*".
    public class UbicacionFormViewModel
    {
        public Ubicacion Ubicacion { get; init; } = null!;
        public Bodega Bodega { get; init; } = null!;
        public SeleccionProductoViewModel Seleccion { get; init; } = new();
    }
}

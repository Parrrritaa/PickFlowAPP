using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

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

    // Views/Bodegas/Crear.cshtml y Editar.cshtml: solo lo que el formulario envía. El Id llega por la ruta,
    // y Activa es anulable para que un formulario sin estado dé error en vez de guardar "inactiva".
    public class BodegaCampos
    {
        [Required(ErrorMessage = "El nombre de la bodega es obligatorio")]
        [StringLength(100)]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(250)]
        public string? Descripcion { get; set; }

        [Required(ErrorMessage = "Elija el estado de la bodega")]
        public bool? Activa { get; set; }

        // Solo para mostrarlo al editar: se genera al crear y no se puede cambiar
        [BindNever]
        public string Codigo { get; set; } = string.Empty;

        public static BodegaCampos De(Bodega bodega) => new()
        {
            Nombre = bodega.Nombre, Descripcion = bodega.Descripcion, Activa = bodega.Activa, Codigo = bodega.Codigo,
        };

        public void AplicarA(Bodega bodega)
        {
            bodega.Nombre = Nombre.Trim();
            bodega.Descripcion = Descripcion?.Trim();
            bodega.Activa = Activa!.Value;
        }
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

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
    // Id y CodigoUbicacion solo se muestran al editar (0 y vacío al crear); el POST recibe UbicacionCampos.
    public class UbicacionFormViewModel
    {
        public UbicacionCampos Ubicacion { get; init; } = null!;
        public Bodega Bodega { get; init; } = null!;
        public SeleccionProductoViewModel Seleccion { get; init; } = new();
        public int Id { get; init; }
        public string CodigoUbicacion { get; init; } = string.Empty;
    }

    // Lo que envía el formulario de ubicación. La bodega y el Id llegan por la ruta, y Activa es anulable
    // para que un formulario sin estado dé error en vez de guardar la ubicación como inactiva.
    public class UbicacionCampos
    {
        [Required(ErrorMessage = "El pasillo es obligatorio")]
        [StringLength(20)]
        [RegularExpression(@"^\d{1,9}$", ErrorMessage = "El pasillo solo debe contener números (hasta 9 dígitos)")]
        public string Pasillo { get; set; } = string.Empty;

        [Required(ErrorMessage = "El estante es obligatorio")]
        [StringLength(20)]
        [RegularExpression(@"^\d{1,9}$", ErrorMessage = "El estante solo debe contener números (hasta 9 dígitos)")]
        public string Estante { get; set; } = string.Empty;

        [Required(ErrorMessage = "El nivel es obligatorio")]
        [StringLength(20)]
        [RegularExpression(@"^\d{1,9}$", ErrorMessage = "El nivel solo debe contener números (hasta 9 dígitos)")]
        public string Nivel { get; set; } = string.Empty;

        [Required(ErrorMessage = "Elija el estado de la ubicación")]
        public bool? Activa { get; set; }

        public static UbicacionCampos De(Ubicacion ubicacion) => new()
        {
            Pasillo = ubicacion.Pasillo, Estante = ubicacion.Estante, Nivel = ubicacion.Nivel, Activa = ubicacion.Activa,
        };
    }
}

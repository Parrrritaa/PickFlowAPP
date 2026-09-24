using ClosedXML.Excel;
using PickingApp.Models;

namespace PickingApp.Services
{
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
        public int FilasConUbicacionNoRegistrada { get; set; }
        public int FilasConProductoIncorrecto { get; set; }
        public int FilasConPedidoDuplicado { get; set; }
    }

    public interface IExcelService
    {
        Task<ExcelValidationResult> ValidarArchivoExcelAsync(Stream stream);
        byte[] GenerarPlantillaExcel();
    }
}

namespace PickingApp.Models.ViewModels
{
    // Página actual de una lista larga (pedidos, LOG). La consulta trae solo esa página con Skip/Take
    // y Views/Shared/_Paginacion.cshtml dibuja los enlaces conservando los filtros de la URL.
    public class Paginacion
    {
        public int Pagina { get; }
        public int TamanoPagina { get; }

        // Filas por página que calculó el navegador para la pantalla del usuario (site.js guarda la cookie
        // pf_filas_<lista>); 8 mientras no la haya. Se acota para que una cookie manipulada no pida miles de filas.
        public static int FilasPorPantalla(Microsoft.AspNetCore.Http.HttpRequest request, string lista) =>
            int.TryParse(request.Cookies["pf_filas_" + lista], out var filas) ? Math.Clamp(filas, 4, 50) : 8;
        public int TotalRegistros { get; }

        public int TotalPaginas => Math.Max(1, (TotalRegistros + TamanoPagina - 1) / TamanoPagina);
        public int Saltar => (Pagina - 1) * TamanoPagina;

        // Registros que se ven en esta página, contados desde 1 ("21 a 40 de 75")
        public int Desde => TotalRegistros == 0 ? 0 : Saltar + 1;
        public int Hasta => Math.Min(Saltar + TamanoPagina, TotalRegistros);

        // Una página fuera de rango (?pagina=0, o una que dejó de existir al filtrar) se ajusta a la más cercana
        public Paginacion(int? pagina, int tamanoPagina, int totalRegistros)
        {
            TamanoPagina = tamanoPagina;
            TotalRegistros = totalRegistros;
            Pagina = Math.Clamp(pagina ?? 1, 1, TotalPaginas);
        }
    }
}

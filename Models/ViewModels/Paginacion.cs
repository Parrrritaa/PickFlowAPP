namespace PickingApp.Models.ViewModels
{
    // Página actual de una lista larga (pedidos, LOG). La consulta trae solo esa página con Skip/Take
    // y Views/Shared/_Paginacion.cshtml dibuja los enlaces conservando los filtros de la URL.
    public class Paginacion
    {
        public int Pagina { get; }
        public int TamanoPagina { get; }
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

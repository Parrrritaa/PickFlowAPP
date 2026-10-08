using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace PickingApp.Services
{
    public static class ModelStateExtensions
    {
        // Para las acciones que responden con una alerta (TempData) en vez de volver a mostrar el formulario
        public static string PrimerError(this ModelStateDictionary modelState) =>
            modelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault()
            ?? "Revise los datos enviados.";
    }
}

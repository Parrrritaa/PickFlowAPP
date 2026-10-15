using PickingApp.Services;

namespace PickingApp.Tests
{
    // Plantilla HTML del correo con el código de verificación
    public class CorreoTests
    {
        // La plantilla real, buscada subiendo desde bin/ hasta la carpeta del proyecto
        private static string PlantillaReal()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Plantillas"))) dir = dir.Parent;
            return File.ReadAllText(Path.Combine(dir!.FullName, "Plantillas", "CorreoCodigoVerificacion.html"));
        }

        [Fact]
        public void RellenaTodosLosMarcadores()
        {
            var html = EmailService.ConstruirHtml(PlantillaReal(), "Recupera tu contraseña", "Intro", "Laura Gómez", "482913",
                "laura@empresa.com", new DateTime(2026, 10, 15, 14, 52, 0));

            Assert.DoesNotContain("{{", html);
            Assert.Contains("482913", html);
            Assert.Contains("15/10/2026", html);
            Assert.Contains("2026 PickFlow", html);
        }

        [Fact]
        public void CodificaElNombreParaQueNoSeInyecteHtml()
        {
            var html = EmailService.ConstruirHtml("{{NOMBRE}}", "", "", "<script>alert(1)</script>", "", "", DateTime.Now);

            Assert.Equal("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        }
    }
}

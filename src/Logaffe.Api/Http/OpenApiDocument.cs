using Microsoft.AspNetCore.OpenApi;

namespace Logaffe.Api.Http;

/// <summary>
/// The document is checked in and compared against the running installation by
/// CI, so it has to describe the shape of the API and nothing about the machine
/// that happened to serve it.
/// </summary>
public static class OpenApiDocument
{
    public static IServiceCollection AddLogaffeOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info.Title = "logaffe";
            document.Info.Description =
                "The read and write surface of one logaffe installation. "
                + "Every installation is at its own address.";

            // Whoever generated it was at some address; nobody else is.
            document.Servers?.Clear();

            return Task.CompletedTask;
        }));
}

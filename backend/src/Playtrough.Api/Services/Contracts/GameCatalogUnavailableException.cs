namespace Playtrough.Api.Services.Contracts;

// Never attach the upstream exception: it may contain the request URL and API key.
public sealed class GameCatalogUnavailableException() : Exception(
    "El catálogo de juegos no está disponible. Intentá nuevamente más tarde.");

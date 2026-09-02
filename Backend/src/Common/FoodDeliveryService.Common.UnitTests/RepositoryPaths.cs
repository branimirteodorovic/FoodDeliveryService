namespace FoodDeliveryService.Common.UnitTests;

/// <summary>
/// Several suites here assert things about files that live in the repository rather than in the test
/// output — Grafana dashboards, the Gateway's routing configuration, the Kubernetes manifests. They
/// all need the same answer to the same question: where is <c>Backend/</c> from here?
/// </summary>
internal static class RepositoryPaths
{
    /// <summary>
    /// Walks up from the assembly location to the one directory that owns both
    /// <c>docker-compose.yml</c> and <c>docker/</c>, then appends <paramref name="segments"/>.
    /// </summary>
    internal static string Backend(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "docker-compose.yml")) &&
                Directory.Exists(Path.Combine(directory.FullName, "docker")))
            {
                return Path.Combine([directory.FullName, .. segments]);
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"No Backend root (docker-compose.yml + docker/) above {AppContext.BaseDirectory}.");
    }
}

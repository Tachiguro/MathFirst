namespace MathFirst.Application.Lifecycle;

using System.Threading;
using System.Threading.Tasks;

public interface IAppResetCoordinator
{
    Task ExecuteFullResetAsync(
        CancellationToken cancellationToken = default);
}

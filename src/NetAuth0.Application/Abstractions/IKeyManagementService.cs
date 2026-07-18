namespace NetAuth0.Application.Abstractions;

public interface IKeyManagementService
{
    Task<string> RotateAsync(CancellationToken cancellationToken);
}

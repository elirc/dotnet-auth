namespace NetAuth0.Application.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total)
{
    public static async Task<PagedResult<T>> CreateAsync(IQueryable<T> query, int page, int pageSize, Func<IQueryable<T>, Task<List<T>>> materialize, Func<IQueryable<T>, Task<int>> count)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await count(query);
        var items = await materialize(query.Skip((page - 1) * pageSize).Take(pageSize));
        return new PagedResult<T>(items, page, pageSize, total);
    }
}

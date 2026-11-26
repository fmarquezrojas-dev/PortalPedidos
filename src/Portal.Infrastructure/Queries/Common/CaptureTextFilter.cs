using Portal.Domain.Entities;

namespace Portal.Infrastructure.Queries.Common;

internal static class CaptureTextFilter
{
    public static IQueryable<Customer> Matching(this IQueryable<Customer> query, string term) =>
        query.Where(c => c.ExternalCode.Contains(term) || c.Name.Contains(term));

    public static IQueryable<Product> Matching(this IQueryable<Product> query, string term) =>
        query.Where(p => p.ExternalCode.Contains(term) || p.Name.Contains(term));
}

using Microsoft.EntityFrameworkCore;

namespace MinimalApi.React;

/// <summary>The sample's cursor feed, seeded from the same posts as the numbered feed.</summary>
public sealed class FeedDbContext(DbContextOptions<FeedDbContext> options) : DbContext(options)
{
    /// <summary>The feed posts.</summary>
    public DbSet<Post> Posts => Set<Post>();
}

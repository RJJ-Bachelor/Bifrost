using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EirService.KnowledgeBase.Infrastructure.Persistence;

public sealed class EirKnowledgeBaseDbContextFactory : IDesignTimeDbContextFactory<EirKnowledgeBaseDbContext>
{
    public EirKnowledgeBaseDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<EirKnowledgeBaseDbContext>().UseNpgsql("Host=localhost;Database=eir;Username=postgres",
            options => options.MigrationsHistoryTable("__EFMigrationsHistory", "knowledgebase")).Options);
}

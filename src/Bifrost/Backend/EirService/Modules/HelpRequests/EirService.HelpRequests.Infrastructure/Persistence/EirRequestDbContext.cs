using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure.Persistence.Outbox;

namespace EirService.HelpRequests.Infrastructure.Persistence;

public sealed class EirRequestDbContext(DbContextOptions<EirRequestDbContext> options) : DbContext(options)
{
    public DbSet<HelpRequest> Requests => Set<HelpRequest>();
    public DbSet<HelpRequestMessage> Messages => Set<HelpRequestMessage>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("helprequests");
        modelBuilder.AddOutbox("helprequests");
        modelBuilder.Entity<HelpRequest>(entity =>
        {
            entity.ToTable("HelpRequest");
            entity.HasKey(request => request.Id);
            entity.Property(request => request.Id).ValueGeneratedNever();
            // A BEFORE INSERT trigger allocates the number within the insert transaction.
            entity.Property(request => request.RequestNumber).HasDefaultValue(0).ValueGeneratedOnAdd();
            entity.Property(request => request.StudentId).HasMaxLength(100).IsRequired();
            entity.Property(request => request.AssignedTeacherId).HasMaxLength(100);
            entity.Property(request => request.HelpType).HasConversion<string>().HasMaxLength(20);
            entity.Property(request => request.Status).HasConversion<string>().HasMaxLength(30);
            entity.Property<uint>("xmin").IsRowVersion();
            entity.Ignore(request => request.IsOpen);
            entity.Ignore(request => request.DisplayNumber);
            entity.HasIndex(request => new { request.SessionId, request.Generation, request.RequestNumber }).IsUnique();
            entity.HasMany(request => request.Messages).WithOne().HasForeignKey(message => message.HelpRequestId).OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(request => request.Messages).HasField("messages").UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.HasIndex(request => request.StudentId);
            entity.HasIndex(request => new { request.SessionId, request.Generation, request.ReturnedToQueueAt, request.QueuedAt, request.Id })
                .IsDescending(false, false, true, false, false)
                .HasFilter("\"Status\" = 'Pending'");
            entity.HasIndex(request => new { request.SessionId, request.Generation, request.StudentId })
                .IsUnique().HasDatabaseName("IX_HelpRequest_OneOpenPerStudent")
                .HasFilter("\"Status\" IN ('Pending', 'Active', 'AwaitingConfirmation')");
        });
        modelBuilder.SharedTypeEntity<Dictionary<string, object>>("HelpRequestNumberCounter", entity =>
        {
            entity.ToTable("HelpRequestNumberCounter");
            entity.IndexerProperty<Guid>("SessionId");
            entity.IndexerProperty<int>("Generation");
            entity.IndexerProperty<int>("LastNumber");
            entity.HasKey("SessionId", "Generation");
        });
        modelBuilder.Entity<HelpRequestMessage>(entity =>
        {
            entity.ToTable("HelpRequestMessage");
            entity.HasKey(message => message.Id);
            entity.Property(message => message.Id).ValueGeneratedNever();
            entity.Property(message => message.AuthorId).HasMaxLength(100).IsRequired();
            entity.Property(message => message.AuthorRole).HasConversion<string>().HasMaxLength(20);
            entity.Property(message => message.Text).HasConversion(text => text.Value, value => new MessageText(value))
                .HasMaxLength(MessageText.MaxLength).IsRequired();
            entity.HasIndex(message => new { message.HelpRequestId, message.SequenceNumber }).IsUnique();
        });
    }
}

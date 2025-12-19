using Microsoft.EntityFrameworkCore;
using SysMonitor.Core.Data.Entities;

namespace SysMonitor.Core.Data;

/// <summary>
/// Entity Framework Core database context for historical metric storage.
/// </summary>
public class HistoryDbContext : DbContext
{
    private readonly string _dbPath;

    public DbSet<MetricSnapshot> MetricSnapshots { get; set; } = null!;

    public HistoryDbContext()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SysMonitor");

        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }

        _dbPath = Path.Combine(folder, "history.db");
    }

    public HistoryDbContext(DbContextOptions<HistoryDbContext> options) : base(options)
    {
        _dbPath = string.Empty;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite($"Data Source={_dbPath}");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MetricSnapshot>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Timestamp)
                .IsRequired();

            entity.Property(e => e.MetricType)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(e => e.Value)
                .IsRequired();

            // Indexes for efficient querying
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.MetricType);
            entity.HasIndex(e => new { e.MetricType, e.Timestamp });
        });
    }
}

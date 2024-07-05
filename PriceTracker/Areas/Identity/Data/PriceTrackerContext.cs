using PriceTracker.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace PriceTracker.Data
{
    public class PriceTrackerContext : IdentityDbContext<PriceTrackerUser>
    {
        public PriceTrackerContext(DbContextOptions<PriceTrackerContext> options)
            : base(options)
        {
        }

        public DbSet<Settings> Settings { get; set; }
        public DbSet<AffiliateVerification> AffiliateVerification { get; set; }
        public DbSet<StoreClass> Stores { get; set; }
        public DbSet<ProductClass> Products { get; set; }
        public DbSet<PriceHistoryClass> PriceHistories { get; set; }
        public DbSet<ScrapHistoryClass> ScrapHistories { get; set; }
        public DbSet<CategoryClass> Categories { get; set; }
        public DbSet<PriceValueClass> PriceValues { get; set; }
        public DbSet<TableSizeInfo> TableSizeInfo { get; set; }
        public DbSet<FlagsClass> Flags { get; set; }
        public DbSet<ProductFlag> ProductFlags { get; set; }
        public DbSet<PriceTrackerUserStore> UserStores { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<PriceTrackerUser>()
                .HasOne(u => u.AffiliateVerification)
                .WithOne(av => av.PriceTrackerUser)
                .HasForeignKey<AffiliateVerification>(av => av.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PriceHistoryClass>()
                .HasOne(ph => ph.ScrapHistory)
                .WithMany(sh => sh.PriceHistories)
                .HasForeignKey(ph => ph.ScrapHistoryId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ScrapHistoryClass>()
                .HasOne(sh => sh.Store)
                .WithMany(s => s.ScrapHistories)
                .HasForeignKey(sh => sh.StoreId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PriceHistoryClass>()
                .HasOne(ph => ph.Product)
                .WithMany(p => p.PriceHistories)
                .HasForeignKey(ph => ph.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ProductFlag>()
                .HasKey(pf => new { pf.ProductId, pf.FlagId });

            modelBuilder.Entity<ProductFlag>()
                .HasOne(pf => pf.Product)
                .WithMany(p => p.ProductFlags)
                .HasForeignKey(pf => pf.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ProductFlag>()
                .HasOne(pf => pf.Flag)
                .WithMany(f => f.ProductFlags)
                .HasForeignKey(pf => pf.FlagId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<TableSizeInfo>().HasNoKey();

            modelBuilder.Entity<PriceTrackerUserStore>()  // <- Użyj aliasu
                .HasKey(us => new { us.UserId, us.StoreId });

            modelBuilder.Entity<PriceTrackerUserStore>()  // <- Użyj aliasu
                .HasOne(us => us.PriceTrackerUser)
                .WithMany(u => u.UserStores)
                .HasForeignKey(us => us.UserId);

            modelBuilder.Entity<PriceTrackerUserStore>()  // <- Użyj aliasu
                .HasOne(us => us.StoreClass)
                .WithMany(s => s.UserStores)
                .HasForeignKey(us => us.StoreId);
        }

        public async Task<List<TableSizeInfo>> GetTableSizes()
        {
            var query = @"
                SELECT 
                    t.name AS TableName,
                    s.name AS SchemaName,
                    p.rows AS RowCounts,
                    SUM(a.total_pages) * 8 AS TotalSpaceKB, 
                    SUM(a.used_pages) * 8 AS UsedSpaceKB, 
                    (SUM(a.total_pages) - SUM(a.used_pages)) * 8 AS UnusedSpaceKB
                FROM 
                    sys.tables t
                INNER JOIN      
                    sys.indexes i ON t.OBJECT_ID = i.object_id
                INNER JOIN 
                    sys.partitions p ON i.object_id = p.OBJECT_ID AND i.index_id = p.index_id
                INNER JOIN 
                    sys.allocation_units a ON p.partition_id = a.container_id
                LEFT OUTER JOIN 
                    sys.schemas s ON t.schema_id = s.schema_id
                WHERE 
                    t.name IN ('Products', 'PriceHistories')
                GROUP BY 
                    t.Name, s.Name, p.Rows
                ORDER BY 
                    TotalSpaceKB DESC";

            return await this.TableSizeInfo.FromSqlRaw(query).ToListAsync();
        }
    }

    public class TableSizeInfo
    {
        public string TableName { get; set; }
        public string SchemaName { get; set; }
        public long RowCounts { get; set; }
        public long UsedSpaceKB { get; set; }

        public double UsedSpaceMB => UsedSpaceKB / 1024.0;
    }
}

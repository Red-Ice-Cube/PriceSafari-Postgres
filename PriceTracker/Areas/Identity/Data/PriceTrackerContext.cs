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

            modelBuilder.Entity<PriceTrackerUserStore>()
                .HasKey(us => new { us.UserId, us.StoreId });

            modelBuilder.Entity<PriceTrackerUserStore>()
                .HasOne(us => us.PriceTrackerUser)
                .WithMany(u => u.UserStores)
                .HasForeignKey(us => us.UserId);

            modelBuilder.Entity<PriceTrackerUserStore>()
                .HasOne(us => us.StoreClass)
                .WithMany(s => s.UserStores)
                .HasForeignKey(us => us.StoreId);
        }
    }
}

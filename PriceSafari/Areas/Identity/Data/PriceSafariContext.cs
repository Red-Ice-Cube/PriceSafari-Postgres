using PriceSafari.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace PriceSafari.Data
{
    public class PriceSafariContext : IdentityDbContext<PriceSafariUser>
    {
        public PriceSafariContext(DbContextOptions<PriceSafariContext> options)
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
        public DbSet<PriceSafariUserStore> UserStores { get; set; }
        public DbSet<ProductMap> ProductMaps { get; set; }
        public DbSet<CoOfrClass> CoOfrs { get; set; }
        public DbSet<CoOfrPriceHistoryClass> CoOfrPriceHistories { get; set; }
        public DbSet<ClientProfile> ClientProfiles { get; set; }
        public DbSet<Region> Regions { get; set; }
        public DbSet<PriceData> PriceData { get; set; }
        public DbSet<ScrapeRun> ScrapeRuns { get; set; }

        public DbSet<GoogleScrapingProduct> GoogleScrapingProducts { get; set; }
        public DbSet<PriceSafariReport> PriceSafariReports { get; set; }



        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

          


            // Dodatkowe relacje z istniejącymi encjami, jak w oryginalnym przykładzie:
            modelBuilder.Entity<PriceSafariUser>()
                .HasOne(u => u.AffiliateVerification)
                .WithOne(av => av.PriceSafariUser)
                .HasForeignKey<AffiliateVerification>(av => av.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PriceHistoryClass>()
                .HasOne(ph => ph.ScrapHistory)
                .WithMany(sh => sh.PriceHistories)
                .HasForeignKey(ph => ph.ScrapHistoryId)
                .OnDelete(DeleteBehavior.Cascade);

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
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PriceSafariUserStore>()
                .HasKey(us => new { us.UserId, us.StoreId });

            modelBuilder.Entity<PriceSafariUserStore>()
                .HasOne(us => us.PriceSafariUser)
                .WithMany(u => u.UserStores)
                .HasForeignKey(us => us.UserId);

            modelBuilder.Entity<PriceSafariUserStore>()
                .HasOne(us => us.StoreClass)
                .WithMany(s => s.UserStores)
                .HasForeignKey(us => us.StoreId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ClientProfile>()
                 .HasOne(cp => cp.CreatedByUser)
                 .WithMany()
                 .HasForeignKey(cp => cp.CreatedByUserId)
                 .OnDelete(DeleteBehavior.Restrict);
        }

    }
}

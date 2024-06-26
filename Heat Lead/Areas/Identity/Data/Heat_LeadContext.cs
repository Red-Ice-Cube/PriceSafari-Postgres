using Heat_Lead.Areas.Identity.Data;
using Heat_Lead.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PriceTracker.Models;

namespace Heat_Lead.Data;

public class Heat_LeadContext : IdentityDbContext<Heat_LeadUser>
{
    public Heat_LeadContext(DbContextOptions<Heat_LeadContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)

    {
       


        modelBuilder.Entity<Heat_LeadUser>()
            .HasOne(u => u.AffiliateVerification)
            .WithOne(av => av.Heat_LeadUser)
            .HasForeignKey<AffiliateVerification>(av => av.UserId)
            .OnDelete(DeleteBehavior.Cascade);

     



        // Konfiguracja relacji między PriceHistoryClass a ScrapHistoryClass
        modelBuilder.Entity<PriceHistoryClass>()
            .HasOne(ph => ph.ScrapHistory)
            .WithMany(sh => sh.PriceHistories)
            .HasForeignKey(ph => ph.ScrapHistoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // Konfiguracja relacji między ScrapHistoryClass a StoreClass
        modelBuilder.Entity<ScrapHistoryClass>()
            .HasOne(sh => sh.Store)
            .WithMany(s => s.ScrapHistories)
            .HasForeignKey(sh => sh.StoreId)
            .OnDelete(DeleteBehavior.Restrict);

        // Konfiguracja relacji między PriceHistoryClass a ProductClass
        modelBuilder.Entity<PriceHistoryClass>()
            .HasOne(ph => ph.Product)
            .WithMany(p => p.PriceHistories)
            .HasForeignKey(ph => ph.ProductId)
            .OnDelete(DeleteBehavior.Cascade);



        base.OnModelCreating(modelBuilder);
    }


    public DbSet<Settings> Settings { get; set; }

    public DbSet<AffiliateVerification> AffiliateVerification { get; set; }










    public DbSet<StoreClass> Stores { get; set; }
    public DbSet<ProductClass> Products { get; set; }
    public DbSet<PriceHistoryClass> PriceHistories { get; set; }
    public DbSet<ScrapHistoryClass> ScrapHistories { get; set; }
}
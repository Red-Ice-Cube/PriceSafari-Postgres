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
            .HasMany(u => u.Generator)
            .WithOne(g => g.Heat_LeadUser)
            .HasForeignKey(g => g.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Category>()
            .HasMany(p => p.Generator)
            .WithOne(g => g.Category)
            .HasForeignKey(g => g.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Store>()
            .HasMany(s => s.Category)
            .WithOne(p => p.Store)
            .HasForeignKey(p => p.StoreId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Heat_LeadUser>()
            .HasMany(u => u.AffiliateLink)
            .WithOne(a => a.Heat_LeadUser)
            .HasForeignKey(a => a.UserId);

        modelBuilder.Entity<Category>()
            .HasMany(c => c.Product)
            .WithOne(p => p.Category)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AffiliateLink>()
            .HasMany(a => a.AffiliateLinkClick)
            .WithOne(c => c.AffiliateLink)
            .HasForeignKey(c => c.AffiliateLinkId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Heat_LeadUser>()
            .HasOne(u => u.Wallet)
            .WithOne(w => w.Heat_LeadUser)
            .HasForeignKey<Wallet>(w => w.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Heat_LeadUser>()
            .HasMany(u => u.Paycheck)
            .WithOne(p => p.Heat_LeadUser)
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<Wallet>()
            .HasMany(w => w.Paycheck)
            .WithOne(p => p.Wallet)
            .HasForeignKey(p => p.WalletId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<CampaignProduct>()
            .HasKey(cp => new { cp.CampaignId, cp.ProductId });

        modelBuilder.Entity<CampaignProduct>()
            .HasOne(cp => cp.Campaign)
            .WithMany(c => c.CampaignProducts)
            .HasForeignKey(cp => cp.CampaignId);

        modelBuilder.Entity<CampaignProduct>()
            .HasOne(cp => cp.Product)
            .WithMany()
            .HasForeignKey(cp => cp.ProductId);

        modelBuilder.Entity<CampaignCategory>()
            .HasKey(cc => new { cc.CampaignId, cc.CategoryId });

        modelBuilder.Entity<CampaignCategory>()
            .HasOne(cc => cc.Campaign)
            .WithMany(c => c.CampaignCategories)
            .HasForeignKey(cc => cc.CampaignId);

        modelBuilder.Entity<CampaignCategory>()
            .HasOne(cc => cc.Category)
            .WithMany()
            .HasForeignKey(cc => cc.CategoryId);

        modelBuilder.Entity<AffiliateLink>()
            .HasOne(al => al.Campaign)
            .WithMany(c => c.AffiliateLinks)
            .HasForeignKey(al => al.CampaignId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<Heat_LeadUser>()
            .HasOne(u => u.AffiliateVerification)
            .WithOne(av => av.Heat_LeadUser)
            .HasForeignKey<AffiliateVerification>(av => av.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Product>()
            .HasMany(p => p.StoreProductIds)
            .WithOne(s => s.Product)
            .HasForeignKey(s => s.ProductId);

        modelBuilder.Entity<CanvasJS>()
            .HasOne(cjs => cjs.AffiliateLink)
            .WithMany(al => al.CanvasJSItems)
            .HasForeignKey(cjs => cjs.AffiliateLinkId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CanvasJS>()
             .HasOne(cjs => cjs.Style)
             .WithMany()
             .HasForeignKey(cjs => cjs.StyleId)
             .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<CanvasJSStyle>()
               .HasOne(cs => cs.User)
               .WithMany(u => u.CanvasJSStyles)
               .HasForeignKey(cs => cs.UserId)
               .IsRequired()
               .OnDelete(DeleteBehavior.NoAction);




        modelBuilder.Entity<InterceptOrder>()
             .HasIndex(o => new { o.OrderId, o.OrderKey })
             .IsUnique();




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

    public DbSet<Store> Store { get; set; }
    public DbSet<Category> Category { get; set; }
    public DbSet<Generator> Generator { get; set; }
    public DbSet<Product> Product { get; set; }
    public DbSet<Order> Order { get; set; } = default!;
    public DbSet<AffiliateLink> AffiliateLink { get; set; }
    public DbSet<AffiliateLinkClick> AffiliateLinkClick { get; set; }
    public DbSet<Wallet> Wallet { get; set; }
    public DbSet<Paycheck> Paycheck { get; set; }
    public DbSet<Campaign> Campaigns { get; set; }
    public DbSet<CampaignProduct> CampaignProducts { get; set; }
    public DbSet<CampaignCategory> CampaignCategories { get; set; }
    public DbSet<InterceptOrder> InterceptOrders { get; set; }
    public DbSet<OrderDetail> OrderDetails { get; set; }
    public DbSet<Settings> Settings { get; set; }
    public DbSet<GhostOrderDetail> GhostOrderDetail { get; set; }
    public DbSet<News> News { get; set; }
    public DbSet<AffiliateVerification> AffiliateVerification { get; set; }
    public DbSet<ProductIdStore> ProductIdStores { get; set; }
    public DbSet<FingerprintData> FingerprintData { get; set; }
    public DbSet<CanvasJS> CanvasJS { get; set; }
    public DbSet<CanvasJSStyle> CanvasJSStyles { get; set; }









    public DbSet<StoreClass> Stores { get; set; }
    public DbSet<ProductClass> Products { get; set; }
    public DbSet<PriceHistoryClass> PriceHistories { get; set; }
    public DbSet<ScrapHistoryClass> ScrapHistories { get; set; }
}
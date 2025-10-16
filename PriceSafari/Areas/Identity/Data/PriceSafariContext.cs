using PriceSafari.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Models.SchedulePlan;
using PriceSafari.Models.ProductXML;

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
        public DbSet<GoogleScrapingProduct> GoogleScrapingProducts { get; set; }
        public DbSet<PriceSafariReport> PriceSafariReports { get; set; }
        public DbSet<GlobalPriceReport> GlobalPriceReports { get; set; }
        public DbSet<PlanClass> Plans { get; set; }
        public DbSet<InvoiceCounter> InvoiceCounters { get; set; }

        public DbSet<InvoiceClass> Invoices { get; set; }
        public DbSet<UserPaymentData> UserPaymentDatas { get; set; }
        public DbSet<DeviceStatus> DeviceStatuses { get; set; }
        public DbSet<TaskExecutionLog> TaskExecutionLogs { get; set; }
        public DbSet<SchedulePlan> SchedulePlans { get; set; }
        public DbSet<DayDetail> DayDetails { get; set; }
        public DbSet<ScheduleTask> ScheduleTasks { get; set; }
        public DbSet<ScheduleTaskStore> ScheduleTaskStores { get; set; }

        public DbSet<GoogleFieldMapping> GoogleFieldMappings { get; set; }
        public DbSet<CeneoFieldMapping> CeneoFieldMappings { get; set; }
        public DbSet<CompetitorPresetClass> CompetitorPresets { get; set; }
        public DbSet<CompetitorPresetItem> CompetitorPresetItems { get; set; }
        public DbSet<AllegroProductClass> AllegroProducts { get; set; }
        public DbSet<AllegroOfferToScrape> AllegroOffersToScrape { get; set; }
        public DbSet<AllegroScrapedOffer> AllegroScrapedOffers { get; set; }
        public DbSet<AllegroScrapeHistory> AllegroScrapeHistories { get; set; }
        public DbSet<AllegroPriceHistory> AllegroPriceHistories { get; set; }
        public DbSet<UserMessage> UserMessages { get; set; }
        public DbSet<PriceHistoryExtendedInfoClass> PriceHistoryExtendedInfos { get; set; }
        public DbSet<AllegroPriceHistoryExtendedInfoClass> AllegroPriceHistoryExtendedInfos { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

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
                .HasKey(pf => pf.ProductFlagId);

            modelBuilder.Entity<ProductFlag>()
                .HasOne(pf => pf.Product)
                .WithMany(p => p.ProductFlags)
                .HasForeignKey(pf => pf.ProductId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ProductFlag>()
                .HasOne(pf => pf.AllegroProduct)
                .WithMany(ap => ap.ProductFlags)
                .HasForeignKey(pf => pf.AllegroProductId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<ProductFlag>()
                .HasOne(pf => pf.Flag)
                .WithMany(f => f.ProductFlags)
                .HasForeignKey(pf => pf.FlagId)
                .OnDelete(DeleteBehavior.NoAction);

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

            modelBuilder.Entity<GoogleScrapingProduct>()
                 .HasOne(gsp => gsp.Region)
                 .WithMany(r => r.GoogleScrapingProducts)
                 .HasForeignKey(gsp => gsp.RegionId)
                 .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<GlobalPriceReport>()
                .HasOne(gpr => gpr.Region)
                .WithMany(r => r.GlobalPriceReports)
                .HasForeignKey(gpr => gpr.RegionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PriceSafariReport>()
                  .HasOne(psr => psr.Store)
                  .WithMany(s => s.PriceSafariReports)
                  .HasForeignKey(psr => psr.StoreId)
                  .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PriceSafariUser>()
                   .HasMany(u => u.UserPaymentDatas)
                   .WithOne(pd => pd.User)
                   .HasForeignKey(pd => pd.UserId)
                   .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<SchedulePlan>()
                 .HasOne(sp => sp.Monday)
                 .WithOne()
                 .HasForeignKey<SchedulePlan>(sp => sp.MondayId)
                 .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<SchedulePlan>()
                .HasOne(sp => sp.Tuesday)
                .WithOne()
                .HasForeignKey<SchedulePlan>(sp => sp.TuesdayId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<SchedulePlan>()
             .HasOne(sp => sp.Wednesday)
             .WithOne()
             .HasForeignKey<SchedulePlan>(sp => sp.WednesdayId)
             .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<SchedulePlan>()
              .HasOne(sp => sp.Thursday)
              .WithOne()
              .HasForeignKey<SchedulePlan>(sp => sp.ThursdayId)
              .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<SchedulePlan>()
               .HasOne(sp => sp.Friday)
               .WithOne()
               .HasForeignKey<SchedulePlan>(sp => sp.FridayId)
               .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<SchedulePlan>()
               .HasOne(sp => sp.Saturday)
               .WithOne()
               .HasForeignKey<SchedulePlan>(sp => sp.SaturdayId)
               .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<SchedulePlan>()
               .HasOne(sp => sp.Sunday)
               .WithOne()
               .HasForeignKey<SchedulePlan>(sp => sp.SundayId)
               .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ScheduleTask>()
                .HasOne(st => st.DayDetail)
                .WithMany(dd => dd.Tasks)
                .HasForeignKey(st => st.DayDetailId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ScheduleTaskStore>()
                .HasOne(sts => sts.ScheduleTask)
                .WithMany(st => st.TaskStores)
                .HasForeignKey(sts => sts.ScheduleTaskId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ScheduleTaskStore>()
                .HasOne(sts => sts.Store)
                .WithMany(s => s.ScheduleTaskStores)
                .HasForeignKey(sts => sts.StoreId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AllegroScrapeHistory>()
                .HasMany(h => h.PriceHistories)
                .WithOne(p => p.AllegroScrapeHistory)
                .HasForeignKey(p => p.AllegroScrapeHistoryId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AllegroProductClass>()

                .HasMany<AllegroPriceHistory>()
                .WithOne(p => p.AllegroProduct)
                .HasForeignKey(p => p.AllegroProductId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PriceSafariUser>()
                  .HasMany(u => u.UserMessages)
                  .WithOne(m => m.User)
                  .HasForeignKey(m => m.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AllegroProductClass>()
               .HasOne(ap => ap.Store)
               .WithMany(s => s.AllegroProducts)
               .HasForeignKey(ap => ap.StoreId)
               .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PriceHistoryExtendedInfoClass>()
                .HasIndex(e => new { e.ProductId, e.ScrapHistoryId })
                .IsUnique();

            modelBuilder.Entity<AllegroPriceHistoryExtendedInfoClass>(entity =>
            {
        
                entity.HasOne(ext => ext.AllegroProduct)
                      .WithMany()
                      .HasForeignKey(ext => ext.AllegroProductId)
                      .OnDelete(DeleteBehavior.NoAction); // <-- ZMIANA

                entity.HasOne(ext => ext.ScrapHistory)
                      .WithMany()
                      .HasForeignKey(ext => ext.ScrapHistoryId)
                      .OnDelete(DeleteBehavior.Cascade); // <-- BEZ ZMIAN
            });
        }
    }
}
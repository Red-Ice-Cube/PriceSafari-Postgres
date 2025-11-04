using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using PdfSharp.Fonts;
using PriceSafari.Culture;
using PriceSafari.Data;
using PriceSafari.DotEnv;
using PriceSafari.Hubs;
using PriceSafari.Models;
using PriceSafari.Models.SchedulePlan;
using PriceSafari.Scrapers;
using PriceSafari.Services.AllegroServices;
using PriceSafari.Services.ConnectionStatus;
using PriceSafari.Services.ControlNetwork;
using PriceSafari.Services.ControlXY;
using PriceSafari.Services.EmailService;
using PriceSafari.Services.ScheduleService;
using PriceSafari.Services.ViewRenderService;
using PriceSafari.SignalIR;

public class Program
{
    public static async Task Main(string[] args)
    {

        var root = Directory.GetCurrentDirectory();
        var dotenv = Path.Combine(root, ".env");
        DotEnv.Load(dotenv);

        var builder = WebApplication.CreateBuilder(args);

        builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        builder.Configuration.AddEnvironmentVariables();

        var dbServer = Environment.GetEnvironmentVariable("DB_SERVER");
        var dbName = Environment.GetEnvironmentVariable("DB_NAME");
        var dbUser = Environment.GetEnvironmentVariable("DB_USER");
        var dbPassword = Environment.GetEnvironmentVariable("DB_PASSWORD");

        var connectionString = $"Data Source={dbServer};Database={dbName};Uid={dbUser};Password={dbPassword};TrustServerCertificate=True";

        builder.Services.AddDbContext<PriceSafariContext>(options =>
           options.UseSqlServer(connectionString, sqlServerOptions =>
           {
          
               sqlServerOptions.EnableRetryOnFailure(
                   maxRetryCount: 5, 
                   maxRetryDelay: TimeSpan.FromSeconds(30), 
                   errorNumbersToAdd: null); 

               sqlServerOptions.UseCompatibilityLevel(110);
          
        }));


        builder.Services.AddDefaultIdentity<PriceSafariUser>(options => options.SignIn.RequireConfirmedAccount = true)
       .AddRoles<IdentityRole>()
       .AddErrorDescriber<PolishIdentityErrorDescriber>()
       .AddEntityFrameworkStores<PriceSafariContext>();
        builder.Services.AddScoped<AuthorizeStoreAccessAttribute>();
        builder.Services.AddControllersWithViews();
        builder.Services.AddHttpClient();

        builder.Services.AddTransient<EmailService>();

        builder.Services.AddTransient<IEmailSender>(s => s.GetRequiredService<EmailService>());

        builder.Services.AddTransient<IAppEmailSender>(s => s.GetRequiredService<EmailService>());
        builder.Services.AddSignalR();
        builder.Services.AddHostedService<KeepAliveService>();
        builder.Services.AddTransient<IViewRenderService, ViewRenderService>();
        builder.Services.AddSingleton<ControlXYService>();
        builder.Services.AddScoped<StoreProcessingService>();
        builder.Services.AddHostedService<ScheduledTaskService>();
        builder.Services.AddScoped<UrlGroupingService>();
        builder.Services.AddHttpClient<CeneoScraper>();
        builder.Services.AddScoped<CeneoScraper>();
        builder.Services.AddScoped<GoogleScraperService>();
        builder.Services.AddScoped<CeneoScraperService>();
        builder.Services.AddHostedService<ScraperHealthCheckService>();
        builder.Services.AddScoped<INetworkControlService, NetworkControlService>();
        builder.Services.AddScoped<AllegroUrlGroupingService>();
        builder.Services.AddScoped<AllegroScrapingService>();
        builder.Services.AddScoped<AllegroApiBotService>();
        builder.Services.AddScoped<AllegroProcessingService>();
        builder.Services.AddScoped<AllegroPriceBridgeService>();

        GlobalFontSettings.UseWindowsFontsUnderWindows = true;

        builder.Services.AddMemoryCache();
        builder.Services.AddSession(options =>
        {
            options.IdleTimeout = TimeSpan.FromMinutes(600);
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
        });

        var app = builder.Build();

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Home/Error");
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseSession();
        app.MapControllers();
        app.MapControllerRoute(
              name: "default",
              pattern: "{controller=Identity}/{action=Login}/{id?}"
         );

        app.Use(async (context, next) =>
        {
            if (context.Request.Path == "/")
            {
                context.Response.Redirect("/Identity/Account/Login");
                return;
            }

            await next();
        });

        app.MapRazorPages();

        using (var scope = app.Services.CreateScope())
        {
            var services = scope.ServiceProvider;
            try
            {
                var context = services.GetRequiredService<PriceSafariContext>();

                if (!context.Settings.Any())
                {
                    var defaultSettings = new Settings();
                    context.Settings.Add(defaultSettings);
                    await context.SaveChangesAsync();
                }

                await CreateDefaultAdmin(services);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        using (var scope = app.Services.CreateScope())
        {
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

            var roles = new[] { "Admin", "Manager", "Member", "PreMember" };

            foreach (var role in roles)
            {

                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole(role));
                }
            }
        }

        app.MapHub<ScrapingHub>("/scrapingHub");
        app.MapHub<ReportProgressHub>("/reportProgressHub");
        app.MapHub<DashboardProgressHub>("/dashboardProgressHub");

        await app.RunAsync();
    }

    private static async Task CreateDefaultAdmin(IServiceProvider serviceProvider)
    {
        var userManager = serviceProvider.GetRequiredService<UserManager<PriceSafariUser>>();
        var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var context = serviceProvider.GetRequiredService<PriceSafariContext>();

        if (!userManager.Users.Any())
        {
            if (!await roleManager.RoleExistsAsync("Admin"))
            {
                await roleManager.CreateAsync(new IdentityRole("Admin"));
            }

            var adminUser = new PriceSafariUser
            {
                UserName = "mateusz.werner@myjki.com",
                Email = "mateusz.werner@myjki.com",
                EmailConfirmed = true,
                PartnerName = "Mateusz",
                PartnerSurname = "Werner",
                IsMember = false,
                IsActive = true
            };

            var result = await userManager.CreateAsync(adminUser, "Test1234$");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(adminUser, "Admin");

                var affiliateVerification = new AffiliateVerification
                {
                    UserId = adminUser.Id,
                    IsVerified = true
                };
                context.AffiliateVerification.Add(affiliateVerification);
                await context.SaveChangesAsync();
            }
            else
            {
                foreach (var error in result.Errors)
                {
                    Console.WriteLine(error.Description);
                }
            }
        }
    }

}
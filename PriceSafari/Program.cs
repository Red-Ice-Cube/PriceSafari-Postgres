using PriceSafari.Data;
using PriceSafari.DotEnv;
using PriceSafari.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Hubs;
using PriceSafari.Services.ViewRenderService;
using PriceSafari.Scrapers;



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

        //var connectionString = $"Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=PriceSafariDBLH;Integrated Security=True;Connect Timeout=30;Encrypt=False;Trust Server Certificate=False;Application Intent=ReadWrite;Multi Subnet Failover=False";
        builder.Services.AddDbContext<PriceSafariContext>(options => options.UseSqlServer(connectionString));

        builder.Services.AddDefaultIdentity<PriceSafariUser>(options => options.SignIn.RequireConfirmedAccount = true)
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<PriceSafariContext>();
        builder.Services.AddScoped<AuthorizeStoreAccessAttribute>();
        builder.Services.AddControllersWithViews();
        builder.Services.AddHttpClient();

        builder.Services.AddTransient<IEmailSender, EmailService>();
        builder.Services.AddSignalR();
        builder.Services.AddHostedService<KeepAliveService>();
        builder.Services.AddTransient<IViewRenderService, ViewRenderService>();

        builder.Services.AddScoped<StoreProcessingService>();
        builder.Services.AddHostedService<ScheduledTaskService>();

        builder.Services.AddHttpClient<CaptchaScraper>();
        builder.Services.AddScoped<CaptchaScraper>();

        builder.Services.AddMemoryCache();
        builder.Services.AddSession(options =>
        {
            options.IdleTimeout = TimeSpan.FromMinutes(30);
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

        app.UseAuthorization();
        app.UseSession();

        app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}");

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
            var roles = new[] { "Admin", "Manager", "Member" };

            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                    await roleManager.CreateAsync(new IdentityRole(role));
            }
        }


    
        app.MapHub<ScrapingHub>("/scrapingHub");
        app.MapHub<ReportProgressHub>("/reportProgressHub");

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
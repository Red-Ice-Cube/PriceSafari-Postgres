
using PriceTracker.Data;
using PriceTracker.DotEnv;
using PriceTracker.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using PriceTracker.Hubs;
using PriceTracker.Services;

//elo elo
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

        builder.Services.AddDbContext<PriceTrackerContext>(options => options.UseSqlServer(connectionString));

        builder.Services.AddDefaultIdentity<PriceTrackerUser>(options => options.SignIn.RequireConfirmedAccount = true)
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<PriceTrackerContext>();

        builder.Services.AddControllersWithViews();
        builder.Services.AddHttpClient();

        builder.Services.AddTransient<IEmailSender, EmailService>();


        builder.Services.AddSignalR();

        // Register HttpClient for Scraper
        builder.Services.AddHttpClient<Scraper>();

        // Register Scraper as a service
        builder.Services.AddScoped<Scraper>();

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
                var context = services.GetRequiredService<PriceTrackerContext>();

                if (!context.Settings.Any())
                {
                    var defaultSettings = new Settings();
                    context.Settings.Add(defaultSettings);
                    await context.SaveChangesAsync();
                }
            }
            catch (Exception)
            {
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

        // Map SignalR hub
        app.MapHub<ScrapingHub>("/scrapingHub");

        await app.RunAsync();
    }
}

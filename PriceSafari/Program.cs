
using PriceSafari.Data;
using PriceSafari.DotEnv;
using PriceSafari.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Hubs;
using PriceSafari.Services;


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

        builder.Services.AddDbContext<PriceSafariContext>(options => options.UseSqlServer(connectionString));

        builder.Services.AddDefaultIdentity<PriceSafariUser>(options => options.SignIn.RequireConfirmedAccount = true)
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<PriceSafariContext>();

        builder.Services.AddControllersWithViews();
        builder.Services.AddHttpClient();

        builder.Services.AddTransient<IEmailSender, EmailService>();


        builder.Services.AddSignalR();

        builder.Services.AddHttpClient<Scraper>();

        builder.Services.AddScoped<Scraper>();

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

        app.MapHub<ScrapingHub>("/scrapingHub");

        await app.RunAsync();
    }
}

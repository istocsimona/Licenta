using Licenta.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // Tabelele principale
    public DbSet<Trip> Trips { get; set; }
    public DbSet<DayPlan> DayPlans { get; set; }
    public DbSet<Location> Locations { get; set; }
    public DbSet<ItineraryItem> ItineraryItems { get; set; }

    // Tabelele pentru preferințe și tag-uri
    public DbSet<Tag> Tags { get; set; }
    public DbSet<LocationTag> LocationTags { get; set; }
    public DbSet<UserTagPreference> UserTagPreferences { get; set; }

    // Tabelele pentru servicii externe
    public DbSet<WeatherForecast> WeatherForecasts { get; set; }
    public DbSet<Accomodation> Accomodations { get; set; }
    public DbSet<Reservation> Reservations { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Această linie este CRITICĂ pentru Individual Accounts/Identity. 
        // Dacă o omiți, migrarea va eșua.
        base.OnModelCreating(modelBuilder);

        // 1. Configurare Cheie Compusă pentru LocationTag (Many-to-Many)
        modelBuilder.Entity<LocationTag>()
            .HasKey(lt => new { lt.LocationId, lt.TagId });

        // 2. Configurare Cheie Compusă pentru UserTagPreference
        modelBuilder.Entity<UserTagPreference>()
            .HasKey(utp => new { utp.UserId, utp.TagId });

        // 3. Opțional: Configurare Relații (dacă EF Core nu le detectează automat)

        // Relația User -> Trips (One-to-Many)
        modelBuilder.Entity<Trip>()
            .HasOne(t => t.User)
            .WithMany(u => u.Trips)
            .HasForeignKey(t => t.UserId);

        // Relația Trip -> DayPlans (One-to-Many)
        modelBuilder.Entity<DayPlan>()
            .HasOne(dp => dp.Trip)
            .WithMany(t => t.DayPlans)
            .HasForeignKey(dp => dp.TripId);

        // Relația DayPlan -> ItineraryItems (One-to-Many)
        modelBuilder.Entity<ItineraryItem>()
            .HasOne(ii => ii.DayPlan)
            .WithMany(dp => dp.ItineraryItems)
            .HasForeignKey(ii => ii.DayPlanId)
            .OnDelete(DeleteBehavior.Restrict);

        // 4. Configurare pentru precizie zecimală (pentru coordonate GPS)
        modelBuilder.Entity<Location>(entity =>
        {
            entity.Property(e => e.Latitude).HasColumnType("decimal(9, 6)");
            entity.Property(e => e.Longitude).HasColumnType("decimal(9, 6)");
        });
    }
}
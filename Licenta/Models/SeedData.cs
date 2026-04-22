using Licenta.Data;
using Licenta.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Licenta.Models
{
    public static class SeedData
    {
        public static void Initialize(IServiceProvider serviceProvider)
        {
            using (var context = new ApplicationDbContext(
                serviceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>()))
            {
                // -----------------------------
                // 1. CREATE ROLES
                // -----------------------------
                if (!context.Roles.Any())
                {
                    context.Roles.AddRange(
                        new IdentityRole
                        {
                            Id = "ad3bca2c-fd2e-4723-9220-1365049560a1",
                            Name = "Admin",
                            NormalizedName = "ADMIN"
                        },
                        new IdentityRole
                        {
                            Id = "ad3bca2c-fd2e-4723-9220-1365049560a2",
                            Name = "User",
                            NormalizedName = "USER"
                        }
                    );
                }

                // -----------------------------
                // 2. CREATE ADMIN USER
                // -----------------------------
                var hasher = new PasswordHasher<ApplicationUser>();

                // Verificăm dacă admin-ul există deja după email
                if (!context.Users.Any(u => u.UserName == "admin@licenta.com"))
                {
                    context.Users.Add(
                        new ApplicationUser
                        {
                            Id = "8e445865-a24d-4543-a6c6-9443d048cdb9", // ID unic
                            UserName = "admin@licenta.com",
                            Email = "admin@licenta.com",
                            NormalizedEmail = "ADMIN@LICENTA.COM",
                            NormalizedUserName = "ADMIN@LICENTA.COM",
                            EmailConfirmed = true,
                            PasswordHash = hasher.HashPassword(null, "Admin123!"),
                            ProfilePicture = "/images/profiles/default-admin.png" // Poza default conform diagramei 
                        }
                    );

                    // Alocăm rolul de Admin utilizatorului creat
                    context.UserRoles.Add(
                        new IdentityUserRole<string>
                        {
                            RoleId = "ad3bca2c-fd2e-4723-9220-1365049560a1",
                            UserId = "8e445865-a24d-4543-a6c6-9443d048cdb9"
                        }
                    );
                }

                // -----------------------------
                // 3. CREATE DEFAULT TAGS 
                // -----------------------------
                if (!context.Tags.Any())
                {
                    context.Tags.AddRange(
    // Culture & History
    new Tag { Name = "Museum", Color = "#FF5733", IsDefault = true, Icon = "bi-bank2" },
    new Tag { Name = "Historic Site", Color = "#C0392B", IsDefault = true, Icon = "bi-building-fill" },
    new Tag { Name = "Art Gallery", Color = "#9B59B6", IsDefault = true, Icon = "bi-palette-fill" }, // Palette looks better for art
    new Tag { Name = "Exhibition Center", Color = "#8E44AD", IsDefault = true, Icon = "bi-image-fill" },
    new Tag { Name = "Cultural Center", Color = "#7D3C98", IsDefault = true, Icon = "bi-globe-central-south-asia" }, // globe with detail
    new Tag { Name = "Library", Color = "#6C3483", IsDefault = true, Icon = "bi-book-fill" },
    new Tag { Name = "Archaeological Site", Color = "#A04000", IsDefault = true, Icon = "bi-eye-fill" }, // Eye of Horus style for archaeology

    // Nature
    new Tag { Name = "Park", Color = "#2ECC71", IsDefault = true, Icon = "bi-tree-fill" },
    new Tag { Name = "Botanical Garden", Color = "#27AE60", IsDefault = true, Icon = "bi-flower1" },
    new Tag { Name = "Nature Reserve", Color = "#1E8449", IsDefault = true, Icon = "bi-leaf-fill" },
    new Tag { Name = "National Park", Color = "#196F3D", IsDefault = true, Icon = "bi-map-fill" },
    new Tag { Name = "Lake", Color = "#5DADE2", IsDefault = true, Icon = "bi-water" },
    new Tag { Name = "River", Color = "#3498DB", IsDefault = true, Icon = "bi-droplet-fill" },
    new Tag { Name = "Hiking Trail", Color = "#27AE60", IsDefault = true, Icon = "bi-signpost-2-fill" }, // Directional signs for trails
    new Tag { Name = "Viewpoint", Color = "#F4D03F", IsDefault = true, Icon = "bi-binoculars-fill" },
    new Tag { Name = "Cave", Color = "#5D6D7E", IsDefault = true, Icon = "bi-hole" },

    // Food & Drink
    new Tag { Name = "Restaurant", Color = "#3498DB", IsDefault = true, Icon = "bi-utensils" }, // Classic utensils
    new Tag { Name = "Cafe", Color = "#5DADE2", IsDefault = true, Icon = "bi-cup-hot-fill" },
    new Tag { Name = "Bar", Color = "#2E86C1", IsDefault = true, Icon = "bi-glass-cocktail" },
    new Tag { Name = "Street Food", Color = "#1B4F72", IsDefault = true, Icon = "bi-truck-flatbed" }, // Food truck vibe
    new Tag { Name = "Fast Food", Color = "#E74C3C", IsDefault = true, Icon = "bi-lightning-charge-fill" },
    new Tag { Name = "Fine Dining", Color = "#C0392B", IsDefault = true, Icon = "bi-gem-fill" },
    new Tag { Name = "Bakery", Color = "#D35400", IsDefault = true, Icon = "bi-egg-fill" },
    new Tag { Name = "Ice Cream", Color = "#F5B7B1", IsDefault = true, Icon = "bi-cone-striped" },
    new Tag { Name = "Nightclub", Color = "#8E44AD", IsDefault = true, Icon = "bi-music-note-beamed" },
    new Tag { Name = "Pub", Color = "#6E2C00", IsDefault = true, Icon = "bi-person-raised-hand" }, // Social vibe

    // Landmarks & Religion
    new Tag { Name = "Attraction", Color = "#E67E22", IsDefault = true, Icon = "bi-star-fill" },
    new Tag { Name = "Church", Color = "#AF7AC5", IsDefault = true, Icon = "bi-heart-fill" },
    new Tag { Name = "Landmark", Color = "#F1C40F", IsDefault = true, Icon = "bi-geo-alt-fill" },
    new Tag { Name = "Monument", Color = "#D4AC0D", IsDefault = true, Icon = "bi-award-fill" },
    new Tag { Name = "Statue", Color = "#B7950B", IsDefault = true, Icon = "bi-person-fill" },
    new Tag { Name = "Castle", Color = "#A93226", IsDefault = true, Icon = "bi-shield-shaded" }, // Shield looks like a crest

    // Shopping
    new Tag { Name = "Shopping Mall", Color = "#16A085", IsDefault = true, Icon = "bi-bag-check-fill" },
    new Tag { Name = "Local Market", Color = "#48C9B0", IsDefault = true, Icon = "bi-basket-fill" },
    new Tag { Name = "Bookstore", Color = "#7FB3D5", IsDefault = true, Icon = "bi-journal-bookmark-fill" },
    new Tag { Name = "Souvenir Shop", Color = "#0E6655", IsDefault = true, Icon = "bi-gift-fill" },

    // Entertainment
    new Tag { Name = "Zoo", Color = "#7DCEA0", IsDefault = true, Icon = "bi-bug-fill" },
    new Tag { Name = "Aquarium", Color = "#76D7C4", IsDefault = true, Icon = "bi-tsunami" },
    new Tag { Name = "Theme Park", Color = "#F5B041", IsDefault = true, Icon = "bi-emoji-laughing-fill" },

    // Geography
    new Tag { Name = "Beach", Color = "#85C1E9", IsDefault = true, Icon = "bi-brightness-high-fill" }, // Sun for beach
    new Tag { Name = "Waterfall", Color = "#2E86C1", IsDefault = true, Icon = "bi-cloud-drizzle-fill" },
    new Tag { Name = "Mountain", Color = "#7FB3D5", IsDefault = true, Icon = "bi-triangle-fill" }, // Triangle looks like a peak

    // Arts & Performance
    new Tag { Name = "Theater", Color = "#DC7633", IsDefault = true, Icon = "bi-masks" },
    new Tag { Name = "Opera House", Color = "#CA6F1E", IsDefault = true, Icon = "bi-megaphone-fill" },
    new Tag { Name = "Concert Hall", Color = "#A04000", IsDefault = true, Icon = "bi-boombox-fill" },

    // Sports
    new Tag { Name = "Stadium", Color = "#52BE80", IsDefault = true, Icon = "bi-flag-fill" },
    new Tag { Name = "Sports Arena", Color = "#239B56", IsDefault = true, Icon = "bi-dribbble" }
);
                }

                context.SaveChanges();
            }
        }
    }
}
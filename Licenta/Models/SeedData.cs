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
                        new Tag { Name = "Museum", Color = "#FF5733", IsDefault = true, Icon = "museum-icon" },
                        new Tag { Name = "Historic Site", Color = "#C0392B", IsDefault = true, Icon = "historic-icon" },
                        new Tag { Name = "Art Gallery", Color = "#9B59B6", IsDefault = true, Icon = "gallery-icon" },

                        new Tag { Name = "Park", Color = "#2ECC71", IsDefault = true, Icon = "park-icon" },
                        new Tag { Name = "Botanical Garden", Color = "#27AE60", IsDefault = true, Icon = "garden-icon" },
                        new Tag { Name = "Nature Reserve", Color = "#1E8449", IsDefault = true, Icon = "nature-icon" },

                        new Tag { Name = "Restaurant", Color = "#3498DB", IsDefault = true, Icon = "restaurant-icon" },
                        new Tag { Name = "Cafe", Color = "#5DADE2", IsDefault = true, Icon = "cafe-icon" },
                        new Tag { Name = "Bar", Color = "#2E86C1", IsDefault = true, Icon = "bar-icon" },
                        new Tag { Name = "Street Food", Color = "#1B4F72", IsDefault = true, Icon = "streetfood-icon" },

                        new Tag { Name = "Church", Color = "#AF7AC5", IsDefault = true, Icon = "church-icon" },

                        new Tag { Name = "Landmark", Color = "#F1C40F", IsDefault = true, Icon = "landmark-icon" },
                        new Tag { Name = "Monument", Color = "#D4AC0D", IsDefault = true, Icon = "monument-icon" },
                        new Tag { Name = "Statue", Color = "#B7950B", IsDefault = true, Icon = "statue-icon" },

                        new Tag { Name = "Castle", Color = "#A93226", IsDefault = true, Icon = "castle-icon" },

                        new Tag { Name = "Shopping Mall", Color = "#16A085", IsDefault = true, Icon = "mall-icon" },
                        new Tag { Name = "Local Market", Color = "#48C9B0", IsDefault = true, Icon = "market-icon" },
                        new Tag { Name = "Souvenir Shop", Color = "#0E6655", IsDefault = true, Icon = "souvenir-icon" },

                        new Tag { Name = "Zoo", Color = "#7DCEA0", IsDefault = true, Icon = "zoo-icon" },
                        new Tag { Name = "Aquarium", Color = "#76D7C4", IsDefault = true, Icon = "aquarium-icon" },
                        new Tag { Name = "Theme Park", Color = "#F5B041", IsDefault = true, Icon = "themepark-icon" },

                        new Tag { Name = "Beach", Color = "#85C1E9", IsDefault = true, Icon = "beach-icon" },
                        new Tag { Name = "Waterfall", Color = "#2E86C1", IsDefault = true, Icon = "waterfall-icon" },
                        new Tag { Name = "Mountain", Color = "#7FB3D5", IsDefault = true, Icon = "mountain-icon" },

                        new Tag { Name = "Theater", Color = "#DC7633", IsDefault = true, Icon = "theater-icon" },
                        new Tag { Name = "Opera House", Color = "#CA6F1E", IsDefault = true, Icon = "opera-icon" },
                        new Tag { Name = "Concert Hall", Color = "#A04000", IsDefault = true, Icon = "concert-icon" },

                        new Tag { Name = "Stadium", Color = "#52BE80", IsDefault = true, Icon = "stadium-icon" },
                        new Tag { Name = "Sports Arena", Color = "#239B56", IsDefault = true, Icon = "arena-icon" }

                    );
                }

                context.SaveChanges();
            }
        }
    }
}
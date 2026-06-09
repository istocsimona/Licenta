using Licenta.Models;
using Licenta.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Licenta.Controllers
{
    [Authorize] // Only logged-in users can access this
    public class UserPreferencesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public UserPreferencesController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // GET: /UserPreferences
        public async Task<IActionResult> Index()
        {
            var userId = _userManager.GetUserId(User);

            // Get all tags and match them with the user's current preferences (if any)
            var tags = await _context.Tags.ToListAsync();
            var userPrefs = await _context.UserTagPreferences
                                          .Where(p => p.UserId == userId)
                                          .ToListAsync();

            var model = tags.Select(t => new TagPreferenceViewModel
            {
                TagId = t.TagId,
                Name = t.Name,
                Color = t.Color,
                Icon = t.Icon,
                // Assign RankOrder if it exists, otherwise leave null
                RankOrder = userPrefs.FirstOrDefault(p => p.TagId == t.TagId)?.RankOrder
            })
            // Order by existing rank first, then by the remaining unranked tags
            .OrderBy(t => t.RankOrder == null ? 1 : 0)
            .ThenBy(t => t.RankOrder)
            .ToList();

            return View(model);
        }

        // POST: /UserPreferences/Save
        [HttpPost]
        public async Task<IActionResult> Save([FromBody] List<TagTierDto> tieredTags)
        {
            var userId = _userManager.GetUserId(User);

            // Delete existing preferences to start fresh
            var existingPrefs = _context.UserTagPreferences.Where(p => p.UserId == userId);
            _context.UserTagPreferences.RemoveRange(existingPrefs);

            if (tieredTags != null && tieredTags.Any())
            {
                foreach (var item in tieredTags)
                {
                    if (item.Tier <= 3) // Only save Must(1), Should(2), and Could(3)
                    {
                        _context.UserTagPreferences.Add(new UserTagPreference
                        {
                            UserId = userId,
                            TagId = item.TagId,
                            RankOrder = item.Tier
                        });
                    }
                }
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Preferences saved successfully!" });
        }

        // POST: /UserPreferences/Reset
        [HttpPost]
        public async Task<IActionResult> Reset()
        {
            var userId = _userManager.GetUserId(User);

            // Delete all preferences for this user
            var existingPrefs = _context.UserTagPreferences.Where(p => p.UserId == userId);
            _context.UserTagPreferences.RemoveRange(existingPrefs);

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }
    }

    public class TagTierDto
    {
        public int TagId { get; set; }
        public int Tier { get; set; }
    }
}
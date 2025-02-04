using DashboardReportApp.Controllers.Attributes;
using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace DashboardReportApp.Controllers
{
    [PasswordProtected(Password = "5intergy")] // Set your password here
    public class ToolingHistoryController : Controller
    {
        private readonly ToolingHistoryService _service;

        public ToolingHistoryController(ToolingHistoryService service)
        {
            _service = service;
        }

        public IActionResult Index()
        {
            // 1. The next group ID from your service
            var nextGroupId = _service.GetNextGroupID();

            // 2. Put it in ViewBag so the Razor can read it
            ViewBag.NextGroupID = nextGroupId;

            // Provide default values for the form
            var toolingHistory = new ToolingHistoryModel
            {
                DateInitiated = DateTime.Today,
                DateDue = DateTime.Today
            };
            var toolingHistories = _service.GetToolingHistories(); // Fetch all records
            ViewBag.ToolingHistories = toolingHistories;          // Pass the list to the view
            return View(toolingHistory);               // Pass an empty model for the form
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Index(ToolingHistoryModel toolingHistory)
        {
            if (ModelState.IsValid)
            {
                _service.AddToolingHistory(toolingHistory); // Add a new record
                return RedirectToAction(nameof(Index));
            }

            // If form submission fails, reload the list and show validation errors
            var toolingHistories = _service.GetToolingHistories();
            ViewBag.ToolingHistories = toolingHistories;
            return View(toolingHistory);
        }

        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(ToolingHistoryModel toolingHistory)
        {
            if (ModelState.IsValid)
            {
                _service.AddToolingHistory(toolingHistory); // Add a new record
                return RedirectToAction(nameof(Index));
            }

            return View(toolingHistory);
        }

        [HttpGet]
        public IActionResult Edit(int id)
        {
            var toolingHistory = _service.GetToolingHistories().Find(x => x.Id == id); // Fetch record by Id
            if (toolingHistory == null)
            {
                return NotFound();
            }

            return View(toolingHistory);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(ToolingHistoryModel toolingHistory)
        {
            if (ModelState.IsValid)
            {
                _service.UpdateToolingHistory(toolingHistory); // Update the record
                return RedirectToAction(nameof(Index));
            }

            return View(toolingHistory);
        }
        [HttpGet]
        public IActionResult GroupDetails(int groupID)
        {
            Console.WriteLine("groupID: " + groupID);
            var groupRecords = _service.GetToolItemsByGroupID(groupID);

            // Initialize NewToolItem with GroupID
            var newToolItem = new ToolItemViewModel
            {
                GroupID = groupID, // Ensure this is set

               // DateDue = DateTime.Today,
               // DateFitted = DateTime.Today

            };

            var model = new GroupDetailsViewModel
            {
                GroupID = groupID,
                ToolItems = groupRecords,
                NewToolItem = newToolItem
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateToolingHistory(ToolingHistoryModel model)
        {
            // If your model is valid, update the existing record
            if (ModelState.IsValid)
            {
                _service.UpdateToolingHistory(model);
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddToolingHistory(ToolingHistoryModel model)
        {
            // If your model is valid, insert a new record
            if (ModelState.IsValid)
            {
                _service.AddToolingHistory(model);
            }
            return RedirectToAction(nameof(Index));
        }

        // AFTER (Fixed):
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddOrEditToolItem(GroupDetailsViewModel model)
        {
            // 'model.NewToolItem' now contains the values posted from the form
            var toolItem = model.NewToolItem;

            Console.WriteLine($"GroupID: {toolItem.GroupID}");
            Console.WriteLine($"ToolNumber: {toolItem.ToolNumber}");
            Console.WriteLine($"ToolDesc: {toolItem.ToolDesc}");
            Console.WriteLine($"Action: {toolItem.Action}");
            Console.WriteLine($"ToolItem: {toolItem.ToolItem}");


            // Otherwise, call your service to add/update the item
            _service.AddToolItem(toolItem);

            // Redirect back to the same group details page
            return RedirectToAction(nameof(GroupDetails), new { groupID = toolItem.GroupID });
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddToolItem(ToolItemViewModel model)
        {
            // model.Id should be 0
            // model.GroupID is the group
            // The rest of the fields (ToolNumber, etc.) are from the new row

            // Insert a new row in the DB:
            if (ModelState.IsValid)
            {
                _service.AddToolItem(model); // a new Insert method
            }

            return RedirectToAction(nameof(GroupDetails), new { groupID = model.GroupID });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateToolItem(ToolItemViewModel model)
        {
            // Log the data you actually got from the form
            Console.WriteLine($"UPDATE (Action) => ID: {model.Id}, ToolNumber: {model.ToolNumber}, Action={model.Action}");

            if (ModelState.IsValid)
            {
                // In your service:
                _service.UpdateToolItem(model);
                return RedirectToAction(nameof(GroupDetails), new { groupID = model.GroupID });
            }
            if (!ModelState.IsValid)
            {
                foreach (var key in ModelState.Keys)
                {
                    var errors = ModelState[key].Errors;
                    foreach (var error in errors)
                    {
                        Console.WriteLine($"Model error for {key}: {error.ErrorMessage}");
                    }
                }
                return RedirectToAction(nameof(GroupDetails), new { groupID = model.GroupID });
            }

            return RedirectToAction(nameof(GroupDetails), new { groupID = model.GroupID });
        }


    }
}

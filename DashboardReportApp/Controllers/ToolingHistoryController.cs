using DashboardReportApp.Models;
using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace DashboardReportApp.Controllers
{
    public class ToolingHistoryController : Controller
    {
        private readonly ToolingHistoryService _service;

        public ToolingHistoryController(ToolingHistoryService service)
        {
            _service = service;
        }

        public IActionResult Index()
        {
            // Provide default values for the form
            var toolingHistory = new ToolingHistory
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
        public IActionResult Index(ToolingHistory toolingHistory)
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
        public IActionResult Create(ToolingHistory toolingHistory)
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
        public IActionResult Edit(ToolingHistory toolingHistory)
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

           

            // Otherwise, call your service to add/update the item
            _service.AddOrUpdateToolItem(toolItem);

            // Redirect back to the same group details page
            return RedirectToAction(nameof(GroupDetails), new { groupID = toolItem.GroupID });
        }

    }
}

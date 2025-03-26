﻿using DashboardReportApp.Services;
using Microsoft.AspNetCore.Mvc;
using System;

namespace DashboardReportApp.Controllers
{
    [Route("Prolink")]
    public class ProlinkController : Controller
    {
        private readonly ProlinkService _prolinkService;

        public ProlinkController(ProlinkService prolinkService)
        {
            _prolinkService = prolinkService;
        }

        [HttpGet("")]
        public IActionResult Index()
        {
            return View();
        }

       [HttpGet("GeneratePdf")]
public IActionResult GeneratePdf(
    string partString, 
    string type, 
    DateTime? startDate, 
    DateTime? endDate, 
    bool onlyOutOfSpec = false)
{
    try
    {
        // Simply let the ProlinkService do the same single call.
        byte[] pdfBytes = _prolinkService.GeneratePdf(
            partString, 
            type, 
            startDate, 
            endDate, 
            onlyOutOfSpec
        );
        return File(pdfBytes, "application/pdf", "report.pdf");
    }
    catch (Exception ex)
    {
        return StatusCode(500, ex.Message);
    }
}



        [HttpGet("QueryData")]
        public IActionResult QueryData(
      string partString,
      string type,
      DateTime? startDate,
      DateTime? endDate,
      bool onlyOutOfSpec = false
  )
        {
            try
            {
                // 1) pivoted data (matching PDF logic)
                var pivotedResults = _prolinkService.GetPivotedData(
                    partString,
                    type,
                    startDate,
                    endDate,
                    onlyOutOfSpec
                );

                // 2) return as JSON
                return Json(new
                {
                    departmentResults = pivotedResults
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }



    }
}

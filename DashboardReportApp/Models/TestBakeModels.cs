using System;
using System.Collections.Generic;

namespace DashboardReportApp.Models
{
    public class TestBakeDensityRow
    {
        public int Index { get; set; } // 1,2,3
        public decimal? DryWeight { get; set; }
        public decimal? WetWeight { get; set; }
        public decimal? Volume { get; set; }
        public decimal? Density { get; set; } // you can compute later
    }

    /// <summary>
    /// One combined row showing:
    /// - Master molding + sintering limits from masterm
    /// - Prolink molding (MTB) nominal/tol/measured
    /// - Prolink sintering (STB) nominal/tol/measured
    /// </summary>
    public class CombinedDimensionRow
    {
        // Master molding “green” (from masterm)
        public string MasterMoldName { get; set; } = "";
        public decimal? MasterMoldLow { get; set; }
        public decimal? MasterMoldHigh { get; set; }
        public decimal? MasterMoldNominal { get; set; }
        public decimal? MasterMoldTolMinus { get; set; }
        public decimal? MasterMoldTolPlus { get; set; }

        // Master sintering “finish” (from masterm)
        public decimal? MasterSinterLow { get; set; }
        public decimal? MasterSinterHigh { get; set; }
        public decimal? MasterSinterNominal { get; set; }
        public decimal? MasterSinterTolMinus { get; set; }
        public decimal? MasterSinterTolPlus { get; set; }

        // Prolink MOLD test bake (Factor = MTB)
        public string MoldName { get; set; } = "";
        public decimal? MoldNominal { get; set; }
        public decimal? MoldTolMinus { get; set; }
        public decimal? MoldTolPlus { get; set; }
        public decimal? MoldMeasurement { get; set; }

        // Prolink SINTER test bake (Factor = STB)
        public string SinterName { get; set; } = "";
        public decimal? SinterNominal { get; set; }
        public decimal? SinterTolMinus { get; set; }
        public decimal? SinterTolPlus { get; set; }
        public decimal? SinterMeasurement { get; set; }
    }

    public class TestBakeHeader
    {
        public DateTime? Date { get; set; }
        public string Part { get; set; } = "";
        public string? Component { get; set; } = "";
        public string ProductionNumber { get; set; } = "";
        public string RunNumber { get; set; } = "";

        public string MachineNumber { get; set; } = "";
        public string Material { get; set; } = "";
        public string LotNumber { get; set; } = "";

        // From toolinginventory
        public string TestedBy { get; set; } = "";
        public string TopPunch { get; set; } = "";
        public string BottomPunch { get; set; } = "";
        public string Die { get; set; } = "";
        public string Pin { get; set; } = "";
        public string ToolNumber { get; set; } = "";

        // user-selected
        public string TestType { get; set; } = "";    // Molding Test Bake / Sintering Test Bake
        public string Reason { get; set; } = "";
    }

    public class TestBakeSinterInfo
    {
        public string SinterOperator { get; set; } = "";
        public DateTime? TimeStartedSinter { get; set; }
        public string Oven { get; set; } = "";
        public decimal? BeltSpeed { get; set; }
        public decimal? PreHeatTemp { get; set; }
        public decimal? HighHeatTemp { get; set; }
        public decimal? DewPoint { get; set; }
        public int? PartsCurrentlyInFurnace { get; set; }
        public string MixNumberOfPartsInFurnace { get; set; } = "";
        public string Method { get; set; } = "";      // belt, ceramic plate, etc.
        public string Orientation { get; set; } = ""; // flange up/down
    }

    public class TestBakeViewModel
    {
        // Scan/search inputs (like HoldTag)
        public string SearchProductionNumber { get; set; } = "";
        public string SearchRunNumber { get; set; } = "";
        public string SearchPart { get; set; } = "";
        public string? SearchComponent { get; set; } = "";

        // User inputs
        public string SearchTestType { get; set; } = "";
        public string SearchReason { get; set; } = "";

        // Data
        public TestBakeHeader Header { get; set; } = new TestBakeHeader();
        public List<string> ToolNumbers { get; set; } = new List<string>();

        public List<TestBakeDensityRow> DensityRows { get; set; } = new List<TestBakeDensityRow>
        {
            new TestBakeDensityRow { Index = 1 },
            new TestBakeDensityRow { Index = 2 },
            new TestBakeDensityRow { Index = 3 }
        };

        public List<CombinedDimensionRow> Dimensions { get; set; } = new List<CombinedDimensionRow>();

        public TestBakeSinterInfo SinterInfo { get; set; } = new TestBakeSinterInfo();

        public bool HasResults { get; set; }
        public string ErrorMessage { get; set; } = "";
        public List<TestBakeLoginRow> ActiveLogins { get; set; } = new();


        public List<TestBakeHeaderRow> HeaderHistory { get; set; } = new();

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 25;
        public int TotalPages { get; set; }
        public int TotalCount { get; set; }


        public int? HeaderId { get; set; }
        public DateTime? TestBakeStartTime { get; set; }


    }
    public class TestBakeLoginRow
    {
        public int Id { get; set; }
        public string Operator { get; set; } = "";
        public DateTime StartTime { get; set; }

        public string Furnace { get; set; } = "";
        public string? ProductionNumber { get; set; }
        public string? RunNumber { get; set; }
        public string? Part { get; set; }
        public string? Component { get; set; }
        public string? TestType { get; set; }
        public string? Reason { get; set; }
    }

    public class TestBakeHeaderRow
    {
        public int Id { get; set; }
        public int LoginId { get; set; }
        public string Operator { get; set; } = "";

        public string? ProductionNumber { get; set; }
        public string? RunNumber { get; set; }
        public string? TestType { get; set; }
        public string? Reason { get; set; }

        public string? ProlinkPart { get; set; }
        public DateTime TestBakeStartTime { get; set; }

        public string? OutcomeStatus { get; set; }
        public string? OutcomeNotes { get; set; }
        public string? OutcomeBy { get; set; }
        public DateTime? OutcomeDate { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public string? FileName { get; set; }
    }

}

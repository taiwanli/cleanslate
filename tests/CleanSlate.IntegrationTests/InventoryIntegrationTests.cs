using FluentAssertions;
using Xunit;
using CleanSlate.Inventory;
using CleanSlate.Leftover;
using CleanSlate.Safety;

namespace CleanSlate.IntegrationTests;

public class InventoryIntegrationTests
{
    [Fact]
    public void Registry_inventory_does_not_throw()
    {
        var inv = new RegistrySoftwareInventory();
        var act = () => inv.Scan();
        act.Should().NotThrow();
    }

    [Fact]
    public void Orphan_registry_scan_returns_list()
    {
        var scanner = new OrphanScanner();
        var items = scanner.FindRegistryOrphans();
        items.Should().NotBeNull();
    }

    [Fact]
    public void Com_firewall_env_scan_is_safe()
    {
        var scanner = new ComFirewallEnvScanner();
        var act = () => scanner.ScanAll(["CleanSlateUnknownAppXYZ"]);
        act.Should().NotThrow();
    }

    [Fact]
    public void History_store_roundtrip()
    {
        var path = Path.Combine(Path.GetTempPath(), "CleanSlateInt", Guid.NewGuid().ToString("N"), "h.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            using var store = new HistoryStore(path);
            store.InsertJob("i1", "Cleanup", "t", 1, 0, 0, null);
            store.ListJobs().Should().Contain(j => j.JobId == "i1");
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { }
        }
    }
}

public class ForceRemovalIntegrationTests
{
    [Fact]
    public void Plan_on_unknown_name_is_empty_or_safe()
    {
        var planner = new Uninstall.ForceRemovalPlanner();
        var plan = planner.Build("CleanSlateNeverInstalledApp", []);
        plan.Targets.Should().NotBeNull();
    }
}

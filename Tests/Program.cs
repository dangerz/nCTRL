using System.Threading.Tasks;

static class Program
{
    static async Task Main()
    {
        await ProtocolTests.Run();
        await KiaProtocolTests.Run();
        FlavorTests.Run();
        await VaultTests.Run();
        VehicleLinkTests.Run();
        DisclaimerTests.Run();
        IndicatorTests.Run();
        CarModelTests.Run();
        SnapshotTests.Run();
        DetailTests.Run();
        HealthTests.Run();
        PlaceTests.Run();
        LastLocationTests.Run();
        PresetTests.Run();
        ChargeLineTests.Run();
        await ColdRangeTests.Run();
        AppThemeTests.Run();
        await RequestBudgetTests.Run();
        await SurroundViewTests.Run();
        await DemoTests.Run();
        await DestinationTests.Run();
        WidgetShareTests.Run();
        await SharedTests.Run();
        await ShareSenderTests.Run();
        await MapTests.Run();
        MapViewTests.Run();
        PullTests.Run();
    }
}

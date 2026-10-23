using System.Threading.Tasks;

static class Program
{
    static async Task Main()
    {
        await ProtocolTests.Run();
        await VaultTests.Run();
        VehicleLinkTests.Run();
    }
}

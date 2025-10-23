using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
namespace PRiR1_PP_MW
{
    class Program
    {
        private const int DEPOSIT_SIZE = 2000;
        private const int VEHICLE_CAPACITY = 200;
        private const int MINING_TIME_PER_UNIT = 10;
        private const int UNLOADING_TIME_PER_UNIT = 10;
        private const int TRANSPORT_TIME = 1000;

        private static int coalInDeposit = DEPOSIT_SIZE;
        private static int coalInWarehouse = 0;

        private static SemaphoreSlim miningSlots = new SemaphoreSlim(2, 2);
        private static SemaphoreSlim warehouseSlot = new SemaphoreSlim(1, 1);
        private static object depositLock = new object();
        private static object warehouseLock = new object();

        static async Task Main(string[] args)
        {
            Console.WriteLine("SYMULACJA WYDOBYCIA WĘGLA\n");

            int[] minerCounts = { 1, 2, 3, 5, 10 };

            foreach (int minerCount in minerCounts)
            {
                coalInDeposit = DEPOSIT_SIZE;
                coalInWarehouse = 0;

                Console.WriteLine($"\nTest z {minerCount} górnikami");

                Stopwatch stopwatch = Stopwatch.StartNew();

                Task[] minerTasks = new Task[minerCount];
                for (int i = 0; i < minerCount; i++)
                {
                    int minerId = i + 1;
                    minerTasks[i] = Task.Run(() => MinerWork(minerId));
                }

                await Task.WhenAll(minerTasks);

                stopwatch.Stop();

                Console.WriteLine($"\nWęgiel przetransportowany :D!");
                Console.WriteLine($"Węgiel w magazynie: {coalInWarehouse} jednostek");
                Console.WriteLine($"Całkowity czas: {stopwatch.Elapsed.TotalSeconds:F2} sekund");
                Console.WriteLine(new string('-', 50));
            }

            Console.ReadKey();
        }

        static async Task MinerWork(int minerId)
        {
            while (true)
            {
                int minedCoal = 0;

                await miningSlots.WaitAsync();

                try
                {
                    lock (depositLock)
                    {
                        if (coalInDeposit == 0)
                        {
                            break;
                        }
                    }

                    Console.WriteLine($"Górnik {minerId} rozpoczyna wydobycie...");

                    while (minedCoal < VEHICLE_CAPACITY)
                    {
                        lock (depositLock)
                        {
                            if (coalInDeposit > 0)
                            {
                                coalInDeposit--;
                                minedCoal++;
                            }
                            else
                            {
                                break;
                            }
                        }

                        await Task.Delay(MINING_TIME_PER_UNIT);
                    }

                    if (minedCoal > 0)
                    {
                        lock (depositLock)
                        {
                            Console.WriteLine($"Górnik {minerId} wydobył {minedCoal} jednostek węgla. " +
                                            $"Pozostało w złożu: {coalInDeposit} jednostek.");
                        }
                    }
                }
                finally
                {
                    miningSlots.Release();
                }

                if (minedCoal == 0)
                {
                    Console.WriteLine($"Górnik {minerId} kończy pracę (brak węgla w złożu).");
                    break;
                }

                Console.WriteLine($"Górnik {minerId} transportuje węgiel do magazynu...");
                await Task.Delay(TRANSPORT_TIME);

                await warehouseSlot.WaitAsync();

                try
                {
                    Console.WriteLine($"Górnik {minerId} rozpoczyna rozładunek...");

                    await Task.Delay(minedCoal * UNLOADING_TIME_PER_UNIT);

                    lock (warehouseLock)
                    {
                        coalInWarehouse += minedCoal;
                        Console.WriteLine($"Górnik {minerId} rozładował {minedCoal} jednostek. " +
                                        $"W magazynie: {coalInWarehouse} jednostek.");
                    }
                }
                finally
                {
                    warehouseSlot.Release();
                }

                Console.WriteLine($"Górnik {minerId} wraca do kopalni...");
                await Task.Delay(TRANSPORT_TIME);
            }
        }
    }
}


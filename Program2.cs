using System.Diagnostics;

namespace PRiR2_PP_MW
{
    enum MinerStatus
    {
        Idle,
        Mining,
        Transporting,
        Unloading,
        Returning,
        Finished
    }

    class Program
    {
        private const int DEPOSIT_SIZE = 2000;
        private const int VEHICLE_CAPACITY = 200;
        private const int MINING_TIME_PER_UNIT = 10;
        private const int UNLOADING_TIME_PER_UNIT = 10;
        private const int TRANSPORT_TIME = 10000;
        private const int UI_UPDATE_INTERVAL = 100;

        private static int coalInDeposit = DEPOSIT_SIZE;
        private static int coalInWarehouse = 0;

        private static Dictionary<int, MinerStatus> minerStatuses = new Dictionary<int, MinerStatus>();
        private static Dictionary<int, int> minerProgress = new Dictionary<int, int>();

        private static SemaphoreSlim miningSlots = new SemaphoreSlim(2, 2);
        private static SemaphoreSlim warehouseSlot = new SemaphoreSlim(1, 1);
        private static object depositLock = new object();
        private static object warehouseLock = new object();
        private static object consoleLock = new object();
        private static object statusLock = new object();

        private static CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private static bool simulationRunning = true;

        private const int STATUS_START_LINE = 3;
        private const int LOG_START_LINE = 15;
        private static int currentLogLine = LOG_START_LINE;

        static async Task Main(string[] args)
        {
            Console.Clear();
            Console.CursorVisible = false;

            lock (consoleLock)
            {
                Console.SetCursorPosition(0, 0);
                Console.WriteLine("=== SYMULACJA WYDOBYCIA WĘGLA ===".PadRight(60));
                Console.WriteLine(new string('=', 60));
            }

            Console.SetCursorPosition(0, LOG_START_LINE);
            Console.Write("Podaj liczbę górników (1-10): ");
            if (!int.TryParse(Console.ReadLine(), out int minerCount) || minerCount < 1 || minerCount > 10)
            {
                minerCount = 5;
            }

            Console.Clear();
            InitializeUI(minerCount);

            for (int i = 1; i <= minerCount; i++)
            {
                minerStatuses[i] = MinerStatus.Idle;
                minerProgress[i] = 0;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();

            Task uiTask = Task.Run(() => UpdateUI(cancellationTokenSource.Token));

            Task[] minerTasks = new Task[minerCount];
            for (int i = 0; i < minerCount; i++)
            {
                int minerId = i + 1;
                minerTasks[i] = Task.Run(() => MinerWork(minerId));
            }

            await Task.WhenAll(minerTasks);

            simulationRunning = false;
            cancellationTokenSource.Cancel();

            await uiTask;

            stopwatch.Stop();

            lock (consoleLock)
            {
                Console.SetCursorPosition(0, currentLogLine++);
                Console.WriteLine(new string('=', 60));
                Console.WriteLine($"SYMULACJA ZAKOŃCZONA!".PadRight(60));
                Console.WriteLine($"Całkowity czas: {stopwatch.Elapsed.TotalSeconds:F2} sekund".PadRight(60));
                Console.WriteLine($"Węgiel w magazynie: {coalInWarehouse} jednostek".PadRight(60));
                Console.WriteLine(new string('=', 60));
            }

            Console.CursorVisible = true;
            Console.ReadKey();
        }

        static void InitializeUI(int minerCount)
        {
            lock (consoleLock)
            {
                Console.SetCursorPosition(0, 0);
                Console.WriteLine("=== SYMULACJA WYDOBYCIA WĘGLA ===".PadRight(60));
                Console.WriteLine(new string('=', 60));
                Console.WriteLine();

                Console.WriteLine("Stan złoża: --- jednostek węgla".PadRight(60));
                Console.WriteLine("Stan magazynu: --- jednostek węgla".PadRight(60));
                Console.WriteLine();
                Console.WriteLine("STATUS GÓRNIKÓW:".PadRight(60));
                Console.WriteLine(new string('-', 60));

                for (int i = 1; i <= minerCount; i++)
                {
                    Console.WriteLine($"Górnik {i}: Oczekuje...".PadRight(60));
                }

                Console.WriteLine(new string('=', 60));
                Console.WriteLine("LOGI:".PadRight(60));
                Console.WriteLine(new string('-', 60));
            }
        }

        static async Task UpdateUI(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && simulationRunning)
            {
                lock (consoleLock)
                {
                    Console.SetCursorPosition(0, STATUS_START_LINE);
                    lock (depositLock)
                    {
                        Console.WriteLine($"Stan złoża: {coalInDeposit} jednostek węgla".PadRight(60));
                    }

                    Console.SetCursorPosition(0, STATUS_START_LINE + 1);
                    lock (warehouseLock)
                    {
                        Console.WriteLine($"Stan magazynu: {coalInWarehouse} jednostek węgla".PadRight(60));
                    }

                    lock (statusLock)
                    {
                        int line = STATUS_START_LINE + 5;
                        foreach (var kvp in minerStatuses)
                        {
                            Console.SetCursorPosition(0, line++);
                            string statusText = GetStatusText(kvp.Key, kvp.Value);
                            Console.WriteLine(statusText.PadRight(60));
                        }
                    }
                }

                await Task.Delay(UI_UPDATE_INTERVAL);
            }
        }

        static string GetStatusText(int minerId, MinerStatus status)
        {
            string statusString = status switch
            {
                MinerStatus.Mining => $"Wydobywa węgiel...",
                MinerStatus.Transporting => "Transportuje do magazynu...",
                MinerStatus.Unloading => $"Rozładowuje węgiel...",
                MinerStatus.Returning => "Wraca do kopalni...",
                MinerStatus.Finished => "Zakończył pracę.",
                _ => "Oczekuje..."
            };

            return $"Górnik {minerId}: {statusString}";
        }

        static void UpdateMinerStatus(int minerId, MinerStatus status, int progress = 0)
        {
            lock (statusLock)
            {
                minerStatuses[minerId] = status;
                minerProgress[minerId] = progress;
            }
        }

        static void LogMessage(string message)
        {
            lock (consoleLock)
            {
                if (currentLogLine < Console.WindowHeight - 2)
                {
                    Console.SetCursorPosition(0, currentLogLine++);
                    Console.WriteLine($"{message}".PadRight(60));
                }
            }
        }

        static async Task MinerWork(int minerId)
        {
            while (true)
            {
                int minedCoal = 0;

                UpdateMinerStatus(minerId, MinerStatus.Idle);
                await miningSlots.WaitAsync();

                try
                {
                    lock (depositLock)
                    {
                        if (coalInDeposit == 0)
                        {
                            UpdateMinerStatus(minerId, MinerStatus.Finished);
                            LogMessage($"Górnik {minerId} kończy pracę (brak węgla).");
                            break;
                        }
                    }

                    UpdateMinerStatus(minerId, MinerStatus.Mining, 0);
                    LogMessage($"Górnik {minerId} wydobywa węgiel...");

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

                        int progress = (minedCoal * 100) / VEHICLE_CAPACITY;
                        UpdateMinerStatus(minerId, MinerStatus.Mining, progress);

                        await Task.Delay(MINING_TIME_PER_UNIT);
                    }

                    if (minedCoal > 0)
                    {
                        LogMessage($"Górnik {minerId} wydobył {minedCoal} jednostek węgla.");
                    }
                }
                finally
                {
                    miningSlots.Release();
                }

                if (minedCoal == 0)
                {
                    UpdateMinerStatus(minerId, MinerStatus.Finished);
                    break;
                }

                UpdateMinerStatus(minerId, MinerStatus.Transporting);
                LogMessage($"Górnik {minerId} transportuje do magazynu...");
                await Task.Delay(TRANSPORT_TIME);

                await warehouseSlot.WaitAsync();

                try
                {
                    UpdateMinerStatus(minerId, MinerStatus.Unloading, 0);
                    LogMessage($"Górnik {minerId} rozładowuje węgiel...");

                    for (int i = 0; i < minedCoal; i++)
                    {
                        await Task.Delay(UNLOADING_TIME_PER_UNIT);
                        int progress = ((i + 1) * 100) / minedCoal;
                        UpdateMinerStatus(minerId, MinerStatus.Unloading, progress);
                    }

                    lock (warehouseLock)
                    {
                        coalInWarehouse += minedCoal;
                    } 

                    LogMessage($"Górnik {minerId} rozładował {minedCoal} jednostek.");
                }
                finally
                {
                    warehouseSlot.Release();
                }

                UpdateMinerStatus(minerId, MinerStatus.Returning);
                LogMessage($"Górnik {minerId} wraca do kopalni...");
                await Task.Delay(TRANSPORT_TIME);
            }
        }
    }
}


// Дочерний процесс для теста восстановления после аварии: бесконечно сохраняет снимки и журнал
// питомца и после каждой подтверждённой транзакции печатает «committed <tick>». Тест убивает
// процесс посреди записи и проверяет базу.
// Аргументы: <путь к базе> <pet id> <seed>.
using System.Globalization;
using Pigeon.Core;
using Pigeon.Persistence.Sqlite;
using Pigeon.Simulation;

if (args.Length != 3)
{
    Console.Error.WriteLine("usage: Pigeon.Persistence.CrashWriter <database> <pet-id> <seed>");
    return 2;
}

var store = await SqlitePetStore.OpenAsync(args[0]);
var petId = new PetId(Guid.Parse(args[1]));
var seed = ulong.Parse(args[2], CultureInfo.InvariantCulture);
var simulation = new PigeonSimulation(seed, SimulationOptions.Desktop);
long savedJournalLength = 0;

while (true)
{
    if (simulation.Tick % 50 == 0)
    {
        simulation.Submit(new UserPoked());
    }

    simulation.RunTicks(7);
    var newEntries = simulation.GetJournalFrom(savedJournalLength);
    await store.SaveAsync(petId, simulation.CreateSnapshot(), newEntries, CancellationToken.None);
    savedJournalLength = simulation.JournalLength;

    Console.Out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"committed {simulation.Tick}"));
    Console.Out.Flush();
}
